namespace CubeBin.Core.Models;

/// <summary>Uma pasta onde o agente procura duplicados, temporários antigos e arquivos grandes parados.</summary>
public class PastaAnalisada
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Nome { get; set; } = string.Empty;
    public string Caminho { get; set; } = string.Empty;
}
