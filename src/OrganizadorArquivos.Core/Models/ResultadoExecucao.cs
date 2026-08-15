namespace OrganizadorArquivos.Core.Models;

/// <summary>Resumo de uma execução do organizador, usado para montar a notificação final e o log.</summary>
public class ResultadoExecucao
{
    public DateTime InicioEm { get; set; }
    public DateTime FimEm { get; set; }
    public int ArquivosMovidos { get; set; }
    public int ArquivosComFalha { get; set; }
    public int ArquivosPendentes { get; set; }
    public string? CaminhoBackupCsv { get; set; }
    public List<(string Arquivo, string Motivo)> Falhas { get; set; } = new();

    /// <summary>Avisos não-críticos (ex.: falha ao registrar no recall.json) que não impedem a
    /// organização de ser considerada bem-sucedida, mas valem ser reportados para diagnóstico.</summary>
    public List<string> AvisosRecall { get; set; } = new();

    public bool Cancelado { get; set; }

    public bool Sucesso => !Cancelado && ArquivosComFalha == 0;
}
