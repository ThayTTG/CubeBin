namespace CubeBin.Core.Models;

/// <summary>
/// Arquivo cuja extensão não corresponde a nenhuma RegraExtensao. Fica em fila até o
/// usuário decidir (criar regra, mover manualmente ou ignorar) — ver seção 5 da spec.
/// </summary>
public class ArquivoPendente
{
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoOriginal { get; set; } = string.Empty;
    public string Extensao { get; set; } = string.Empty;
    public string PastaOrigemId { get; set; } = string.Empty;
    public DateTime DataDeteccao { get; set; } = DateTime.Now;

    /// <summary>"Pendente", "Ignorado", "Resolvido".</summary>
    public string Status { get; set; } = "Pendente";
}
