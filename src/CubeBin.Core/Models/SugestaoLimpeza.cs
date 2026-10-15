namespace CubeBin.Core.Models;

/// <summary>
/// Uma sugestão de limpeza gerada pelo agente — nunca resulta em exclusão automática. O usuário
/// aprova ou ignora item a item (ou em lote) numa tela dedicada antes de qualquer arquivo sumir.
/// </summary>
public class SugestaoLimpeza
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = string.Empty;
    public string Caminho { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public TipoSugestao Tipo { get; set; }

    /// <summary>Texto legível explicando o motivo (ex.: "Duplicado de C:\...\outro.zip", "Não acessado há 132 dias").</summary>
    public string Motivo { get; set; } = string.Empty;

    /// <summary>Só preenchido quando Tipo é Duplicado: caminho do arquivo "original" que foi mantido.</summary>
    public string? CaminhoDuplicadoDe { get; set; }

    public DateTime DetectadoEm { get; set; } = DateTime.Now;

    /// <summary>"Pendente" | "Aprovado" | "Ignorado" | "Excluido".</summary>
    public string Status { get; set; } = "Pendente";
}
