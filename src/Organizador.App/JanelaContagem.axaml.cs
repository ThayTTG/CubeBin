using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Organizador.App;

/// <summary>
/// Janelinha com a contagem regressiva antes da organização automática (Linux). Fecha sozinha
/// quando o tempo acaba. O resultado diz se o usuário cancelou.
/// </summary>
public partial class JanelaContagem : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TaskCompletionSource<bool> _resposta = new();
    private int _restantes;

    public JanelaContagem() : this(60) { }

    public JanelaContagem(int segundos)
    {
        InitializeComponent();
        Title = InfoApp.Nome;

        _restantes = Math.Max(1, segundos);
        BarraContagem.Maximum = _restantes;
        AtualizarTexto();

        _timer.Tick += (_, _) =>
        {
            _restantes--;
            if (_restantes <= 0)
            {
                Responder(cancelado: false);
                return;
            }
            AtualizarTexto();
        };

        Opened += (_, _) => _timer.Start();
        // Fechar pelo X conta como "deixar seguir", igual a ignorar o toast no Windows.
        Closed += (_, _) =>
        {
            _timer.Stop();
            _resposta.TrySetResult(false);
        };
    }

    /// <summary>Mostra a janela e espera: true se o usuário cancelou, false se o tempo acabou.</summary>
    public static Task<bool> MostrarAsync(int segundos)
    {
        var janela = new JanelaContagem(segundos);
        janela.Show();
        return janela._resposta.Task;
    }

    private void AtualizarTexto()
    {
        TxtContagem.Text = $"O {InfoApp.Nome} vai organizar as suas pastas em {_restantes} s. " +
                           "Se você estiver usando o computador agora, clique em Cancelar.";
        BarraContagem.Value = BarraContagem.Maximum - _restantes;
    }

    private void Responder(bool cancelado)
    {
        _timer.Stop();
        if (!_resposta.TrySetResult(cancelado)) return;
        Close();
    }

    private void BtnCancelar_Click(object? sender, RoutedEventArgs e) => Responder(cancelado: true);

    private void BtnComecarAgora_Click(object? sender, RoutedEventArgs e) => Responder(cancelado: false);
}
