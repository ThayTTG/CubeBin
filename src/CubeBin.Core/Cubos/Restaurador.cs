using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;
using CubeBin.Core.Registros;

namespace CubeBin.Core.Cubos;

public class ResultadoRestauracao
{
    public int Restaurados { get; set; }
    public List<FalhaArquivo> Falhas { get; } = new();
    /// <summary>Arquivos que já não estavam onde o registro dizia (a pessoa mexeu depois).</summary>
    public List<string> NaoEncontrados { get; } = new();
    /// <summary>Arquivos devolvidos com outro nome porque havia um arquivo novo no lugar.</summary>
    public List<string> Renomeados { get; } = new();
}

/// <summary>
/// Desfazer / Restaurar (PRD 5.1, 5.3 e tela de Cubos): lê o registro JSON do Cubo e devolve cada
/// arquivo ao lugar de origem. Regra de conflito: se apareceu um arquivo novo com o mesmo nome na
/// origem, o devolvido ganha o prefixo da pasta de onde está saindo — nunca sobrescreve (PRD 6.3).
/// Cada devolução também é anotada no registro.
/// </summary>
public class Restaurador
{
    private readonly CuboService _cubos;
    private readonly ILixeira? _lixeira;
    private readonly Ambiente _ambiente;

    public Restaurador(Ambiente ambiente, CuboService cubos, ILixeira? lixeira = null)
    {
        _ambiente = ambiente;
        _cubos = cubos;
        _lixeira = lixeira;
    }

    public ResultadoRestauracao Restaurar(Cubo cubo, IProgress<Progresso>? progresso = null)
    {
        var resultado = new ResultadoRestauracao();
        if (cubo.CaminhoRegistro is null || !File.Exists(cubo.CaminhoRegistro))
        {
            resultado.Falhas.Add(new FalhaArquivo { Caminho = cubo.CaminhoRegistro ?? "", Motivo = "o registro deste Cubo não foi encontrado" });
            return resultado;
        }
        if (cubo.Tipo == TipoCubo.ApagadoDeVez)
        {
            resultado.Falhas.Add(new FalhaArquivo { Caminho = "", Motivo = "arquivos apagados de vez não podem ser restaurados" });
            return resultado;
        }

        // Do último para o primeiro, como um "desfazer" de verdade.
        var itens = LeitorRegistro.LerItens(cubo.CaminhoRegistro).Where(i => !i.Restaurado).Reverse().ToList();
        var reservados = NomeSemConflito.NovoConjunto();

        using (var registro = RegistroExecucao.Reabrir(cubo.CaminhoRegistro))
        {
            for (var i = 0; i < itens.Count; i++)
            {
                var item = itens[i];
                progresso?.Report(new Progresso(i, itens.Count, Path.GetFileName(item.Origem)));
                // ok == false: a ação falhou e o arquivo nunca saiu do lugar.
                if (item.Ok == false) continue;

                switch (item.Acao)
                {
                    case TipoAcao.Mover:
                        DevolverMovido(item, registro, resultado, reservados);
                        break;
                    case TipoAcao.Lixeira:
                        DevolverDaLixeira(item, registro, resultado, reservados);
                        break;
                    case TipoAcao.Nuvem:
                        // Restaurar da nuvem baixa o pacote: feito pelo serviço da nuvem (2.0).
                        break;
                }
            }
        }

        if (resultado.Falhas.Count == 0)
        {
            cubo.Restaurado = true;
            cubo.RestauradoEm = DateTime.Now;
            _cubos.Salvar(cubo);
        }
        _ambiente.Log($"Restauração do Cubo {cubo.Id}: {resultado.Restaurados} devolvido(s), " +
                      $"{resultado.Falhas.Count} falha(s), {resultado.NaoEncontrados.Count} não encontrado(s).");
        return resultado;
    }

    private void DevolverMovido(ItemRegistro item, RegistroExecucao registro, ResultadoRestauracao resultado, ISet<string> reservados)
    {
        if (item.Destino is null || !File.Exists(item.Destino))
        {
            // Se o app parou antes de anotar o resultado e o arquivo ainda está na origem, ele nunca saiu.
            if (!File.Exists(item.Origem)) resultado.NaoEncontrados.Add(item.Destino ?? item.Origem);
            return;
        }

        var para = Devolver(item.Destino, item.Origem, item.PastaDestino, resultado, reservados);
        try
        {
            File.Move(item.Destino, para, overwrite: false);
            registro.AnotarRestauracao(item.N, item.Destino, para, ok: true);
            resultado.Restaurados++;
        }
        catch (Exception ex)
        {
            registro.AnotarRestauracao(item.N, item.Destino, para, ok: false, Organizador.TraduzirErro(ex));
            resultado.Falhas.Add(new FalhaArquivo { Caminho = item.Destino, Motivo = Organizador.TraduzirErro(ex) });
        }
    }

    private void DevolverDaLixeira(ItemRegistro item, RegistroExecucao registro, ResultadoRestauracao resultado, ISet<string> reservados)
    {
        if (_lixeira is null || item.Extra is null)
        {
            resultado.Falhas.Add(new FalhaArquivo { Caminho = item.Origem, Motivo = "a Lixeira não está disponível" });
            return;
        }
        if (!_lixeira.Existe(item.Extra))
        {
            resultado.NaoEncontrados.Add(item.Origem);
            return;
        }

        var para = Devolver(null, item.Origem, "Lixeira", resultado, reservados);
        try
        {
            _lixeira.Restaurar(item.Extra, para);
            registro.AnotarRestauracao(item.N, item.Extra, para, ok: true);
            resultado.Restaurados++;
        }
        catch (Exception ex)
        {
            registro.AnotarRestauracao(item.N, item.Extra, para, ok: false, Organizador.TraduzirErro(ex));
            resultado.Falhas.Add(new FalhaArquivo { Caminho = item.Origem, Motivo = Organizador.TraduzirErro(ex) });
        }
    }

    /// <summary>Para onde devolver: a origem, ou um nome com prefixo se a origem estiver ocupada.</summary>
    private static string Devolver(string? de, string origem, string? pastaDeOndeSai, ResultadoRestauracao resultado, ISet<string> reservados)
    {
        var pasta = Path.GetDirectoryName(origem)!;
        Directory.CreateDirectory(pasta);
        var prefixo = PrefixoDoNome(pastaDeOndeSai);
        var para = NomeSemConflito.Resolver(pasta, Path.GetFileName(origem), prefixo, reservados);
        if (!string.Equals(para, origem, StringComparison.Ordinal)) resultado.Renomeados.Add(para);
        return para;
    }

    /// <summary>"Imagens" → "Img"; nome personalizado → as 4 primeiras letras.</summary>
    public static string PrefixoDoNome(string? nomePasta)
    {
        if (string.IsNullOrWhiteSpace(nomePasta)) return "Restaurado";
        foreach (var padrao in PastasDoSistema.Todas)
            if (string.Equals(PastasDoSistema.NomeAmigavel(padrao), nomePasta, StringComparison.OrdinalIgnoreCase))
                return PastasDoSistema.PrefixoPadrao(padrao);
        var letras = new string(nomePasta.Where(char.IsLetterOrDigit).ToArray());
        return letras.Length >= 4 ? letras[..4] : (letras.Length > 0 ? letras : "Restaurado");
    }
}
