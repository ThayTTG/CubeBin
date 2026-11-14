using Microsoft.Toolkit.Uwp.Notifications;
using CubeBin.Core.Services;

namespace CubeBin.App.Services;

/// <summary>
/// Implementação de INotificationService com os toasts nativos do Windows: avisos simples de
/// título + mensagem (análise terminou, exclusão concluída, backup enviado). O aviso de início com
/// "Cancelar" ficou no organizador, que é quem age sozinho; o CubeBin só sugere.
/// </summary>
public class ToastNotificationService : INotificationService
{
    public void NotificarMensagemSimples(string titulo, string mensagem)
    {
        // Fora do Windows (só para testar a tela) não há toasts.
        if (!OperatingSystem.IsWindows()) return;

        new ToastContentBuilder()
            .AddText(titulo)
            .AddText(mensagem)
            .Show();
    }
}
