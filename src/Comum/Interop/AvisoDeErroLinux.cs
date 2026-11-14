namespace Comum.Interop;

/// <summary>
/// Aviso de erro grave no Linux, sem depender do visual do app (o par da MessageBox nativa do
/// Windows): zenity (GNOME e a maioria) ou kdialog (KDE); sem nenhum dos dois, ao menos uma
/// notificação. Espera a pessoa fechar o aviso.
/// </summary>
public static class AvisoDeErroLinux
{
    public static void Mostrar(string nomeDoApp, string titulo, string mensagem)
    {
        var limite = TimeSpan.FromMinutes(30);
        if (ComandoExterno.Executar("zenity", new[] { "--error", $"--title={titulo}", $"--text={mensagem}", "--no-markup" },
                limite: limite) is not null)
            return;
        if (ComandoExterno.Executar("kdialog", new[] { "--title", titulo, "--error", mensagem }, limite: limite) is not null)
            return;
        NotificacaoLinux.Enviar(nomeDoApp, titulo, mensagem, urgente: true);
    }
}
