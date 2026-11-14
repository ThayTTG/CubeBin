using Avalonia.Threading;
using Comum.Interop;
using Organizador.Core.Models;
using Organizador.Core.Services;

namespace Organizador.App.Services;

/// <summary>
/// Notificações no Linux (o par do ToastNotificationService do Windows):
/// - Aviso de início: uma janelinha com contagem regressiva e "Cancelar" (JanelaContagem). É mais
///   garantido que botões em notificação, que nem todo ambiente mostra.
/// - Resumo final e avisos simples: notificação do sistema via notify-send (libnotify). Quando o
///   notify-send aceita botões (libnotify 0.7.10+), o resumo ganha "Ver detalhes", que abre o mesmo
///   relatório do clique no toast do Windows.
/// </summary>
public class LinuxNotificationService : INotificationService
{
    public Task<bool> NotificarInicioComCancelamentoAsync(int janelaCancelamentoSegundos) =>
        Dispatcher.UIThread.InvokeAsync(() => JanelaContagem.MostrarAsync(janelaCancelamentoSegundos));

    public void NotificarResultadoFinal(ResultadoExecucao resultado)
    {
        if (resultado.Cancelado)
        {
            NotificacaoLinux.Enviar(InfoApp.Nome, InfoApp.Nome, "Execução cancelada pelo usuário.");
            return;
        }

        var texto = $"{resultado.ArquivosMovidos} movidos, {resultado.ArquivosComFalha} falharam, " +
                    $"{resultado.ArquivosPendentes} pendentes.";

        // Em segundo plano: com botão, o notify-send fica esperando até a notificação sumir.
        _ = Task.Run(() =>
        {
            var clicou = NotificacaoLinux.EnviarComBotao(InfoApp.Nome, InfoApp.Nome, texto, "Ver detalhes");
            if (clicou == true)
                AvisosComuns.MostrarRelatorioResultado(resultado);
            else if (clicou is null)
                // notify-send antigo (sem botões) ou ausente: aviso simples.
                NotificacaoLinux.Enviar(InfoApp.Nome, InfoApp.Nome, texto + $" Abra o {InfoApp.Nome} para ver as pendências.");
        });
    }

    public void NotificarMensagemSimples(string titulo, string mensagem) =>
        NotificacaoLinux.Enviar(InfoApp.Nome, titulo, mensagem);

    public bool ConfirmarRiscoSubpastas(string nomePasta) => AvisosComuns.ConfirmarRiscoSubpastas(nomePasta);
}
