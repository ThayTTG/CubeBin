using Avalonia.Threading;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Servicos;

/// <summary>
/// No Linux: o aviso de início é uma janelinha com contagem regressiva e "Cancelar" (JanelaContagem),
/// que aparece em qualquer ambiente; o fim e os avisos vão pelo notify-send. Quando o notify-send
/// aceita botões, o aviso de fim ganha "Ver resultado".
/// </summary>
public sealed class AvisosLinux : IAvisos
{
    public Task<bool> AvisarInicioAsync(int segundos, int quantidade) =>
        Dispatcher.UIThread.InvokeAsync(() => JanelaContagem.MostrarAsync(segundos, quantidade));

    public void AvisarFim(string titulo, string mensagem, Action? aoClicar)
    {
        _ = Task.Run(() =>
        {
            var clicou = aoClicar is null
                ? null
                : NotificacaoLinux.EnviarComBotao(InfoApp.Nome, titulo, mensagem, "Ver resultado");
            if (clicou == true) Dispatcher.UIThread.Post(aoClicar!);
            else if (clicou is null) NotificacaoLinux.Enviar(InfoApp.Nome, titulo, mensagem);
        });
    }

    public void Avisar(string titulo, string mensagem) => NotificacaoLinux.Enviar(InfoApp.Nome, titulo, mensagem);
}
