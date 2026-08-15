using OrganizadorArquivos.Core.Interop;

namespace OrganizadorArquivos.Core.Services;

/// <summary>
/// Detecta há quanto tempo o usuário está ausente (sem input de mouse/teclado),
/// via Win32 GetLastInputInfo — seção 7 da spec.
/// </summary>
public class IdleDetectionService
{
    public TimeSpan ObterTempoOcioso()
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
        return ObterTempoOcioso() >= TimeSpan.FromMinutes(tempoAusenciaMinutos);
    }
}
