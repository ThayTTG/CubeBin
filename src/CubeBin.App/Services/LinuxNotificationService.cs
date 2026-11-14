using System.IO;
using Avalonia.Threading;
using CubeBin.Core.Interop;
using CubeBin.Core.Models;
using CubeBin.Core.Services;

namespace CubeBin.App.Services;

/// <summary>
/// Notificações no Linux (o par do ToastNotificationService do Windows):
/// - Aviso de início: uma janelinha com contagem regressiva e "Cancelar" (JanelaContagem). É mais
///   garantido que botões em notificação, que nem todo ambiente mostra.
/// - Resumo final e avisos simples: notificação do sistema via notify-send (libnotify). Quando o
///   notify-send aceita botões (libnotify 0.7.10+), o resumo ganha "Ver detalhes", que abre o mesmo
///   relatório do clique no toast do Windows.
/// Sem notify-send instalado, os avisos simples não aparecem (o resto do app segue normal).
/// </summary>
public class LinuxNotificationService : INotificationService
{
    private static readonly string? CaminhoIcone = ObterCaminhoIcone();

    public Task<bool> NotificarInicioComCancelamentoAsync(int janelaCancelamentoSegundos) =>
        Dispatcher.UIThread.InvokeAsync(() => JanelaContagem.MostrarAsync(janelaCancelamentoSegundos));

    public void NotificarResultadoFinal(ResultadoExecucao resultado)
    {
        if (resultado.Cancelado)
        {
            Enviar("CubeBin", "Execução cancelada pelo usuário.");
            return;
        }

        var texto = $"{resultado.ArquivosMovidos} movidos, {resultado.ArquivosComFalha} falharam, " +
                    $"{resultado.ArquivosPendentes} pendentes.";

        // Em segundo plano: com botão, o notify-send fica esperando até a notificação sumir.
        _ = Task.Run(() =>
        {
            var resposta = ComandoExterno.Executar("notify-send",
                MontarArgumentos("CubeBin", texto, "--action=ver=Ver detalhes", "--wait"),
                limite: TimeSpan.FromMinutes(30));

            if (resposta is { Codigo: 0 })
            {
                if (resposta.Saida.Trim() == "ver")
                    AvisosComuns.MostrarRelatorioResultado(resultado);
                return;
            }

            // notify-send antigo (sem botões) ou ausente: aviso simples.
            Enviar("CubeBin", texto + " Abra o CubeBin para ver as pendências.");
        });
    }

    public void NotificarMensagemSimples(string titulo, string mensagem) => Enviar(titulo, mensagem);

    public bool ConfirmarRiscoSubpastas(string nomePasta) => AvisosComuns.ConfirmarRiscoSubpastas(nomePasta);

    private static void Enviar(string titulo, string mensagem) =>
        ComandoExterno.Abrir("notify-send", MontarArgumentos(titulo, mensagem).ToArray());

    private static List<string> MontarArgumentos(string titulo, string mensagem, params string[] extras)
    {
        var argumentos = new List<string> { "--app-name=CubeBin" };
        if (CaminhoIcone is not null)
            argumentos.Add($"--icon={CaminhoIcone}");
        argumentos.AddRange(extras);
        argumentos.Add(titulo);
        argumentos.Add(mensagem);
        return argumentos;
    }

    /// <summary>O icon.png que vai junto do executável no pacote Linux.</summary>
    private static string? ObterCaminhoIcone()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "icon.png");
        return File.Exists(caminho) ? caminho : null;
    }
}
