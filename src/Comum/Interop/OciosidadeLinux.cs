using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Comum.Interop;

/// <summary>
/// Há quanto tempo o usuário não mexe no mouse/teclado, no Linux. Não existe uma API única como o
/// GetLastInputInfo do Windows; cada ambiente expõe a sua:
/// 1) GNOME (X11 e Wayland): D-Bus org.gnome.Mutter.IdleMonitor.GetIdletime (milissegundos).
/// 2) KDE Plasma: D-Bus org.freedesktop.ScreenSaver.GetSessionIdleTime (milissegundos no KDE).
/// 3) Qualquer sessão X11: extensão XScreenSaver (libXss).
/// Se nenhum responder (ex.: Sway, Hyprland), devolve null e quem chamou decide o que fazer.
/// </summary>
public static partial class OciosidadeLinux
{
    private static readonly TimeSpan LimiteComando = TimeSpan.FromSeconds(3);

    public static TimeSpan? Obter()
    {
        if (!OperatingSystem.IsLinux()) return null;

        var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToUpperInvariant();

        if (!desktop.Contains("KDE"))
        {
            var gnome = ChamarGdbus("org.gnome.Mutter.IdleMonitor", "/org/gnome/Mutter/IdleMonitor/Core",
                "org.gnome.Mutter.IdleMonitor.GetIdletime");
            if (gnome is not null) return gnome;
        }

        // O GetSessionIdleTime devolve milissegundos no KDE; em outros ambientes a unidade varia,
        // então só é usado no KDE.
        if (desktop.Contains("KDE"))
        {
            var kde = ChamarGdbus("org.freedesktop.ScreenSaver", "/ScreenSaver",
                "org.freedesktop.ScreenSaver.GetSessionIdleTime");
            if (kde is not null) return kde;
        }

        return ObterPeloX11();
    }

    private static TimeSpan? ChamarGdbus(string destino, string objeto, string metodo)
    {
        var resultado = ComandoExterno.Executar("gdbus",
            new[] { "call", "--session", "--dest", destino, "--object-path", objeto, "--method", metodo },
            limite: LimiteComando);

        if (resultado is null || resultado.Codigo != 0) return null;
        return LerRespostaGdbus(resultado.Saida);
    }

    /// <summary>Lê a resposta do gdbus, no formato "(uint64 12345,)" ou "(uint32 12345,)".</summary>
    public static TimeSpan? LerRespostaGdbus(string saida)
    {
        var achado = NumeroGdbus().Match(saida);
        if (!achado.Success || !ulong.TryParse(achado.Groups[1].Value, out var milissegundos))
            return null;
        return TimeSpan.FromMilliseconds(milissegundos);
    }

    [GeneratedRegex(@"\(\s*u?int(?:32|64)\s+(\d+)\s*,?\s*\)")]
    private static partial Regex NumeroGdbus();

    // ---------- X11 (XScreenSaver) ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public IntPtr Window;
        public int State;
        public int Kind;
        public ulong TilOrSince;
        public ulong Idle;
        public ulong EventMask;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr nome);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr dados);

    [DllImport("libXss.so.1")]
    private static extern IntPtr XScreenSaverAllocInfo();

    [DllImport("libXss.so.1")]
    private static extern int XScreenSaverQueryInfo(IntPtr display, IntPtr janela, IntPtr info);

    private static TimeSpan? ObterPeloX11()
    {
        // No Wayland o XScreenSaver (via XWayland) só enxerga os apps X11: o número não vale.
        var sessao = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(sessao, "wayland", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return null;

        var display = IntPtr.Zero;
        var info = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;

            info = XScreenSaverAllocInfo();
            if (info == IntPtr.Zero) return null;

            if (XScreenSaverQueryInfo(display, XDefaultRootWindow(display), info) == 0) return null;

            var dados = Marshal.PtrToStructure<XScreenSaverInfo>(info);
            return TimeSpan.FromMilliseconds(dados.Idle);
        }
        catch (Exception)
        {
            // libX11/libXss ausentes ou incompatíveis.
            return null;
        }
        finally
        {
            if (info != IntPtr.Zero) XFree(info);
            if (display != IntPtr.Zero) XCloseDisplay(display);
        }
    }
}
