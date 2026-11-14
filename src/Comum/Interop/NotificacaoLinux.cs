namespace Comum.Interop;

/// <summary>
/// Notificação do sistema no Linux, pelo notify-send (libnotify). É o par dos toasts do Windows
/// para os dois apps da família. Sem notify-send instalado, simplesmente não aparece nada.
/// </summary>
public static class NotificacaoLinux
{
    /// <summary>O icon.png que vai solto ao lado do executável no pacote Linux.</summary>
    private static readonly string? CaminhoIcone = ObterCaminhoIcone();

    /// <summary>Aviso simples, sem esperar.</summary>
    public static void Enviar(string nomeDoApp, string titulo, string mensagem, bool urgente = false)
    {
        var extras = urgente ? new[] { "--urgency=critical" } : Array.Empty<string>();
        ComandoExterno.Abrir("notify-send", MontarArgumentos(nomeDoApp, titulo, mensagem, extras).ToArray());
    }

    /// <summary>
    /// Aviso com um botão. Espera a notificação sumir (até 30 min) e devolve true se o botão foi
    /// clicado. Devolve null quando o notify-send não aceita botões (libnotify antes da 0.7.10)
    /// ou não está instalado: aí quem chamou decide o aviso simples. Chame fora da thread da tela.
    /// </summary>
    public static bool? EnviarComBotao(string nomeDoApp, string titulo, string mensagem, string textoBotao)
    {
        var resposta = ComandoExterno.Executar("notify-send",
            MontarArgumentos(nomeDoApp, titulo, mensagem, $"--action=botao={textoBotao}", "--wait"),
            limite: TimeSpan.FromMinutes(30));

        if (resposta is not { Codigo: 0 }) return null;
        return resposta.Saida.Trim() == "botao";
    }

    private static List<string> MontarArgumentos(string nomeDoApp, string titulo, string mensagem, params string[] extras)
    {
        var argumentos = new List<string> { $"--app-name={nomeDoApp}" };
        if (CaminhoIcone is not null)
            argumentos.Add($"--icon={CaminhoIcone}");
        argumentos.AddRange(extras);
        argumentos.Add(titulo);
        argumentos.Add(mensagem);
        return argumentos;
    }

    private static string? ObterCaminhoIcone()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "icon.png");
        return File.Exists(caminho) ? caminho : null;
    }
}
