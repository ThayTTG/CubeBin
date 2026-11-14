using System.Runtime.InteropServices;
using Avalonia;
using CubeBin.Core.Interop;
using NokyLabs.UI;

namespace CubeBin.App;

/// <summary>Ponto de partida do programa.</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Exceções fora da thread da tela (threads de segundo plano) também vão para o crash.log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                App.RegistrarCrash(ex);
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            App.RegistrarCrash(ex);
            AvisoDoSistema($"Não foi possível iniciar o CubeBin.\n\nDetalhes em:\n{App.CaminhoLogCrash}",
                "Erro na inicialização");
        }
    }

    // Usado também pelo previewer do Avalonia.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseNokyLabs()
            .LogToTrace();

    /// <summary>
    /// Aviso de último caso, com a caixa de mensagem do próprio Windows (não depende do visual
    /// do app, então aparece mesmo se o erro tiver vindo dele).
    /// </summary>
    public static void AvisoDoSistema(string mensagem, string titulo)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBoxW(IntPtr.Zero, mensagem, titulo, 0x10 /* MB_ICONERROR */);
                return;
            }

            Console.Error.WriteLine($"{titulo}: {mensagem}");
            if (OperatingSystem.IsLinux())
                AvisoNoLinux(mensagem, titulo);
        }
        catch
        {
            // Se nem a janela de aviso puder ser mostrada, o crash.log já tem o registro.
        }
    }

    /// <summary>
    /// No Linux, a caixa de erro do próprio ambiente: zenity (GNOME e a maioria) ou kdialog (KDE);
    /// sem nenhum dos dois, ao menos uma notificação.
    /// </summary>
    private static void AvisoNoLinux(string mensagem, string titulo)
    {
        var limite = TimeSpan.FromMinutes(30);
        if (ComandoExterno.Executar("zenity", new[] { "--error", $"--title={titulo}", $"--text={mensagem}", "--no-markup" },
                limite: limite) is not null)
            return;
        if (ComandoExterno.Executar("kdialog", new[] { "--title", titulo, "--error", mensagem }, limite: limite) is not null)
            return;
        ComandoExterno.Abrir("notify-send", "--app-name=CubeBin", "--urgency=critical", titulo, mensagem);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
