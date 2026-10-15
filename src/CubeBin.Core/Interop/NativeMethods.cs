using System.Runtime.InteropServices;

namespace CubeBin.Core.Interop;

/// <summary>P/Invoke para GetLastInputInfo — usado pelo IdleDetectionService (seção 7 da spec).</summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
