using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace CubeBin.App;

/// <summary>
/// Ícone de bandeja do sistema — mantém o app residente em segundo plano (seção 7 da spec).
/// Clique no ícone abre o CubeBin; o menu tem "Abrir o CubeBin", "Organizar agora" e "Sair".
/// O menu é o nativo do sistema (no Windows ele segue o tema claro/escuro do próprio Windows).
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly TrayIcon _trayIcon;

    public TrayIconManager(Application app, Action abrirConfiguracoes, Action executarAgora, Action sair)
    {
        var abrir = new NativeMenuItem("Abrir o CubeBin");
        abrir.Click += (_, _) => abrirConfiguracoes();

        var organizar = new NativeMenuItem("Organizar agora");
        organizar.Click += (_, _) => executarAgora();

        var sairItem = new NativeMenuItem("Sair");
        sairItem.Click += (_, _) => sair();

        var menu = new NativeMenu();
        menu.Items.Add(abrir);
        menu.Items.Add(organizar);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(sairItem);

        _trayIcon = new TrayIcon
        {
            Icon = ObterIconeDoApp(),
            ToolTipText = "CubeBin",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => abrirConfiguracoes();

        TrayIcon.SetIcons(app, new TrayIcons { _trayIcon });
    }

    /// <summary>
    /// O ícone vai embutido no próprio programa (recurso do Avalonia): app.ico no Windows,
    /// icon.png no Linux (a bandeja de lá trabalha com imagens PNG).
    /// </summary>
    private static WindowIcon? ObterIconeDoApp()
    {
        try
        {
            var recurso = OperatingSystem.IsWindows() ? "avares://CubeBin/app.ico" : "avares://CubeBin/icon.png";
            using var arquivo = AssetLoader.Open(new Uri(recurso));
            return new WindowIcon(arquivo);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
