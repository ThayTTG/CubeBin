using Comum.Interop;

namespace Comum;

/// <summary>
/// Detecta há quanto tempo o usuário está ausente (sem input de mouse/teclado) — seção 7 da spec.
/// Windows: Win32 GetLastInputInfo. Linux: D-Bus do GNOME/KDE ou XScreenSaver (ver OciosidadeLinux).
/// </summary>
public class IdleDetectionService
{
    /// <summary>
    /// Tempo ocioso, ou null quando o sistema não informa (ex.: Linux com Sway/Hyprland, ou um
    /// sistema sem suporte).
    /// </summary>
    public TimeSpan? ObterTempoOcioso()
    {
        if (OperatingSystem.IsWindows())
            return ObterNoWindows();

        if (OperatingSystem.IsLinux())
            return OciosidadeLinux.Obter();

        return null;
    }

    private static TimeSpan ObterNoWindows()
    {
        var info = new NativeMethods.LASTINPUTINFO();
        info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);

        if (!NativeMethods.GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        var tickCountAtual = (uint)Environment.TickCount;
        var milissegundosOcioso = tickCountAtual - info.dwTime;

        return TimeSpan.FromMilliseconds(milissegundosOcioso);
    }

    public bool UsuarioEstaAusente(int tempoAusenciaMinutos)
    {
        var ocioso = ObterTempoOcioso();

        // Sem como medir (só acontece fora do Windows): segue o horário agendado sem esperar a
        // ausência. O aviso com "Cancelar" continua aparecendo antes de qualquer arquivo ser movido.
        if (ocioso is null)
            return true;

        return ocioso.Value >= TimeSpan.FromMinutes(tempoAusenciaMinutos);
    }
}
