using Comum.Interop;
using CubeBin.Core.Services;

namespace CubeBin.App.Services;

/// <summary>
/// O par do ToastNotificationService no Linux: avisos simples (análise terminou, exclusão
/// concluída, backup enviado) pela notificação do sistema (notify-send).
/// </summary>
public class LinuxNotificationService : INotificationService
{
    public void NotificarMensagemSimples(string titulo, string mensagem) =>
        NotificacaoLinux.Enviar("CubeBin", titulo, mensagem);
}
