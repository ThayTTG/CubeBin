using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using CubeBin.Core.Models;
using CubeBin.Core.Services;

namespace CubeBin.App.Services;

/// <summary>
/// Implementação concreta de INotificationService usando toasts nativos do Windows
/// (seção 6 da spec): aviso de início com contagem de 60s e cancelamento, e resumo final.
/// Clicar na notificação final (corpo ou botão) mostra um relatório do que aconteceu.
/// </summary>
public class ToastNotificationService : INotificationService
{
    private const string TagCancelamento = "organizador-inicio";
    private volatile bool _canceladoPeloUsuario;
    private ResultadoExecucao? _ultimoResultado;

    public ToastNotificationService()
    {
        // Um único assinante para todo o tempo de vida do app — evita empilhar handlers
        // duplicados a cada ciclo de execução (cada notificação de início registrava um novo
        // handler antes desta correção, um vazamento pequeno mas real ao longo do tempo).
        ToastNotificationManagerCompat.OnActivated += OnToastAtivado;
    }

    private void OnToastAtivado(ToastNotificationActivatedEventArgsCompat e)
    {
        if (e.Argument.Contains("acao=cancelar"))
        {
            _canceladoPeloUsuario = true;
            return;
        }

        if (_ultimoResultado is not null &&
            (e.Argument.Contains("acao=ver_resultado") || e.Argument.Contains("acao=abrir_pendencias")))
        {
            MostrarRelatorioResultado(_ultimoResultado);
        }
    }

    public Task<bool> NotificarInicioComCancelamentoAsync(int janelaCancelamentoSegundos)
    {
        _canceladoPeloUsuario = false;

        new ToastContentBuilder()
            .AddText("CubeBin")
            .AddText($"A organização vai começar em {janelaCancelamentoSegundos}s.")
            .AddButton(new ToastButton()
                .SetContent("Cancelar")
                .AddArgument("acao", "cancelar")
                .SetBackgroundActivation())
            .Show(toast => toast.Tag = TagCancelamento);

        return AguardarJanelaAsync(janelaCancelamentoSegundos);
    }

    private async Task<bool> AguardarJanelaAsync(int segundos)
    {
        var decorrido = 0;
        while (decorrido < segundos)
        {
            if (_canceladoPeloUsuario)
            {
                ToastNotificationManagerCompat.History.Remove(TagCancelamento);
                return true;
            }

            await Task.Delay(1000);
            decorrido++;
        }

        ToastNotificationManagerCompat.History.Remove(TagCancelamento);
        return _canceladoPeloUsuario;
    }

    public void NotificarResultadoFinal(ResultadoExecucao resultado)
    {
        _ultimoResultado = resultado;

        var builder = new ToastContentBuilder()
            .AddArgument("acao", "ver_resultado")
            .AddText("CubeBin");

        if (resultado.Cancelado)
        {
            builder.AddText("Execução cancelada pelo usuário.");
        }
        else
        {
            builder.AddText(
                $"{resultado.ArquivosMovidos} movidos, {resultado.ArquivosComFalha} falharam, " +
                $"{resultado.ArquivosPendentes} pendentes. Clique para ver detalhes.");

            if (resultado.ArquivosPendentes > 0)
            {
                builder.AddButton(new ToastButton()
                    .SetContent("Ver pendências")
                    .AddArgument("acao", "abrir_pendencias"));
            }
        }

        builder.Show();
    }

    /// <summary>
    /// Relatório do que deixou de ser organizado e por quê (item 1 do feedback de uso), mostrado
    /// ao clicar na notificação final. O clique chega numa thread própria do sistema de toast,
    /// então despacha para a thread de UI antes de abrir a MessageBox.
    /// </summary>
    private static void MostrarRelatorioResultado(ResultadoExecucao resultado)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (resultado.Falhas.Count == 0)
            {
                var extras = resultado.ArquivosPendentes > 0
                    ? $"\n\n{resultado.ArquivosPendentes} arquivo(s) ficaram pendentes (extensão sem regra cadastrada) — veja a aba \"Pendências\"."
                    : "";

                System.Windows.MessageBox.Show(
                    $"{resultado.ArquivosMovidos} arquivo(s) organizado(s) com sucesso.{extras}",
                    "Resultado da organização",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            var detalhes = string.Join("\n", resultado.Falhas.Select(f => $"• {Path.GetFileName(f.Arquivo)}: {f.Motivo}"));
            var avisoRecall = resultado.AvisosRecall.Count > 0
                ? $"\n\n(Além disso, {resultado.AvisosRecall.Count} arquivo(s) organizado(s) com sucesso não puderam ser registrados no Recall — a organização em si não foi afetada.)"
                : "";

            System.Windows.MessageBox.Show(
                $"{resultado.ArquivosMovidos} arquivo(s) organizado(s) com sucesso.\n" +
                $"{resultado.ArquivosComFalha} arquivo(s) NÃO puderam ser organizados:\n\n{detalhes}{avisoRecall}",
                "Alguns arquivos não puderam ser organizados",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        });
    }

    public void NotificarMensagemSimples(string titulo, string mensagem)
    {
        new ToastContentBuilder()
            .AddText(titulo)
            .AddText(mensagem)
            .Show();
    }

    public bool ConfirmarRiscoSubpastas(string nomePasta)
    {
        var mensagem =
            $"Ativar a organização de subpastas em \"{nomePasta}\" pode mover arquivos que estão " +
            "dentro de estruturas de pastas já organizadas manualmente, alterando essa organização. Continuar?";

        var resultado = System.Windows.MessageBox.Show(
            mensagem,
            "Aviso de risco",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        return resultado == System.Windows.MessageBoxResult.Yes;
    }
}
