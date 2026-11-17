using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IOPath = System.IO.Path;

namespace CubeBin.App;

/// <summary>O app: sobe os serviços, o agendador e o ícone da bandeja (ligados nas próximas etapas).</summary>
public partial class App : Application
{
    /// <summary>%LOCALAPPDATA%\CubeBin (Windows) ou ~/.local/share/CubeBin (Linux).</summary>
    public static readonly string PastaDados = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CubeBin");

    public static readonly string CaminhoLogCrash = IOPath.Combine(PastaDados, "crash.log");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    public static void RegistrarCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(PastaDados);
            File.AppendAllText(CaminhoLogCrash, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // Se nem o log conseguir ser gravado, não há mais nada a fazer aqui.
        }
    }
}
