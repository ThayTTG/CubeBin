namespace CubeBin.Core.Services;

/// <summary>Classifica um arquivo em uma categoria legível (Fotos, Músicas, Vídeos, Documentos...)
/// a partir da extensão — usado nos detalhes de um pacote de backup (Camada 4).</summary>
public static class CategoriaArquivo
{
    private static readonly Dictionary<string, string> Mapa = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "Fotos", [".jpeg"] = "Fotos", [".png"] = "Fotos", [".gif"] = "Fotos",
        [".bmp"] = "Fotos", [".webp"] = "Fotos", [".heic"] = "Fotos", [".svg"] = "Fotos",

        [".mp3"] = "Músicas", [".wav"] = "Músicas", [".flac"] = "Músicas", [".aac"] = "Músicas", [".m4a"] = "Músicas",

        [".mp4"] = "Vídeos", [".mkv"] = "Vídeos", [".avi"] = "Vídeos", [".mov"] = "Vídeos", [".wmv"] = "Vídeos",

        [".pdf"] = "Documentos", [".docx"] = "Documentos", [".doc"] = "Documentos",
        [".xlsx"] = "Documentos", [".xls"] = "Documentos", [".pptx"] = "Documentos", [".txt"] = "Documentos",

        [".zip"] = "Compactados", [".rar"] = "Compactados", [".7z"] = "Compactados",
    };

    public static string Classificar(string extensao) =>
        Mapa.TryGetValue(extensao, out var categoria) ? categoria : "Outros";
}
