using System.Windows;
using Application = System.Windows.Application;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace CubeBin.App;

/// <summary>
/// Substitui a MessageBox nativa (branca, fora do tema) por um diálogo no visual da NokyLabs,
/// com a MESMA forma de chamar. Os arquivos da interface apontam o apelido "MessageBox" para cá
/// (using MessageBox = CubeBin.App.DialogoCubeBin;), então as chamadas existentes não mudam.
///
/// Parâmetros extras e opcionais permitem trocar "Sim"/"Não" por verbos ("Excluir definitivamente"
/// / "Cancelar") e marcar a confirmação como perigosa (botão Perigo com ícone de lixeira).
///
/// Se o diálogo temático não puder ser criado por qualquer motivo, cai para a MessageBox nativa:
/// uma mensagem nunca deixa de aparecer por causa do visual.
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
        var app = Application.Current;
        if (app is null)
            return System.Windows.MessageBox.Show(mensagem, titulo, botoes, icone, padrao);

        // Chamadas vindas de outras threads (ex.: clique numa notificação) vão para a thread da tela.
        if (!app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() =>
                Show(mensagem, titulo, botoes, icone, padrao, textoConfirmar, textoCancelar, perigo));
        }

        try
        {
            var janela = new JanelaDialogo(mensagem, titulo, botoes, icone, padrao, textoConfirmar, textoCancelar, perigo);

            var dona = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible && w != janela)
                       ?? (app.MainWindow is { IsVisible: true } principal ? principal : null);
            if (dona is not null)
                janela.Owner = dona;
            else
                janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            janela.ShowDialog();
            return janela.Resultado;
        }
        catch (Exception)
        {
            return System.Windows.MessageBox.Show(mensagem, titulo, botoes, icone, padrao);
        }
    }
}
