using System.Runtime.InteropServices;

namespace CubeBin.Core.Plataforma;

/// <summary>As pastas padrão do usuário que o CubeBin conhece (PRD 5.1).</summary>
public enum PastaPadrao
{
    Documentos,
    AreaDeTrabalho,
    Downloads,
    Imagens,
    Videos,
    Musicas,
}

/// <summary>
/// Pergunta ao sistema onde ficam as pastas do usuário, do jeito que o Explorer e o gerenciador de
/// arquivos do Linux fazem:
/// - Windows: API de Known Folders (SHGetKnownFolderPath), que já considera o OneDrive ter
///   redirecionado Documentos ou a Área de Trabalho.
/// - Linux: o padrão XDG (~/.config/user-dirs.dirs), que tem o nome real em cada idioma
///   (ex.: ~/Transferências, ~/Imagens). Se a pasta não estiver no arquivo, usa o nome padrão do
///   xdg-user-dirs (~/Pictures, ~/Videos...).
/// Nada aqui apaga pasta nenhuma: no máximo cria a pasta que faltar (<see cref="Garantir"/>).
/// </summary>
public static class PastasDoSistema
{
    /// <summary>Onde procurar o user-dirs.dirs e a "home". Trocável nos testes.</summary>
    public static Func<string> ObterHome { get; set; } =
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Substitui a descoberta real (usado só nos testes, para nunca tocar nas pastas reais).</summary>
    public static Func<PastaPadrao, string>? Substituto { get; set; }

    public static IReadOnlyList<PastaPadrao> Todas { get; } = Enum.GetValues<PastaPadrao>();

    /// <summary>O nome que aparece para a pessoa.</summary>
    public static string NomeAmigavel(PastaPadrao pasta) => pasta switch
    {
        PastaPadrao.Documentos => "Documentos",
        PastaPadrao.AreaDeTrabalho => "Área de Trabalho",
        PastaPadrao.Downloads => "Downloads",
        PastaPadrao.Imagens => "Imagens",
        PastaPadrao.Videos => "Vídeos",
        PastaPadrao.Musicas => "Músicas",
        _ => pasta.ToString(),
    };

    /// <summary>Prefixo usado nos conflitos de nome (PRD 5.1: "Doc-Texto", "Desk-Texto").</summary>
    public static string PrefixoPadrao(PastaPadrao pasta) => pasta switch
    {
        PastaPadrao.Documentos => "Doc",
        PastaPadrao.AreaDeTrabalho => "Desk",
        PastaPadrao.Downloads => "Down",
        PastaPadrao.Imagens => "Img",
        PastaPadrao.Videos => "Vid",
        PastaPadrao.Musicas => "Mus",
        _ => "Pasta",
    };

    /// <summary>Caminho da pasta, mesmo que ela ainda não exista no disco.</summary>
    public static string Obter(PastaPadrao pasta)
    {
        if (Substituto is not null) return Substituto(pasta);

        if (OperatingSystem.IsWindows())
        {
            var noWindows = ObterNoWindows(pasta);
            if (!string.IsNullOrWhiteSpace(noWindows)) return noWindows;
        }

        var home = ObterHome();
        if (OperatingSystem.IsLinux())
        {
            var xdg = PastasXdg.Obter(ChaveXdg(pasta));
            if (!string.IsNullOrWhiteSpace(xdg)) return xdg;
        }

        return Path.Combine(home, NomePadraoEmIngles(pasta));
    }

    /// <summary>
    /// Devolve o caminho e cria a pasta se ela não existir (PRD 6.3: "se faltar, criar a pasta").
    /// <paramref name="criada"/> diz se foi preciso criar, para quem chamou registrar.
    /// </summary>
    public static string Garantir(PastaPadrao pasta, out bool criada)
    {
        var caminho = Obter(pasta);
        criada = false;
        if (!Directory.Exists(caminho))
        {
            Directory.CreateDirectory(caminho);
            criada = true;
        }
        return caminho;
    }

    /// <summary>A chave do user-dirs.dirs (XDG_&lt;CHAVE&gt;_DIR).</summary>
    public static string ChaveXdg(PastaPadrao pasta) => pasta switch
    {
        PastaPadrao.Documentos => "DOCUMENTS",
        PastaPadrao.AreaDeTrabalho => "DESKTOP",
        PastaPadrao.Downloads => "DOWNLOAD",
        PastaPadrao.Imagens => "PICTURES",
        PastaPadrao.Videos => "VIDEOS",
        PastaPadrao.Musicas => "MUSIC",
        _ => throw new ArgumentOutOfRangeException(nameof(pasta)),
    };

    /// <summary>O nome que o xdg-user-dirs usa quando o idioma não define outro.</summary>
    public static string NomePadraoEmIngles(PastaPadrao pasta) => pasta switch
    {
        PastaPadrao.Documentos => "Documents",
        PastaPadrao.AreaDeTrabalho => "Desktop",
        PastaPadrao.Downloads => "Downloads",
        PastaPadrao.Imagens => "Pictures",
        PastaPadrao.Videos => "Videos",
        PastaPadrao.Musicas => "Music",
        _ => throw new ArgumentOutOfRangeException(nameof(pasta)),
    };

    // ---------- Windows (Known Folders) ----------

    private static Guid IdKnownFolder(PastaPadrao pasta) => pasta switch
    {
        PastaPadrao.Documentos => new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        PastaPadrao.AreaDeTrabalho => new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        PastaPadrao.Downloads => new Guid("374DE290-123F-4565-9164-39C4925E467B"),
        PastaPadrao.Imagens => new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        PastaPadrao.Videos => new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        PastaPadrao.Musicas => new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"),
        _ => throw new ArgumentOutOfRangeException(nameof(pasta)),
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    private static string? ObterNoWindows(PastaPadrao pasta)
    {
        try
        {
            var id = IdKnownFolder(pasta);
            // KF_FLAG_DONT_VERIFY (0x4000): devolve o caminho mesmo que a pasta não exista ainda.
            if (SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out var ponteiro) != 0)
                return null;
            try
            {
                return Marshal.PtrToStringUni(ponteiro);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ponteiro);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
