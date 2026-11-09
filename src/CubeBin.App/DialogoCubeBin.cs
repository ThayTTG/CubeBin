using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace CubeBin.App;

// Os mesmos nomes e valores da MessageBox clássica do Windows, para as chamadas da versão 1
// continuarem exatamente iguais (MessageBox.Show(..., MessageBoxButton.YesNo, ...)).
public enum MessageBoxButton { OK = 0, OKCancel = 1, YesNoCancel = 3, YesNo = 4 }
public enum MessageBoxImage { None = 0, Error = 16, Question = 32, Warning = 48, Information = 64 }
public enum MessageBoxResult { None = 0, OK = 1, Cancel = 2, Yes = 6, No = 7 }

/// <summary>
/// O diálogo no visual da NokyLabs, com a MESMA forma de chamar da MessageBox: os arquivos da
/// interface apontam o apelido "MessageBox" para cá (using MessageBox = CubeBin.App.DialogoCubeBin;).
///
/// Como a MessageBox, ele espera a resposta antes de seguir (por baixo, um "laço de espera" do
/// Avalonia mantém a tela viva enquanto o diálogo está aberto).
///
/// Parâmetros extras e opcionais permitem trocar "Sim"/"Não" por verbos ("Excluir definitivamente"
/// / "Cancelar") e marcar a confirmação como perigosa (botão vermelho com ícone de lixeira).
///
/// Se o diálogo temático não puder ser criado por qualquer motivo, cai para a caixa de mensagem
/// nativa do Windows: uma mensagem nunca deixa de aparecer por causa do visual.
/// </summary>
public static class DialogoCubeBin
{
    public static MessageBoxResult Show(string mensagem) =>
        Show(mensagem, "CubeBin");

    public static MessageBoxResult Show(string mensagem, string titulo) =>
        Show(mensagem, titulo, MessageBoxButton.OK);

    public static MessageBoxResult Show(string mensagem, string titulo, MessageBoxButton botoes) =>
        Show(mensagem, titulo, botoes, MessageBoxImage.None);

    public static MessageBoxResult Show(string mensagem, string titulo, MessageBoxButton botoes,
        MessageBoxImage icone) =>
        Show(mensagem, titulo, botoes, icone, MessageBoxResult.None);

    public static MessageBoxResult Show(string mensagem, string titulo, MessageBoxButton botoes,
        MessageBoxImage icone, MessageBoxResult padrao, string? textoConfirmar = null,
        string? textoCancelar = null, bool perigo = false)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return Nativa(mensagem, titulo, botoes, icone);

        // Chamadas vindas de outras threads (ex.: clique numa notificação) vão para a thread da tela.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.Invoke(() =>
                Show(mensagem, titulo, botoes, icone, padrao, textoConfirmar, textoCancelar, perigo));
        }

        try
        {
            var janela = new JanelaDialogo(mensagem, titulo, botoes, icone, padrao, textoConfirmar, textoCancelar, perigo);

            var dona = desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible && w != janela)
                       ?? (desktop.MainWindow is { IsVisible: true } principal ? principal : null);

            var espera = new DispatcherFrame();
            janela.Closed += (_, _) => espera.Continue = false;

            if (dona is not null)
            {
                janela.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _ = janela.ShowDialog(dona);
            }
            else
            {
                janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                janela.ShowInTaskbar = true;
                janela.Topmost = true;
                janela.Show();
            }

            Dispatcher.UIThread.PushFrame(espera);
            return janela.Resultado;
        }
        catch (Exception)
        {
            return Nativa(mensagem, titulo, botoes, icone);
        }
    }

    private static MessageBoxResult Nativa(string mensagem, string titulo, MessageBoxButton botoes, MessageBoxImage icone)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return (MessageBoxResult)MessageBoxW(IntPtr.Zero, mensagem, titulo, (uint)botoes | (uint)icone);
        }
        catch
        {
            // Sem como mostrar nada: segue com a resposta "segura" abaixo.
        }

        return botoes == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.No;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
