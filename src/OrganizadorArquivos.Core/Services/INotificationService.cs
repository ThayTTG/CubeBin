using OrganizadorArquivos.Core.Models;

namespace OrganizadorArquivos.Core.Services;

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

    /// <summary>Exibido ao ativar organizar_subpastas em uma pasta na UI (seção 6 da spec).</summary>
    bool ConfirmarRiscoSubpastas(string nomePasta);
}
