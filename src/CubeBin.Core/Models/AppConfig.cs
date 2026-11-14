using Comum;

namespace CubeBin.Core.Models;

/// <summary>
/// Raiz do config.json do CubeBin a partir da versão 3, quando a organização virou um app à parte.
/// Aqui fica só o que é da faxina e da nuvem: as pastas que o agente analisa, o agente de
/// sugestões, os registros do que foi apagado e o backup no Google Drive (os Cubos).
/// Os nomes dos campos que já existiam na 2.x continuam iguais, para a migração ler o arquivo antigo.
/// </summary>
public class AppConfig
{
    /// <summary>3 = config só da faxina (a 2.x gravava 1 e tinha a organização junto).</summary>
    public int VersaoConfig { get; set; } = 3;

    /// <summary>Quanto tempo sem mexer no computador para o agente poder analisar sozinho.</summary>
    public int TempoAusenciaMinutos { get; set; } = 10;

    /// <summary>Se true, o app é registrado para abrir automaticamente junto com o Windows.</summary>
    public bool IniciarComWindows { get; set; } = false;

    /// <summary>As pastas onde o agente procura o que limpar (nunca o disco inteiro).</summary>
    public List<PastaAnalisada> PastasAnalisadas { get; set; } = new();

    /// <summary>Onde ficam os registros em CSV do que foi apagado, e quantos manter.</summary>
    public BackupConfig Backup { get; set; } = new();

    // ===================== Agente de sugestões =====================

    /// <summary>Desativado por padrão — o agente só roda se o usuário ligar explicitamente.</summary>
    public bool AgenteAtivo { get; set; } = false;

    /// <summary>Quantas vezes por dia o agente analisa as pastas.</summary>
    public int AgenteExecucoesPorDia { get; set; } = 1;

    /// <summary>Horário da primeira análise do dia.</summary>
    public TimeOnly AgenteHorarioInicio { get; set; } = new(3, 0);

    /// <summary>Dias sem "toque" (acesso ou escrita) para um arquivo temporário/cache virar sugestão.</summary>
    public int AgenteDiasTemporarioNaoUsado { get; set; } = 30;

    /// <summary>Dias parado para um arquivo grande virar sugestão.</summary>
    public int AgenteDiasArquivoGrandeParado { get; set; } = 180;

    /// <summary>Tamanho mínimo, em bytes, para um arquivo ser considerado "grande" pelo agente.</summary>
    public long AgenteTamanhoMinimoGrandeBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>Caminho do sugestoes.json — sugestões pendentes/aprovadas/ignoradas do agente.</summary>
    public string ArquivoSugestoes { get; set; } = string.Empty;

    // ===================== Backup na nuvem (os Cubos) =====================

    /// <summary>Pasta onde o token de acesso do Google fica salvo, para não pedir login toda vez.</summary>
    public string PastaCredenciaisGoogleDrive { get; set; } = string.Empty;

    /// <summary>Pasta no Google Drive onde os pacotes de backup (.zip) são salvos.</summary>
    public string CaminhoDriveBackups { get; set; } = "CubeBin/Backups";

    /// <summary>Caminho do pacotes.json — histórico de pacotes de backup enviados (os "Cubos").</summary>
    public string ArquivoPacotesBackup { get; set; } = string.Empty;
}
