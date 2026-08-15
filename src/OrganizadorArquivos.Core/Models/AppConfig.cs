namespace OrganizadorArquivos.Core.Models;

/// <summary>
/// Raiz do config.json — espelha a seção 2 da especificação técnica.
/// </summary>
public class AppConfig
{
    public int VersaoConfig { get; set; } = 1;

    public string DiretorioTemporario { get; set; } = string.Empty;

    /// <summary>Quantas vezes por dia o organizador roda automaticamente (distribuídas em intervalos iguais de 24h/N).</summary>
    public int ExecucoesPorDia { get; set; } = 4;

    /// <summary>Horário da primeira execução do dia. As demais ocorrem a cada 24h/ExecucoesPorDia a partir daqui.</summary>
    public TimeOnly HorarioInicio { get; set; } = new(8, 0);

    public int TempoAusenciaMinutos { get; set; } = 10;

    /// <summary>Janela de cancelamento exibida na notificação de início (padrão: 60s).</summary>
    public int JanelaCancelamentoSegundos { get; set; } = 60;

    /// <summary>Se true, o app é registrado para abrir automaticamente junto com o Windows.</summary>
    public bool IniciarComWindows { get; set; } = false;

    /// <summary>
    /// Diretórios nativos do Windows (Documentos, Área de Trabalho, Downloads, ...).
    /// Nunca remova itens desta lista via código — apenas via ConfigService.LimparPastaPadrao /
    /// ConfigService.RestaurarPastaPadrao.
    /// </summary>
    public List<PastaMonitorada> PastasPadrao { get; set; } = new();

    /// <summary>Pastas criadas livremente pelo usuário — podem ser adicionadas/removidas sem restrição.</summary>
    public List<PastaMonitorada> PastasPersonalizadas { get; set; } = new();

    public Dictionary<string, RegraExtensao> RegrasExtensao { get; set; } = new();

    public BackupConfig Backup { get; set; } = new();

    public string ArquivoFilaPendencias { get; set; } = string.Empty;

    /// <summary>Caminho do recall.json — histórico de arquivos movidos, para poder devolver ao local original.</summary>
    public string ArquivoRecall { get; set; } = string.Empty;

    /// <summary>Pasta onde o token de acesso do Google fica salvo, para não pedir login toda vez.</summary>
    public string PastaCredenciaisGoogleDrive { get; set; } = string.Empty;

    public IEnumerable<PastaMonitorada> TodasAsPastas => PastasPadrao.Concat(PastasPersonalizadas);
}
