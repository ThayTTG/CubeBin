using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Comum.UI;

/// <summary>
/// Ícone de bandeja do sistema — mantém o app residente em segundo plano (seção 7 da spec).
/// Clique no ícone abre o app; o menu tem "Abrir", a ação principal do app e "Sair".
/// O menu é o nativo do sistema (no Windows ele segue o tema claro/escuro do próprio Windows).
/// Cada app da família diz o seu nome, o seu ícone e qual é a sua ação principal.
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly TrayIcon _trayIcon;

    /// <param name="nomeDoApp">Aparece na dica do ícone e em "Abrir o …".</param>
    /// <param name="icone">O ícone do app (veja <see cref="CarregarIcone"/>).</param>
    /// <param name="textoAcaoPrincipal">Ex.: "Organizar agora" ou "Procurar o que limpar".</param>
    public TrayIconManager(Application app, string nomeDoApp, WindowIcon? icone,
        Action abrir, string textoAcaoPrincipal, Action acaoPrincipal, Action sair)
    {
        var itemAbrir = new NativeMenuItem($"Abrir o {nomeDoApp}");
        itemAbrir.Click += (_, _) => abrir();

        var itemAcao = new NativeMenuItem(textoAcaoPrincipal);
        itemAcao.Click += (_, _) => acaoPrincipal();

        var itemSair = new NativeMenuItem("Sair");
        itemSair.Click += (_, _) => sair();

        var menu = new NativeMenu();
        menu.Items.Add(itemAbrir);
        menu.Items.Add(itemAcao);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(itemSair);

        _trayIcon = new TrayIcon
        {
            Icon = icone,
            ToolTipText = nomeDoApp,
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => abrir();

        TrayIcon.SetIcons(app, new TrayIcons { _trayIcon });
    }

    /// <summary>Carrega o ícone embutido no programa (o app.ico como recurso do Avalonia).
    /// Ex.: CarregarIcone("avares://CubeBin/app.ico"). Devolve null se não achar.</summary>
    public static WindowIcon? CarregarIcone(string endereco)
    {
        try
        {
            using var arquivo = AssetLoader.Open(new Uri(endereco));
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
