using System.Windows.Forms;

namespace OrganizadorArquivos.App;

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
            Icon = System.Drawing.SystemIcons.Application, // Substituir por ícone próprio (app.ico) depois do design.
            Visible = true,
            Text = "CubeBin",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => abrirConfiguracoes();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
