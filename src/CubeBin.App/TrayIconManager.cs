using System.Windows.Forms;

namespace CubeBin.App;

/// <summary>
/// Ícone de bandeja do sistema — mantém o app residente em segundo plano (seção 7 da spec).
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconManager(Action abrirConfiguracoes, Action executarAgora, Action sair)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir configurações", null, (_, _) => abrirConfiguracoes());
        menu.Items.Add("Executar agora", null, (_, _) => executarAgora());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => sair());

        _notifyIcon = new NotifyIcon
        {
            Icon = ObterIconeDoApp(),
            Visible = true,
            Text = "CubeBin",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => abrirConfiguracoes();
    }

    /// <summary>
    /// Extrai o ícone embutido no próprio .exe em execução (o mesmo definido via ApplicationIcon
    /// no .csproj) — evita precisar copiar o .ico como arquivo solto junto do executável. Cai no
    /// ícone padrão do Windows se a extração falhar por qualquer motivo (ex.: rodando via
    /// "dotnet run", onde o executável real é o dotnet.exe, sem o ícone do app embutido).
    /// </summary>
    private static System.Drawing.Icon ObterIconeDoApp()
    {
        try
        {
            var caminhoExecutavel = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(caminhoExecutavel))
            {
                var icone = System.Drawing.Icon.ExtractAssociatedIcon(caminhoExecutavel);
                if (icone is not null) return icone;
            }
        }
        catch (Exception)
        {
            // Extração falhou — segue para o ícone padrão do sistema abaixo.
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
