using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace CubeBin.App.Interface;

/// <summary>Uma janelinha que pede um texto (ex.: o nome de uma pasta nova), no visual da NokyLabs.</summary>
public class JanelaTexto : Window
{
    private readonly TextBox _campo;
    private string? _resposta;

    private JanelaTexto(string titulo, string pergunta, string valorInicial, string textoConfirmar)
    {
        Title = titulo;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Dialogo.Icone is not null) Icon = Dialogo.Icone;
        TemaJanelaEscura.Aplicar(this);

        _campo = new TextBox { Text = valorInicial, Margin = new Thickness(0, 12, 0, 0) };
        _campo.Classes.Add("field");
        _campo.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Confirmar();
            if (e.Key == Key.Escape) Close();
        };

        var cancelar = Ui.Botao("Cancelar", "secondary", null, (_, _) => Close());
        var confirmar = Ui.Botao(textoConfirmar, "primary", "IconeVisto", (_, _) => Confirmar());
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        botoes.Children.Add(cancelar);
        botoes.Children.Add(confirmar);

        var pilha = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        pilha.Children.Add(Ui.Texto(titulo, "h3"));
        pilha.Children.Add(Ui.Texto(pergunta, "secondary"));
        pilha.Children.Add(_campo);
        pilha.Children.Add(botoes);
        Content = pilha;

        Opened += (_, _) =>
        {
            _campo.Focus();
            _campo.SelectAll();
        };
    }

    private void Confirmar()
    {
        if (string.IsNullOrWhiteSpace(_campo.Text)) return;
        _resposta = _campo.Text.Trim();
        Close();
    }

    /// <summary>Pede um texto e espera. Devolve null se a pessoa cancelar.</summary>
    public static async Task<string?> PedirAsync(Window? dona, string titulo, string pergunta,
        string valorInicial = "", string textoConfirmar = "OK")
    {
        var janela = new JanelaTexto(titulo, pergunta, valorInicial, textoConfirmar);
        if (dona is not null) await janela.ShowDialog(dona);
        else
        {
            var fim = new TaskCompletionSource();
            janela.Closed += (_, _) => fim.TrySetResult();
            janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            janela.Show();
            await fim.Task;
        }
        return janela._resposta;
    }
}
