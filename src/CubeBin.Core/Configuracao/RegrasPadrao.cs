using CubeBin.Core.Plataforma;

namespace CubeBin.Core.Configuracao;

/// <summary>Jeito único de escrever uma extensão: minúscula e com ponto (".jpg").</summary>
public static class Extensoes
{
    public static string Normalizar(string extensao)
    {
        var e = (extensao ?? "").Trim().ToLowerInvariant();
        if (e.Length == 0) return "";
        return e.StartsWith('.') || e == "~" ? e : "." + e;
    }

    /// <summary>A extensão de um nome de arquivo ("Foto.JPG" → ".jpg"; sem extensão → "").</summary>
    public static string DoArquivo(string nomeOuCaminho) => Normalizar(Path.GetExtension(nomeOuCaminho));
}

/// <summary>
/// As regras que já vêm prontas (PRD 5.1): imagens para Imagens, vídeos para Vídeos, áudios para
/// Músicas e documentos para Documentos. Também serve para dizer a "categoria" de um arquivo nos
/// Cubos (o preview da quantidade por tipo).
/// </summary>
public static class RegrasPadrao
{
    public static readonly string[] Imagens =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".heic", ".heif", ".tif", ".tiff",
        ".ico", ".avif", ".raw", ".cr2", ".nef", ".dng", ".psd", ".xcf",
    };

    public static readonly string[] Videos =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts",
    };

    public static readonly string[] Audios =
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus", ".mid", ".midi", ".aiff",
    };

    public static readonly string[] Documentos =
    {
        ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".xls", ".xlsx", ".ods", ".csv",
        ".ppt", ".pptx", ".odp", ".epub",
    };

    /// <summary>A categoria de um arquivo para o resumo dos Cubos.</summary>
    public static string Categoria(string extensao)
    {
        var e = Extensoes.Normalizar(extensao);
        if (Imagens.Contains(e)) return "Imagens";
        if (Videos.Contains(e)) return "Vídeos";
        if (Audios.Contains(e)) return "Músicas";
        if (Documentos.Contains(e)) return "Documentos";
        return "Outros";
    }

    /// <summary>A configuração de quem abre o CubeBin pela primeira vez.</summary>
    public static Configuracoes CriarConfiguracaoInicial()
    {
        var config = new Configuracoes();
        foreach (var padrao in PastasDoSistema.Todas)
            config.Pastas.Add(CriarPastaPadrao(padrao));

        config.Regras = RegrasIniciais();
        return config;
    }

    public static List<Regra> RegrasIniciais() => new()
    {
        new Regra { PastaId = IdDaPasta(PastaPadrao.Imagens), Extensoes = Imagens.ToList() },
        new Regra { PastaId = IdDaPasta(PastaPadrao.Videos), Extensoes = Videos.ToList() },
        new Regra { PastaId = IdDaPasta(PastaPadrao.Musicas), Extensoes = Audios.ToList() },
        new Regra { PastaId = IdDaPasta(PastaPadrao.Documentos), Extensoes = Documentos.ToList() },
    };

    public static string IdDaPasta(PastaPadrao padrao) => padrao switch
    {
        PastaPadrao.Documentos => "documentos",
        PastaPadrao.AreaDeTrabalho => "area-de-trabalho",
        PastaPadrao.Downloads => "downloads",
        PastaPadrao.Imagens => "imagens",
        PastaPadrao.Videos => "videos",
        PastaPadrao.Musicas => "musicas",
        _ => padrao.ToString().ToLowerInvariant(),
    };

    /// <summary>Por padrão, só Documentos, Área de Trabalho e Downloads são organizadas: é onde os
    /// arquivos se acumulam. Imagens, Vídeos e Músicas são destinos.</summary>
    public static PastaConfig CriarPastaPadrao(PastaPadrao padrao) => new()
    {
        Id = IdDaPasta(padrao),
        Nome = PastasDoSistema.NomeAmigavel(padrao),
        Padrao = padrao,
        Monitorar = padrao is PastaPadrao.Documentos or PastaPadrao.AreaDeTrabalho or PastaPadrao.Downloads,
    };
}
