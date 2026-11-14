using System.Text;
using Microsoft.Win32;

namespace Comum;

/// <summary>
/// Registra (ou remove) o app para abrir automaticamente ao entrar no sistema.
/// Windows: a chave clássica HKCU\...\Run — não exige instalador nem empacotamento MSIX.
/// Linux: um atalho em ~/.config/autostart/&lt;nome&gt;.desktop (padrão XDG Autostart, que GNOME,
/// KDE, XFCE e afins seguem).
/// Cada app da família usa o seu próprio nome, para um não desligar o outro.
/// </summary>
public class InicializacaoService
{
    private const string CaminhoChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _nomeValor;

    /// <param name="nomeDoApp">O nome que aparece na aba Inicializar do Gerenciador de Tarefas
    /// (cada app da família tem o seu, para um não desligar o outro). No Linux, vira o nome do
    /// atalho de autostart.</param>
    public InicializacaoService(string nomeDoApp) => _nomeValor = nomeDoApp;

    /// <summary>Pasta do autostart no Linux. Pode ser trocada nos testes.</summary>
    public string PastaAutostart { get; init; } = ObterPastaAutostartPadrao();

    private string CaminhoAutostart => Path.Combine(PastaAutostart, NomeDoArquivoAutostart(_nomeValor));

    public bool EstaHabilitado()
    {
        if (OperatingSystem.IsWindows())
        {
            using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: false);
            return chave?.GetValue(_nomeValor) is string valor && !string.IsNullOrWhiteSpace(valor);
        }

        return File.Exists(CaminhoAutostart);
    }

    public void Habilitar(string caminhoExecutavel)
    {
        if (OperatingSystem.IsWindows())
        {
            using var chave = Registry.CurrentUser.CreateSubKey(CaminhoChaveRun, writable: true);
            chave.SetValue(_nomeValor, $"\"{caminhoExecutavel}\"");
            return;
        }

        Directory.CreateDirectory(PastaAutostart);
        File.WriteAllText(CaminhoAutostart, MontarAtalhoAutostart(_nomeValor, caminhoExecutavel),
            new UTF8Encoding(false));
    }

    public void Desabilitar()
    {
        if (OperatingSystem.IsWindows())
        {
            using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: true);
            chave?.DeleteValue(_nomeValor, throwOnMissingValue: false);
            return;
        }

        if (File.Exists(CaminhoAutostart))
            File.Delete(CaminhoAutostart);
    }

    /// <summary>"CubeBin" → "cubebin.desktop" (só letras, números e hífen).</summary>
    public static string NomeDoArquivoAutostart(string nomeDoApp)
    {
        var limpo = new StringBuilder();
        foreach (var c in nomeDoApp.ToLowerInvariant())
            limpo.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        return limpo.ToString().Trim('-') + ".desktop";
    }

    /// <summary>O conteúdo do .desktop de autostart (público para os testes).</summary>
    public static string MontarAtalhoAutostart(string nomeDoApp, string caminhoExecutavel)
    {
        var pasta = Path.GetDirectoryName(caminhoExecutavel) ?? "";
        var icone = Path.Combine(pasta, "icon.png");

        var texto = new StringBuilder();
        texto.Append("[Desktop Entry]\n");
        texto.Append("Type=Application\n");
        texto.Append($"Name={nomeDoApp}\n");
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
