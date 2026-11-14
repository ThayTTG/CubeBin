using System.IO;
using Avalonia.Threading;
using Comum.UI;
using Organizador.Core.Models;

namespace Organizador.App.Services;

/// <summary>
/// Diálogos de aviso que valem para qualquer sistema: o relatório do que deixou de ser organizado
/// (ao clicar na notificação final) e a confirmação de risco das subpastas. Usados tanto pelo
/// ToastNotificationService (Windows) quanto pelo LinuxNotificationService.
/// </summary>
public static class AvisosComuns
{
    /// <summary>
    /// Relatório do que deixou de ser organizado e por quê (item 1 do feedback de uso), mostrado
    /// ao clicar na notificação final. O clique chega numa thread própria do sistema de
    /// notificações, então despacha para a thread de UI antes de abrir o diálogo.
    /// </summary>
    public static void MostrarRelatorioResultado(ResultadoExecucao resultado)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (resultado.Falhas.Count == 0)
            {
                var extras = resultado.ArquivosPendentes > 0
                    ? $"\n\n{resultado.ArquivosPendentes} arquivo(s) ficaram pendentes (extensão sem regra cadastrada) — veja a aba \"Pendências\"."
                    : "";

                Dialogo.Show(
                    $"{resultado.ArquivosMovidos} arquivo(s) organizado(s) com sucesso.{extras}",
                    "Resultado da organização",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var detalhes = string.Join("\n", resultado.Falhas.Select(f => $"• {Path.GetFileName(f.Arquivo)}: {f.Motivo}"));
            var avisoRecall = resultado.AvisosRecall.Count > 0
                ? $"\n\n(Além disso, {resultado.AvisosRecall.Count} arquivo(s) organizado(s) com sucesso não puderam ser registrados no Recall — a organização em si não foi afetada.)"
                : "";

            Dialogo.Show(
                $"{resultado.ArquivosMovidos} arquivo(s) organizado(s) com sucesso.\n" +
                $"{resultado.ArquivosComFalha} arquivo(s) NÃO puderam ser organizados:\n\n{detalhes}{avisoRecall}",
                "Alguns arquivos não puderam ser organizados",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    public static bool ConfirmarRiscoSubpastas(string nomePasta)
    {
        var mensagem =
            $"Ativar a organização de subpastas em \"{nomePasta}\" pode mover arquivos que estão " +
            "dentro de estruturas de pastas já organizadas manualmente, alterando essa organização. Continuar?";

        var resultado = Dialogo.Show(
            mensagem,
            "Aviso de risco",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No,
            textoConfirmar: "Ativar mesmo assim",
            textoCancelar: "Cancelar");

        return resultado == MessageBoxResult.Yes;
    }
}
