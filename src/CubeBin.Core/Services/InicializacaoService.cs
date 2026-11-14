using System.Text;
using Microsoft.Win32;

namespace CubeBin.Core.Services;

/// <summary>
/// Registra (ou remove) o app para abrir automaticamente ao entrar no sistema.
/// Windows: a chave clássica HKCU\...\Run — não exige instalador nem empacotamento MSIX.
/// Linux: um atalho em ~/.config/autostart/cubebin.desktop (padrão XDG Autostart, que GNOME, KDE,
/// XFCE e afins seguem).
/// </summary>
public class InicializacaoService
{
    private const string CaminhoChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomeValor = "CubeBin";
    private const string NomeArquivoAutostart = "cubebin.desktop";

    /// <summary>Pasta do autostart no Linux. Pode ser trocada nos testes.</summary>
    public string PastaAutostart { get; init; } = ObterPastaAutostartPadrao();

    private string CaminhoAutostart => Path.Combine(PastaAutostart, NomeArquivoAutostart);

    public bool EstaHabilitado()
    {
        if (OperatingSystem.IsWindows())
        {
            using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: false);
            return chave?.GetValue(NomeValor) is string valor && !string.IsNullOrWhiteSpace(valor);
        }

        return File.Exists(CaminhoAutostart);
    }

    public void Habilitar(string caminhoExecutavel)
    {
        if (OperatingSystem.IsWindows())
        {
            using var chave = Registry.CurrentUser.CreateSubKey(CaminhoChaveRun, writable: true);
            chave.SetValue(NomeValor, $"\"{caminhoExecutavel}\"");
            return;
        }

        Directory.CreateDirectory(PastaAutostart);
        File.WriteAllText(CaminhoAutostart, MontarAtalhoAutostart(caminhoExecutavel), new UTF8Encoding(false));
    }

    public void Desabilitar()
    {
        if (OperatingSystem.IsWindows())
        {
            using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: true);
            chave?.DeleteValue(NomeValor, throwOnMissingValue: false);
            return;
        }

        if (File.Exists(CaminhoAutostart))
            File.Delete(CaminhoAutostart);
    }

    /// <summary>O conteúdo do .desktop de autostart (público para os testes).</summary>
    public static string MontarAtalhoAutostart(string caminhoExecutavel)
    {
        var pasta = Path.GetDirectoryName(caminhoExecutavel) ?? "";
        var icone = Path.Combine(pasta, "icon.png");

        var texto = new StringBuilder();
        texto.Append("[Desktop Entry]\n");
        texto.Append("Type=Application\n");
        texto.Append("Name=CubeBin\n");
        texto.Append("Comment=Organiza suas pastas por tipo de arquivo\n");
        texto.Append($"Exec={EscaparExec(caminhoExecutavel)}\n");
        texto.Append($"Path={pasta}\n");
        if (File.Exists(icone))
            texto.Append($"Icon={icone}\n");
        texto.Append("Terminal=false\n");
        texto.Append("X-GNOME-Autostart-enabled=true\n");
        return texto.ToString();
    }

    /// <summary>
    /// Aspas no caminho do Exec, escapando os caracteres reservados da especificação do .desktop
    /// (aspas, crase, cifrão e barra invertida), para caminhos com espaços ou acentos funcionarem.
    /// </summary>
    public static string EscaparExec(string caminho)
    {
        var escapado = new StringBuilder("\"");
        foreach (var c in caminho)
        {
            if (c is '"' or '`' or '$' or '\\')
                escapado.Append('\\');
            escapado.Append(c);
        }
        escapado.Append('"');
        return escapado.ToString();
    }

    private static string ObterPastaAutostartPadrao()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "autostart");
    }
}
