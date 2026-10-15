using System.Runtime.InteropServices;

namespace CubeBin.Core.Interop;

/// <summary>
/// Resolve os caminhos reais de Documentos, Área de Trabalho e Downloads via a API de Known
/// Folders do Windows (SHGetKnownFolderPath) — a mesma fonte de verdade que o Explorer usa.
/// Diferente de concatenar "Documents"/"Desktop" manualmente no perfil do usuário, isso resolve
/// corretamente o caso de o OneDrive ter redirecionado essas pastas para dentro de si mesmo
/// (recurso "Backup do PC" / Known Folder Move), que move a pasta física de lugar.
/// </summary>
public static class KnownFolders
{
    private static readonly Guid IdDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid IdDocumentos = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly Guid IdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

    private static string? ObterCaminho(Guid idPasta)
    {
        if (SHGetKnownFolderPath(ref idPasta, 0, IntPtr.Zero, out var ponteiro) != 0)
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

    /// <summary>Caminho real da pasta Área de Trabalho, já considerando redirecionamento do OneDrive.</summary>
    public static string ObterAreaTrabalho() =>
        ObterCaminho(IdDesktop) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Caminho real da pasta Documentos, já considerando redirecionamento do OneDrive.</summary>
    public static string ObterDocumentos() =>
        ObterCaminho(IdDocumentos) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Caminho real da pasta Downloads (sem enum equivalente no .NET clássico).</summary>
    public static string ObterDownloads() =>
        ObterCaminho(IdDownloads) ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
