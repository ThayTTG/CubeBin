using System.Text.Json.Serialization;
using CubeBin.Core.Plataforma;

namespace CubeBin.Core.Configuracao;

/// <summary>
/// Tudo o que a pessoa pode ajustar, salvo em <c>configuracoes.json</c> (legível e editável à mão,
/// PRD 7). As regras padrão já vêm prontas: ninguém precisa cadastrar extensão para começar.
/// </summary>
public class Configuracoes
{
    public int VersaoConfig { get; set; } = 1;

    /// <summary>As seis pastas padrão do sistema e as pastas personalizadas.</summary>
    public List<PastaConfig> Pastas { get; set; } = new();

    /// <summary>Para onde vai cada extensão. Cada regra aponta para uma pasta de <see cref="Pastas"/>.
    /// Se a chave não existir no JSON (null), as regras padrão são usadas; uma lista vazia é respeitada.</summary>
    public List<Regra> Regras { get; set; } = null!;

    /// <summary>Extensões encontradas que ainda não têm pasta: a pessoa decide na próxima vez que abrir
    /// a tela de regras (PRD 5.1). Os arquivos ficam onde estão até lá.</summary>
    public List<string> ExtensoesSemPasta { get; set; } = new();

    /// <summary>Extensões que a pessoa mandou deixar sempre onde estão.</summary>
    public List<string> ExtensoesIgnoradas { get; set; } = new();

    public Automacao Automacao { get; set; } = new();

    public Protecoes Protecoes { get; set; } = new();

    public NuvemConfig Nuvem { get; set; } = new();

    public AgenteConfig Agente { get; set; } = new();

    // ---------- Consultas ----------

    public PastaConfig? ObterPasta(string id) => Pastas.FirstOrDefault(p => p.Id == id);

    public IEnumerable<PastaConfig> PastasMonitoradas => Pastas.Where(p => p.Monitorar);

    /// <summary>A regra de uma extensão (".jpg"), ou null se ela não tem pasta.</summary>
    public Regra? RegraDaExtensao(string extensao)
    {
        var ext = Extensoes.Normalizar(extensao);
        return Regras.FirstOrDefault(r => r.Extensoes.Any(e => Extensoes.Normalizar(e) == ext));
    }
}

/// <summary>Uma pasta conhecida pelo CubeBin: padrão do sistema ou personalizada.</summary>
public class PastaConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>Preenchido nas pastas padrão: o caminho é perguntado ao sistema a cada uso
    /// (assim continua certo se o OneDrive ou o XDG mudarem a pasta de lugar).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PastaPadrao? Padrao { get; set; }

    /// <summary>Caminho das pastas personalizadas (nas padrão fica vazio).</summary>
    public string? Caminho { get; set; }

    /// <summary>Se o CubeBin organiza os arquivos que caem aqui.</summary>
    public bool Monitorar { get; set; }

    /// <summary>Organizar também as subpastas (opcional, com aviso de risco: PRD 5.1).</summary>
    public bool OrganizarSubpastas { get; set; }

    /// <summary>Prefixo nos conflitos de nome ("Doc-Texto"). Vazio = gerado a partir do nome.</summary>
    public string? Prefixo { get; set; }

    [JsonIgnore]
    public bool EhPadrao => Padrao is not null;

    /// <summary>Pastas padrão nunca são removidas nem apagadas, só esvaziadas (PRD 5.3 e 6).</summary>
    [JsonIgnore]
    public bool PodeRemover => !EhPadrao;

    public string CaminhoEfetivo() =>
        Padrao is { } padrao ? PastasDoSistema.Obter(padrao) : (Caminho ?? "");

    public string PrefixoEfetivo()
    {
        if (!string.IsNullOrWhiteSpace(Prefixo)) return Prefixo!;
        if (Padrao is { } padrao) return PastasDoSistema.PrefixoPadrao(padrao);
        var letras = new string(Nome.Where(char.IsLetterOrDigit).ToArray());
        return letras.Length >= 4 ? letras[..4] : (letras.Length > 0 ? letras : "Pasta");
    }
}

/// <summary>Uma regra: estas extensões vão para aquela pasta.</summary>
public class Regra
{
    public string PastaId { get; set; } = "";
    public List<string> Extensoes { get; set; } = new();
}

/// <summary>Execução em segundo plano quando a pessoa está ausente (PRD 5.4).</summary>
public class Automacao
{
    /// <summary>Desligada na primeira vez (a opção mais segura): a pessoa liga quando quiser.</summary>
    public bool Ligada { get; set; }

    /// <summary>Tempo T sem mexer no computador antes de organizar.</summary>
    public int MinutosDeAusencia { get; set; } = 10;

    /// <summary>Tempo para cancelar depois do aviso (PRD: 60 s).</summary>
    public int SegundosParaCancelar { get; set; } = 60;

    /// <summary>Intervalo mínimo entre duas organizações automáticas.</summary>
    public int HorasEntreExecucoes { get; set; } = 4;

    public bool IniciarComOSistema { get; set; }

    public DateTime? UltimaExecucaoAutomatica { get; set; }

    /// <summary>Métricas do PRD 10: quantas organizações automáticas rodaram e quantas a pessoa
    /// cancelou (indica se o momento escolhido incomoda).</summary>
    public int ExecucoesAutomaticas { get; set; }
    public int ExecucoesCanceladas { get; set; }
}

/// <summary>As proteções do PRD 6.3. Todas as listas podem ser editadas no JSON.</summary>
public class Protecoes
{
    /// <summary>
    /// Uma pasta com qualquer um destes marcadores é um projeto e o CubeBin nunca mexe dentro dela.
    /// "*.sln" = algum arquivo com essa extensão; "package.json" = arquivo com esse nome;
    /// ".git/" = subpasta com esse nome; "Assets/+ProjectSettings/" = todos juntos (Unity).
    /// </summary>
    public List<string> MarcadoresDeProjeto { get; set; } = new()
    {
        "*.c3p", "*.sln", "*.csproj", "package.json", ".git/", "Assets/+ProjectSettings/",
    };

    /// <summary>Atalhos: nunca são movidos.</summary>
    public List<string> ExtensoesDeAtalho { get; set; } = new() { ".lnk", ".url", ".desktop" };

    /// <summary>Executáveis e instaladores: nunca são movidos (podem ser programas portáteis).</summary>
    public List<string> ExtensoesExecutaveis { get; set; } = new()
    {
        ".exe", ".msi", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".jar", ".appx", ".msix",
        ".sh", ".run", ".appimage", ".bin",
    };

    /// <summary>Downloads em andamento e temporários: nunca são movidos.</summary>
    public List<string> ExtensoesDeDownloadIncompleto { get; set; } = new()
    {
        ".part", ".crdownload", ".download", ".partial", ".opdownload", ".tmp", ".temp", "~",
    };

    /// <summary>Arquivos alterados há menos que isso ficam para a próxima vez.</summary>
    public int MinutosSemAlteracao { get; set; } = 5;

    /// <summary>Acima disso, numa pasta sincronizada com o OneDrive, o CubeBin avisa antes de mover.</summary>
    public int LimiteAvisoOneDriveArquivos { get; set; } = 100;
}

/// <summary>Google Drive (versão 2.0).</summary>
public class NuvemConfig
{
    /// <summary>E-mail da conta conectada (só para mostrar; o token fica no cofre do sistema).</summary>
    public string? ContaConectada { get; set; }
}

/// <summary>Agente de sugestões de limpeza (versão 2.0). Desligado por padrão.</summary>
public class AgenteConfig
{
    public bool Ligado { get; set; }
    public int DiasTemporarioParado { get; set; } = 30;
    public int DiasArquivoGrandeParado { get; set; } = 180;
    public int TamanhoMinimoGrandeMb { get; set; } = 500;
    public DateTime? UltimaAnalise { get; set; }
}
