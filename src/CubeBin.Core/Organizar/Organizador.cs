using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.Registros;
using CubeBin.Core.Relatorios;

namespace CubeBin.Core.Organizar;

/// <summary>Andamento de uma execução, para a barra de progresso.</summary>
public record Progresso(int Feitos, int Total, string ArquivoAtual);

public class FalhaArquivo
{
    public string Caminho { get; init; } = "";
    public string Motivo { get; init; } = "";
    public string Nome => Path.GetFileName(Caminho);
}

/// <summary>Como terminou uma organização.</summary>
public class ResultadoOrganizacao
{
    public List<Movimento> Movidos { get; } = new();
    public List<FalhaArquivo> Falhas { get; } = new();
    public List<ArquivoPulado> Pulados { get; } = new();
    public List<string> PastasCriadas { get; } = new();
    public List<string> ExtensoesSemPasta { get; } = new();
    public bool Cancelado { get; set; }
    public Cubo? Cubo { get; set; }
    public string? CaminhoRegistro { get; set; }
    public string? CaminhoRelatorio { get; set; }

    public bool NadaAFazer => Movidos.Count == 0 && Falhas.Count == 0;
}

/// <summary>
/// Executa um plano: move cada arquivo DIRETO para a pasta de destino (sem pasta temporária),
/// anotando no registro antes e depois de cada um (PRD 5.1 e 6.3). No fim, gera o relatório HTML e
/// guarda o Cubo. Antes de mexer em cada arquivo, confere de novo as proteções: o arquivo pode ter
/// sido aberto ou alterado entre a prévia e a confirmação.
/// </summary>
public class Organizador
{
    private readonly Ambiente _ambiente;
    private readonly CuboService _cubos;
    private readonly Protecao _protecao;

    public Organizador(Ambiente ambiente, CuboService cubos, Protecao protecao)
    {
        _ambiente = ambiente;
        _cubos = cubos;
        _protecao = protecao;
    }

    public ResultadoOrganizacao Executar(PlanoOrganizacao plano, string modo,
        IProgress<Progresso>? progresso = null, CancellationToken cancelar = default)
    {
        var resultado = new ResultadoOrganizacao();
        resultado.Pulados.AddRange(plano.Pulados);
        resultado.ExtensoesSemPasta.AddRange(plano.ExtensoesSemPasta);
        if (plano.Movimentos.Count == 0) return resultado;

        var cubo = new Cubo { Tipo = TipoCubo.Organizado, Modo = modo };
        var reservados = NomeSemConflito.NovoConjunto();
        var total = plano.Movimentos.Count;

        using (var registro = RegistroExecucao.Criar(_ambiente.PastaRegistros, "organizar", modo, cubo.Id))
        {
            resultado.CaminhoRegistro = registro.Caminho;
            _ambiente.Log($"Organização {(modo == "automatico" ? "automática" : "manual")} começou: {total} arquivo(s) na prévia. Registro: {registro.Caminho}");

            for (var i = 0; i < total; i++)
            {
                var mov = plano.Movimentos[i];
                progresso?.Report(new Progresso(i, total, mov.NomeArquivo));
                if (cancelar.IsCancellationRequested)
                {
                    resultado.Cancelado = true;
                    break;
                }
                MoverUm(mov, registro, resultado, reservados);
            }

            registro.AnotarFim();
        }
        progresso?.Report(new Progresso(resultado.Movidos.Count + resultado.Falhas.Count, total, ""));

        cubo.Arquivos = resultado.Movidos.Count;
        cubo.Bytes = resultado.Movidos.Sum(m => m.Tamanho);
        cubo.Falhas = resultado.Falhas.Count;
        cubo.PorCategoria = resultado.Movidos
            .GroupBy(m => RegrasPadrao.Categoria(Extensoes.DoArquivo(m.Origem)))
            .ToDictionary(g => g.Key, g => g.Count());
        cubo.PastasDeOrigem = resultado.Movidos.Select(m => m.PastaOrigemNome).Distinct().ToList();
        cubo.CaminhoRegistro = resultado.CaminhoRegistro;
        cubo.CaminhoRelatorio = resultado.CaminhoRelatorio = GerarRelatorio(resultado, modo);
        _cubos.Salvar(cubo);
        resultado.Cubo = cubo;

        _ambiente.Log($"Organização {(modo == "automatico" ? "automática" : "manual")} terminou: {resultado.Movidos.Count} movido(s), {resultado.Falhas.Count} falha(s)" +
                      (resultado.Cancelado ? ", cancelada no meio" : "") + ".");
        return resultado;
    }

    private void MoverUm(Movimento mov, RegistroExecucao registro, ResultadoOrganizacao resultado, ISet<string> reservados)
    {
        var info = new FileInfo(mov.Origem);
        if (!info.Exists)
        {
            // Sumiu entre a prévia e agora (a pessoa moveu ou apagou): não é falha do CubeBin.
            resultado.Pulados.Add(new ArquivoPulado { Caminho = mov.Origem, Motivo = MotivoPulado.EmUso });
            return;
        }

        var motivo = _protecao.MotivoParaNaoMover(info);
        if (motivo is not null)
        {
            resultado.Pulados.Add(new ArquivoPulado { Caminho = mov.Origem, Motivo = motivo.Value });
            return;
        }

        if (!Directory.Exists(mov.PastaDestinoCaminho))
        {
            Directory.CreateDirectory(mov.PastaDestinoCaminho);
            resultado.PastasCriadas.Add(mov.PastaDestinoCaminho);
            _ambiente.Log($"Pasta criada: {mov.PastaDestinoCaminho}");
        }

        // O nome da prévia pode ter sido ocupado nesse meio-tempo.
        if (File.Exists(mov.Destino) || Directory.Exists(mov.Destino))
            mov.Destino = NomeSemConflito.Resolver(mov.PastaDestinoCaminho, info.Name, mov.PrefixoOrigem, reservados);
        else
            reservados.Add(mov.Destino);

        var n = registro.AnotarAcao(TipoAcao.Mover, mov.Origem, mov.Destino, mov.PastaOrigemNome, mov.PastaDestinoNome, info.Length);
        try
        {
            // overwrite: false — nunca sobrescreve. Entre discos diferentes o .NET copia e apaga.
            File.Move(mov.Origem, mov.Destino, overwrite: false);
            registro.AnotarResultado(n, ok: true);
            resultado.Movidos.Add(mov);
        }
        catch (Exception ex)
        {
            registro.AnotarResultado(n, ok: false, erro: ex.Message);
            resultado.Falhas.Add(new FalhaArquivo { Caminho = mov.Origem, Motivo = TraduzirErro(ex) });
        }
    }

    private string GerarRelatorio(ResultadoOrganizacao resultado, string modo)
    {
        var dados = new DadosRelatorio
        {
            Operacao = modo == "automatico" ? "Organização automática" : "Organização",
            Linhas = resultado.Movidos.Select(m => new LinhaRelatorio
                {
                    Arquivo = m.NomeFinal, PastaOrigem = m.PastaOrigemNome, CaminhoOrigem = m.Origem,
                    Destino = m.PastaDestinoNome, CaminhoDestino = m.Destino, Tamanho = m.Tamanho,
                })
                .Concat(resultado.Falhas.Select(f => new LinhaRelatorio
                {
                    Arquivo = f.Nome, CaminhoOrigem = f.Caminho,
                    PastaOrigem = Path.GetFileName(Path.GetDirectoryName(f.Caminho)) ?? "",
                    Destino = "—", Falhou = true, Motivo = f.Motivo,
                    Tamanho = TamanhoSeguro(f.Caminho),
                }))
                .ToList(),
        };
        if (resultado.Cancelado) dados.Avisos.Add("A organização foi cancelada no meio. Os arquivos que faltavam ficaram onde estavam.");
        foreach (var pasta in resultado.PastasCriadas)
            dados.Avisos.Add($"A pasta \"{pasta}\" não existia e foi criada.");
        if (resultado.ExtensoesSemPasta.Count > 0)
            dados.Avisos.Add("Tipos sem pasta (ficaram onde estavam): " + string.Join(", ", resultado.ExtensoesSemPasta));

        try { return GeradorRelatorio.Gerar(dados, _ambiente.PastaRelatorios); }
        catch (Exception ex)
        {
            _ambiente.Log($"Não foi possível gerar o relatório: {ex.Message}");
            return "";
        }
    }

    internal static long TamanhoSeguro(string caminho)
    {
        try { return new FileInfo(caminho).Length; } catch { return 0; }
    }

    /// <summary>Mensagens curtas e sem jargão para o relatório e a notificação.</summary>
    public static string TraduzirErro(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "sem permissão para mexer neste arquivo",
        PathTooLongException => "o caminho ficaria comprido demais",
        DirectoryNotFoundException => "a pasta não foi encontrada",
        FileNotFoundException => "o arquivo não foi encontrado",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) => "o disco está cheio",
        IOException => "o arquivo está em uso ou o disco recusou (" + ex.Message + ")",
        _ => ex.Message,
    };
}
