using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;
using CubeBin.Core.Registros;
using CubeBin.Core.Relatorios;

namespace CubeBin.Core.LiberarEspaco;

/// <summary>Para onde vão os arquivos (tela "Para onde vão?", PRD 5.3).</summary>
public enum DestinoLiberar
{
    /// <summary>Pacote compactado no Google Drive; só apaga daqui depois de confirmar o envio (recomendado).</summary>
    Nuvem,
    /// <summary>Lixeira do sistema: dá para restaurar.</summary>
    Lixeira,
    /// <summary>Apaga de vez: não dá para desfazer.</summary>
    ApagarDeVez,
}

/// <summary>Um arquivo ou pasta que vai sair do computador.</summary>
public class ItemLiberar
{
    public string Caminho { get; init; } = "";
    public bool EhPasta { get; init; }
    public long Tamanho { get; init; }
    public int Arquivos { get; init; }
    public string PastaOrigemNome { get; init; } = "";
    public Dictionary<string, int> PorCategoria { get; init; } = new();
    public string Nome => Path.GetFileName(Caminho);
}

/// <summary>O que foi escolhido para liberar espaço (vem de "Esvaziar uma pasta" ou "Escolher arquivos").</summary>
public class SelecaoLiberar
{
    /// <summary>"Esvaziar Downloads", "12 arquivos escolhidos"...</summary>
    public string Descricao { get; init; } = "";
    public List<ItemLiberar> Itens { get; } = new();
    public List<ArquivoPulado> Pulados { get; } = new();
    /// <summary>Itens recusados por segurança (pasta padrão, pasta do sistema...).</summary>
    public List<(string Caminho, string Motivo)> Recusados { get; } = new();
    public List<string> PastasDeProjeto { get; } = new();
    /// <summary>Preenchido em "Esvaziar uma pasta".</summary>
    public PastaConfig? PastaEsvaziada { get; init; }

    public long Bytes => Itens.Sum(i => i.Tamanho);
    public int Arquivos => Itens.Sum(i => i.Arquivos);
    public Dictionary<string, int> PorCategoria => Itens.SelectMany(i => i.PorCategoria)
        .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
}

public class ResultadoLiberar
{
    public DestinoLiberar Destino { get; init; }
    public List<ItemLiberar> Feitos { get; } = new();
    public List<FalhaArquivo> Falhas { get; } = new();
    public bool Cancelado { get; set; }
    public Cubo? Cubo { get; set; }
    public string? CaminhoRelatorio { get; set; }
    /// <summary>Preenchido no envio para a nuvem: por que foi adiado (nenhum arquivo local foi apagado).</summary>
    public string? MotivoAdiamento { get; set; }
    public long BytesLiberados => Feitos.Sum(i => i.Tamanho);
}

/// <summary>
/// O módulo Liberar Espaço (PRD 5.2 e 5.3). Regras de segurança, valendo sempre:
/// - pastas padrão podem ser esvaziadas, mas nunca apagadas (nem a pasta, nem uma pasta que contenha uma delas);
/// - pastas do sistema, a raiz do disco e a pasta de dados do CubeBin são recusadas;
/// - ao esvaziar, ficam de fora arquivos ocultos/do sistema, em uso e pastas de projeto;
/// - cada item é anotado no registro antes de sair, e o Cubo guarda o caminho de volta.
/// </summary>
public class Liberador
{
    private readonly Ambiente _ambiente;
    private readonly CuboService _cubos;
    private readonly ILixeira? _lixeira;

    public Liberador(Ambiente ambiente, CuboService cubos, ILixeira? lixeira)
    {
        _ambiente = ambiente;
        _cubos = cubos;
        _lixeira = lixeira;
    }

    // ===================== Escolher =====================

    /// <summary>"Esvaziar uma pasta": tudo o que está dentro dela (a pasta em si continua existindo).</summary>
    public static SelecaoLiberar SelecionarConteudoDaPasta(PastaConfig pasta, Configuracoes config, IArquivosEmUso? emUso = null)
    {
        var caminho = pasta.CaminhoEfetivo();
        var selecao = new SelecaoLiberar { Descricao = $"Esvaziar {pasta.Nome}", PastaEsvaziada = pasta };
        if (!Directory.Exists(caminho)) return selecao;
        emUso ??= ArquivosEmUso.DoSistema();

        var outrasPadrao = config.Pastas.Where(p => p.EhPadrao && p.Id != pasta.Id).Select(p => p.CaminhoEfetivo()).ToList();
        var dir = new DirectoryInfo(caminho);

        foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (Protecao.EhOcultoOuDoSistema(sub)) continue;
            if (outrasPadrao.Any(p => ConfiguracaoService.EstaDentro(p, sub.FullName)))
            {
                selecao.Recusados.Add((sub.FullName, "contém uma pasta padrão do sistema"));
                continue;
            }
            if (Protecao.EhPastaDeProjeto(sub.FullName, config.Protecoes.MarcadoresDeProjeto))
            {
                selecao.PastasDeProjeto.Add(sub.FullName);
                continue;
            }
            selecao.Itens.Add(DescreverPasta(sub, pasta.Nome));
        }

        foreach (var arquivo in dir.GetFiles().OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (Protecao.EhOcultoOuDoSistema(arquivo)) continue;
            if (emUso.EstaEmUso(arquivo.FullName))
            {
                selecao.Pulados.Add(new ArquivoPulado { Caminho = arquivo.FullName, Motivo = MotivoPulado.EmUso });
                continue;
            }
            selecao.Itens.Add(DescreverArquivo(arquivo, pasta.Nome));
        }
        return selecao;
    }

    /// <summary>"Escolher arquivos": os arquivos (ou pastas) que a pessoa escolheu ou arrastou.</summary>
    public static SelecaoLiberar SelecionarArquivos(IEnumerable<string> caminhos, Configuracoes config, string? pastaDeDadosDoApp = null)
    {
        var selecao = new SelecaoLiberar();
        var vistos = NomeSemConflito.NovoConjunto();
        foreach (var bruto in caminhos)
        {
            string caminho;
            try { caminho = Path.GetFullPath(bruto).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { continue; }
            if (!vistos.Add(caminho)) continue;

            var motivo = MotivoParaRecusar(caminho, config, pastaDeDadosDoApp);
            if (motivo is not null)
            {
                selecao.Recusados.Add((caminho, motivo));
                continue;
            }
            var origem = config.Pastas.FirstOrDefault(p => ConfiguracaoService.EstaDentro(caminho, p.CaminhoEfetivo()))?.Nome
                         ?? Path.GetFileName(Path.GetDirectoryName(caminho)) ?? "";
            if (Directory.Exists(caminho)) selecao.Itens.Add(DescreverPasta(new DirectoryInfo(caminho), origem));
            else if (File.Exists(caminho)) selecao.Itens.Add(DescreverArquivo(new FileInfo(caminho), origem));
        }
        // Itens dentro de uma pasta que também foi escolhida saem junto com ela.
        var pastas = selecao.Itens.Where(i => i.EhPasta).Select(i => i.Caminho).ToList();
        selecao.Itens.RemoveAll(i => pastas.Any(p => p != i.Caminho && ConfiguracaoService.EstaDentro(i.Caminho, p)));

        var n = selecao.Itens.Count;
        return new SelecaoLiberar { Descricao = n == 1 ? "1 item escolhido" : $"{n} itens escolhidos" }.Juntar(selecao);
    }

    /// <summary>Por que um caminho não pode sair do computador (ou null se pode).</summary>
    public static string? MotivoParaRecusar(string caminho, Configuracoes config, string? pastaDeDadosDoApp)
    {
        if (!File.Exists(caminho) && !Directory.Exists(caminho)) return "não existe mais";
        var raiz = Path.GetPathRoot(caminho + Path.DirectorySeparatorChar)?.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(caminho.TrimEnd('\\', '/')) || caminho.TrimEnd('\\', '/') == raiz)
            return "é a raiz do disco";

        foreach (var pasta in config.Pastas.Where(p => p.EhPadrao))
            if (ConfiguracaoService.EstaDentro(pasta.CaminhoEfetivo(), caminho))
                return pasta.CaminhoEfetivo() == caminho || ConfiguracaoService.MesmoCaminho(pasta.CaminhoEfetivo(), caminho)
                    ? $"é a pasta padrão {pasta.Nome} (pastas padrão podem ser esvaziadas, nunca apagadas)"
                    : $"contém a pasta padrão {pasta.Nome}";

        if (ConfiguracaoService.MesmoCaminho(caminho, PastasDoSistema.ObterHome()) || ConfiguracaoService.EstaDentro(PastasDoSistema.ObterHome(), caminho))
            return "é (ou contém) a pasta pessoal";
        if (pastaDeDadosDoApp is not null && (ConfiguracaoService.EstaDentro(caminho, pastaDeDadosDoApp) || ConfiguracaoService.EstaDentro(pastaDeDadosDoApp, caminho)))
            return "é a pasta onde o CubeBin guarda os registros (sem ela não há como restaurar)";
        if (Directory.Exists(caminho) && ConfiguracaoService.ValidarPastaPersonalizada(new Configuracoes { Pastas = new(), Regras = new() }, caminho) is { } problema
            && problema.Contains("sistema"))
            return "é uma pasta do sistema ou de programas";
        return null;
    }

    private static ItemLiberar DescreverArquivo(FileInfo arquivo, string origem) => new()
    {
        Caminho = arquivo.FullName, Tamanho = arquivo.Length, Arquivos = 1, PastaOrigemNome = origem,
        PorCategoria = new() { [RegrasPadrao.Categoria(arquivo.Extension)] = 1 },
    };

    private static ItemLiberar DescreverPasta(DirectoryInfo pasta, string origem)
    {
        long tamanho = 0;
        var arquivos = 0;
        var categorias = new Dictionary<string, int>();
        try
        {
            foreach (var arquivo in pasta.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                tamanho += arquivo.Length;
                arquivos++;
                var categoria = RegrasPadrao.Categoria(arquivo.Extension);
                categorias[categoria] = categorias.GetValueOrDefault(categoria) + 1;
            }
        }
        catch { /* sem permissão em parte da pasta: conta o que deu */ }
        return new ItemLiberar
        {
            Caminho = pasta.FullName, EhPasta = true, Tamanho = tamanho, Arquivos = arquivos,
            PastaOrigemNome = origem, PorCategoria = categorias,
        };
    }

    // ===================== Executar (Lixeira e Apagar de vez) =====================

    public ResultadoLiberar Executar(SelecaoLiberar selecao, DestinoLiberar destino,
        IProgress<Progresso>? progresso = null, CancellationToken cancelar = default)
    {
        if (destino == DestinoLiberar.Nuvem)
            throw new InvalidOperationException("O envio para a nuvem é feito pelo EnvioNuvem.");
        if (destino == DestinoLiberar.Lixeira && _lixeira is null)
            throw new InvalidOperationException("A Lixeira não está disponível neste sistema.");

        var resultado = new ResultadoLiberar { Destino = destino };
        if (selecao.Itens.Count == 0) return resultado;

        var tipo = destino == DestinoLiberar.Lixeira ? TipoCubo.Lixeira : TipoCubo.ApagadoDeVez;
        var acao = destino == DestinoLiberar.Lixeira ? TipoAcao.Lixeira : TipoAcao.ApagarDeVez;
        var cubo = new Cubo { Tipo = tipo };
        string caminhoRegistro;

        using (var registro = RegistroExecucao.Criar(_ambiente.PastaRegistros, "liberar", "manual", cubo.Id))
        {
            caminhoRegistro = registro.Caminho;
            _ambiente.Log($"Liberar espaço ({destino}) começou: {selecao.Descricao}, {selecao.Itens.Count} item(ns).");
            for (var i = 0; i < selecao.Itens.Count; i++)
            {
                var item = selecao.Itens[i];
                progresso?.Report(new Progresso(i, selecao.Itens.Count, item.Nome));
                if (cancelar.IsCancellationRequested)
                {
                    resultado.Cancelado = true;
                    break;
                }

                var n = registro.AnotarAcao(acao, item.Caminho, null, item.PastaOrigemNome,
                    destino == DestinoLiberar.Lixeira ? "Lixeira" : "Apagado de vez", item.Tamanho);
                try
                {
                    string? chave = null;
                    if (destino == DestinoLiberar.Lixeira) chave = _lixeira!.Enviar(item.Caminho);
                    else if (item.EhPasta) Directory.Delete(item.Caminho, recursive: true);
                    else File.Delete(item.Caminho);
                    registro.AnotarResultado(n, ok: true, extra: chave);
                    resultado.Feitos.Add(item);
                }
                catch (Exception ex)
                {
                    registro.AnotarResultado(n, ok: false, erro: ex.Message);
                    resultado.Falhas.Add(new FalhaArquivo { Caminho = item.Caminho, Motivo = Organizador.TraduzirErro(ex) });
                }
            }
            registro.AnotarFim();
        }
        progresso?.Report(new Progresso(resultado.Feitos.Count + resultado.Falhas.Count, selecao.Itens.Count, ""));

        PreencherCubo(cubo, resultado.Feitos, resultado.Falhas.Count, caminhoRegistro);
        cubo.CaminhoRelatorio = resultado.CaminhoRelatorio = GerarRelatorio(selecao, resultado,
            destino == DestinoLiberar.Lixeira ? "Lixeira" : "Apagado de vez");
        _cubos.Salvar(cubo);
        resultado.Cubo = cubo;
        _ambiente.Log($"Liberar espaço ({destino}) terminou: {resultado.Feitos.Count} item(ns), {resultado.Falhas.Count} falha(s).");
        return resultado;
    }

    internal static void PreencherCubo(Cubo cubo, List<ItemLiberar> feitos, int falhas, string caminhoRegistro)
    {
        cubo.Arquivos = feitos.Sum(i => i.Arquivos);
        cubo.Bytes = feitos.Sum(i => i.Tamanho);
        cubo.Falhas = falhas;
        cubo.PorCategoria = feitos.SelectMany(i => i.PorCategoria).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
        cubo.PastasDeOrigem = feitos.Select(i => i.PastaOrigemNome).Where(n => n.Length > 0).Distinct().ToList();
        cubo.CaminhoRegistro = caminhoRegistro;
    }

    internal string GerarRelatorio(SelecaoLiberar selecao, ResultadoLiberar resultado, string nomeDestino)
    {
        var dados = new DadosRelatorio
        {
            Operacao = $"Liberar espaço · {selecao.Descricao}",
            ColunaDestino = "Para onde foi",
            RotuloSucesso = "itens liberados",
            Linhas = resultado.Feitos.Select(i => new LinhaRelatorio
                {
                    Arquivo = i.EhPasta ? $"{i.Nome} (pasta, {i.Arquivos} arquivos)" : i.Nome,
                    PastaOrigem = i.PastaOrigemNome, CaminhoOrigem = i.Caminho, Destino = nomeDestino, Tamanho = i.Tamanho,
                })
                .Concat(resultado.Falhas.Select(f => new LinhaRelatorio
                {
                    Arquivo = f.Nome, CaminhoOrigem = f.Caminho, PastaOrigem = Path.GetFileName(Path.GetDirectoryName(f.Caminho)) ?? "",
                    Destino = "—", Falhou = true, Motivo = f.Motivo, Tamanho = Organizador.TamanhoSeguro(f.Caminho),
                }))
                .ToList(),
        };
        if (resultado.Destino == DestinoLiberar.ApagarDeVez)
            dados.Avisos.Add("Estes arquivos foram apagados de vez e não podem ser restaurados.");
        if (resultado.MotivoAdiamento is { } motivo)
            dados.Avisos.Add($"Envio adiado: {motivo}. Nenhum arquivo foi apagado do computador.");
        if (resultado.Cancelado) dados.Avisos.Add("Interrompido no meio: o que faltava ficou no computador.");
        foreach (var projeto in selecao.PastasDeProjeto)
            dados.Avisos.Add($"\"{Path.GetFileName(projeto)}\" parece um projeto e ficou de fora.");

        try { return GeradorRelatorio.Gerar(dados, _ambiente.PastaRelatorios); }
        catch (Exception ex)
        {
            _ambiente.Log($"Não foi possível gerar o relatório: {ex.Message}");
            return "";
        }
    }
}

internal static class SelecaoExtensoes
{
    public static SelecaoLiberar Juntar(this SelecaoLiberar destino, SelecaoLiberar origem)
    {
        destino.Itens.AddRange(origem.Itens);
        destino.Pulados.AddRange(origem.Pulados);
        destino.Recusados.AddRange(origem.Recusados);
        destino.PastasDeProjeto.AddRange(origem.PastasDeProjeto);
        return destino;
    }
}
