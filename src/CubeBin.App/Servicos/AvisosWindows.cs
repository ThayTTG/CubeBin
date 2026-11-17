using Avalonia.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace CubeBin.App.Servicos;

/// <summary>
/// No Windows: toasts nativos. O de início tem o botão "Cancelar" e some depois do tempo; o de fim
/// abre o CubeBin no resultado quando clicado. Se o Windows não mostrar o toast (notificações
/// desligadas), a janelinha de contagem aparece no lugar, para o "Cancelar" nunca faltar.
/// </summary>
public sealed class AvisosWindows : IAvisos
{
    private const string TagInicio = "cubebin-inicio";
    private volatile bool _cancelado;
    private Action? _aoClicarFim;

    public AvisosWindows()
    {
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            if (e.Argument.Contains("acao=cancelar")) _cancelado = true;
            else if (e.Argument.Contains("acao=ver") && _aoClicarFim is { } acao) Dispatcher.UIThread.Post(acao);
        };
    }

    public async Task<bool> AvisarInicioAsync(int segundos, int quantidade)
    {
        _cancelado = false;
        try
        {
            new ToastContentBuilder()
                .AddText("O CubeBin vai organizar seus arquivos")
                .AddText($"{quantidade} arquivo(s) em {segundos} s. Está usando o computador? Cancele.")
                .AddButton(new ToastButton().SetContent("Cancelar").AddArgument("acao", "cancelar").SetBackgroundActivation())
                .Show(t => { t.Tag = TagInicio; t.ExpirationTime = DateTimeOffset.Now.AddSeconds(segundos); });
        }
        catch (Exception)
        {
            return await Dispatcher.UIThread.InvokeAsync(() => JanelaContagem.MostrarAsync(segundos, quantidade));
        }

        for (var i = 0; i < segundos && !_cancelado; i++) await Task.Delay(1000);
        try { ToastNotificationManagerCompat.History.Remove(TagInicio); } catch { /* já sumiu */ }
        return _cancelado;
    }

    public void AvisarFim(string titulo, string mensagem, Action? aoClicar)
    {
        _aoClicarFim = aoClicar;
        try
        {
            new ToastContentBuilder().AddArgument("acao", "ver").AddText(titulo).AddText(mensagem).Show();
        }
        catch { /* sem notificações: o resultado fica na tela de Cubos */ }
    }

    public void Avisar(string titulo, string mensagem)
    {
        try { new ToastContentBuilder().AddText(titulo).AddText(mensagem).Show(); } catch { }
    }
}
