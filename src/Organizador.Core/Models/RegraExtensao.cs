namespace Organizador.Core.Models;

/// <summary>
/// Regra que mapeia um conjunto de extensões a uma pasta de destino (ex: .zip, .rar -> "Compactados").
/// </summary>
public class RegraExtensao
{
    /// <summary>Chave da regra no dicionário de config (ex: "compactados", "executaveis").</summary>
    public string Chave { get; set; } = string.Empty;

    public List<string> Extensoes { get; set; } = new();

    public string Destino { get; set; } = string.Empty;

    /// <summary>
    /// Subcategoria opcional por extensão (chave = extensão em minúsculas, ex: ".rar" -> "WinRAR").
    /// Quando presente para a extensão do arquivo, ele vai para Destino\Subpasta em vez de direto
    /// em Destino. Extensões sem entrada aqui continuam indo direto para Destino (comportamento antigo).
    /// </summary>
    public Dictionary<string, string> Subpastas { get; set; } = new();
}
