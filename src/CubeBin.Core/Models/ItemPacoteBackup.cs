namespace CubeBin.Core.Models;

/// <summary>Um arquivo individual dentro de um pacote de backup enviado à nuvem.</summary>
public class ItemPacoteBackup
{
    public string NomeArquivo { get; set; } = string.Empty;

    /// <summary>Onde o arquivo estava antes de ser incluído no pacote (e apagado localmente).</summary>
    public string CaminhoOriginal { get; set; } = string.Empty;

    public long TamanhoBytes { get; set; }

    /// <summary>Categoria por extensão (Fotos, Músicas, Vídeos, Documentos, Compactados, Outros) — usada na Camada 4.</summary>
    public string Categoria { get; set; } = "Outros";
}
