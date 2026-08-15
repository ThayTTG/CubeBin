namespace OrganizadorArquivos.Core.Models;

/// <summary>
/// Registro de um arquivo movido pelo organizador, guardado em recall.json — permite devolver
/// o arquivo ao local original a qualquer momento, como uma rede de segurança extra além do
/// backup em CSV (que continua existindo e não muda com esta funcionalidade).
/// </summary>
public class ItemRecall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string NomeArquivo { get; set; } = string.Empty;

    /// <summary>Caminho completo de onde o arquivo veio antes de ser organizado.</summary>
    public string CaminhoOrigem { get; set; } = string.Empty;

    /// <summary>Caminho completo de onde o arquivo está agora, depois de organizado.</summary>
    public string CaminhoAtual { get; set; } = string.Empty;

    public DateTime MovidoEm { get; set; } = DateTime.Now;

    /// <summary>"Local" (padrão) ou "GoogleDrive" — de onde esse arquivo precisa ser devolvido.</summary>
    public string Armazenamento { get; set; } = "Local";

    /// <summary>ID do arquivo no Google Drive, necessário para baixar/apagar de lá. Só preenchido
    /// quando Armazenamento é "GoogleDrive".</summary>
    public string? IdArquivoDrive { get; set; }

    /// <summary>"Ativo" (pode ser devolvido) ou "Restaurado" (já foi devolvido ao local original).</summary>
    public string Status { get; set; } = "Ativo";
}
