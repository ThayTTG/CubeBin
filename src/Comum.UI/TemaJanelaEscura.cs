using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace Comum.UI;

/// <summary>
/// Deixa a barra de título do Windows no tema escuro do app via DwmSetWindowAttribute — mantém
/// os botões nativos de minimizar/maximizar/fechar. Cobre três aspectos que o Windows controla
/// separadamente:
/// 1) DWMWA_USE_IMMERSIVE_DARK_MODE: escurece a barra de título em si.
/// 2) DWMWA_BORDER_COLOR: a fina borda do Windows 11 ao redor da janela (senão fica branca).
/// 3) DWMWA_CAPTION_COLOR / DWMWA_TEXT_COLOR: a cor exata da barra e do título (Windows 11 22H2+).
/// Tudo é "melhor esforço": em versões sem suporte o Windows só ignora, sem erro.
/// As cores vêm do Kit NokyLabs (barra = SidebarColor, borda = BorderColor, texto = TextColor).
/// </summary>
public static class TemaJanelaEscura
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_ANTIGO = 19;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int valor, int tamanho);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint valor, int tamanho);

    /// <summary>Empacota RGB no formato COLORREF (0x00BBGGRR) que o DWM espera.</summary>
    private static uint ParaColorRef(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    public static void Aplicar(Window janela)
    {
        if (!OperatingSystem.IsWindows()) return;

        // O identificador nativo da janela só existe depois que ela abre.
        janela.Opened += (_, _) =>
        {
            try
            {
                var hwnd = janela.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                    AplicarNoHandle(janela, hwnd);
            }
            catch
            {
                // Melhor esforço: sem isso a janela só fica com a barra padrão do sistema.
            }
        };
    }

    private static void AplicarNoHandle(Window janela, IntPtr hwnd)
    {
        var ativado = 1;
        var resultado = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref ativado, sizeof(int));
        if (resultado != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_ANTIGO, ref ativado, sizeof(int));

        var corBorda = CorDoTema(janela, "BorderColor", 0x2C, 0x23, 0x47);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref corBorda, sizeof(uint));

        var corLegenda = CorDoTema(janela, "SidebarColor", 0x14, 0x0F, 0x24);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref corLegenda, sizeof(uint));

        var corTexto = CorDoTema(janela, "TextColor", 0xF2, 0xEE, 0xFF);
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref corTexto, sizeof(uint));
    }

    /// <summary>Lê uma cor do Kit; se não achar, usa o valor do guia passado como reserva.</summary>
    private static uint CorDoTema(Window janela, string chave, byte r, byte g, byte b)
    {
        if (janela.TryFindResource(chave, janela.ActualThemeVariant, out var valor) && valor is Color cor)
            return ParaColorRef(cor.R, cor.G, cor.B);
        return ParaColorRef(r, g, b);
    }
}
