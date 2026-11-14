namespace CubeBin.Core.Services;

/// <summary>
/// Abstração de notificações. A implementação concreta (toast nativo do Windows) fica no projeto
/// App, para manter o Core livre de dependências de tela.
/// </summary>
public interface INotificationService
{
    /// <summary>Notificação simples de título + mensagem (análise terminou, exclusão concluída...).</summary>
    void NotificarMensagemSimples(string titulo, string mensagem);
}
