using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CubeBin.App;

/// <summary>
/// Aplica o tema escuro nativo à janela via DwmSetWindowAttribute — mantém os controles nativos
/// de minimizar/maximizar/fechar do Windows, sem precisar reescrever a janela com chrome
/// customizado. Cobre três aspectos separados que o Windows controla independentemente:
/// 1) DWMWA_USE_IMMERSIVE_DARK_MODE: escurece a barra de título em si.
/// 2) DWMWA_BORDER_COLOR: a fina "borda de destaque" do Windows 11 ao redor da janela inteira —
///    sem isso, ela continua branca mesmo com a barra de título já escura (bug relatado).
/// 3) DWMWA_CAPTION_COLOR / DWMWA_TEXT_COLOR: reforço direto de cor na barra de título, disponível
///    a partir do Windows 11 22H2 — redundante com o item 1 na maioria dos casos, mas garante a
///    cor certa mesmo se o modo escuro "automático" (baseado no tema do Windows) não pegar.
/// Todos os atributos além do primeiro são específicos de builds mais novos do Windows 11 — em
/// versões sem suporte, DwmSetWindowAttribute simplesmente retorna erro e nada é aplicado (não
/// lança exceção), então essas chamadas são deliberadamente "melhor esforço", sem fallback.
/// </summary>
public static class TemaJanelaEscura
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_ANTIGO = 19;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    // As cores vêm de Theme/Colors.xaml (barra de título = Barra lateral, borda = Borda,
    // texto = Texto), para tudo continuar centralizado num lugar só.

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int valor, int tamanho);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint valor, int tamanho);

    /// <summary>Empacota RGB no formato COLORREF (0x00BBGGRR) que o DWM espera.</summary>
    private static uint ParaColorRef(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    public static void Aplicar(Window janela)
    {
        // As chamadas só têm efeito depois que o HWND nativo existe de fato — se a janela ainda
        // não foi criada, aguarda SourceInitialized em vez de ignorar silenciosamente (item 1 do
        // pedido de correção). WindowInteropHelper.Handle força a criação do HWND se chamado cedo
        // demais, então checamos primeiro sem forçar, e só então decidimos esperar ou aplicar.
        if (PossuiHandleCriado(janela, out var hwnd))
        {
            AplicarNoHandle(hwnd);
        }
        else
        {
            janela.SourceInitialized += (_, _) =>
            {
                if (PossuiHandleCriado(janela, out var hwndCriado))
                    AplicarNoHandle(hwndCriado);
            };
        }
    }

    private static bool PossuiHandleCriado(Window janela, out IntPtr hwnd)
    {
        hwnd = new WindowInteropHelper(janela).Handle;
        return hwnd != IntPtr.Zero;
    }

    private static void AplicarNoHandle(IntPtr hwnd)
    {
        var ativado = 1;
        var resultado = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref ativado, sizeof(int));
        if (resultado != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_ANTIGO, ref ativado, sizeof(int));

        // Melhor esforço: só funcionam em builds mais novos do Windows 11. Falhas são ignoradas —
        // o resultado nesses casos é só continuar com a borda/legenda padrão do sistema.
        var corBorda = CorDoTema("BordaColor", 0x2C, 0x23, 0x47);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref corBorda, sizeof(uint));

        var corLegenda = CorDoTema("BarraLateralColor", 0x14, 0x0F, 0x24);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref corLegenda, sizeof(uint));

        var corTexto = CorDoTema("TextoColor", 0xF2, 0xEE, 0xFF);
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref corTexto, sizeof(uint));
    }

    /// <summary>Lê uma cor de Theme/Colors.xaml; se não achar (ex.: recurso renomeado), usa o
    /// valor do guia da NokyLabs passado como reserva.</summary>
    private static uint CorDoTema(string chave, byte r, byte g, byte b)
    {
        if (System.Windows.Application.Current?.TryFindResource(chave) is System.Windows.Media.Color cor)
            return ParaColorRef(cor.R, cor.G, cor.B);
        return ParaColorRef(r, g, b);
    }
}
