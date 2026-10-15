namespace CubeBin.Core.Models;

/// <summary>
/// Um envio de backup para a nuvem — os arquivos marcados são compactados num único .zip nomeado
/// com data/hora antes de subir. Cada pacote vira um "Cubo" na aba de visualização (Camada 4).
/// </summary>
public class PacoteBackup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string NomeArquivoZip { get; set; } = string.Empty;

    /// <summary>ID do .zip no Google Drive — necessário para baixar/restaurar depois.</summary>
    public string IdArquivoDrive { get; set; } = string.Empty;

    /// <summary>Link de visualização direta no Google Drive (pode ser nulo se o Drive não retornar um).</summary>
    public string? LinkDrive { get; set; }

    /// <summary>Caminho de pastas no Drive onde o pacote foi salvo (ex.: "CubeBin/Backups").</summary>
    public string CaminhoDrive { get; set; } = string.Empty;

    public DateTime EnviadoEm { get; set; } = DateTime.Now;

    /// <summary>Tamanho do .zip em si (não a soma dos arquivos descompactados).</summary>
    public long TamanhoBytes { get; set; }

    public List<ItemPacoteBackup> Itens { get; set; } = new();
}
