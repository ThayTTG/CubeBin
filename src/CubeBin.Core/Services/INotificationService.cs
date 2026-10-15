using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// Abstração de notificações (seção 6 da spec). A implementação concreta (toast nativo do
/// Windows) fica no projeto App, para manter o Core livre de dependências de UI.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Mostra o aviso de início com janela de cancelamento. Retorna true se o usuário
    /// cancelou dentro do prazo, false se o tempo expirou e a execução deve prosseguir.
    /// </summary>
    Task<bool> NotificarInicioComCancelamentoAsync(int janelaCancelamentoSegundos);

    void NotificarResultadoFinal(ResultadoExecucao resultado);

    /// <summary>Notificação simples de título+mensagem, para operações fora do pipeline principal
    /// de organização (ex.: módulo Deletar).</summary>
    void NotificarMensagemSimples(string titulo, string mensagem);

    /// <summary>Exibido ao ativar organizar_subpastas em uma pasta na UI (seção 6 da spec).</summary>
    bool ConfirmarRiscoSubpastas(string nomePasta);
}
