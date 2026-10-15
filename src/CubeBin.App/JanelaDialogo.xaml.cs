using System.Windows;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;

namespace CubeBin.App;

/// <summary>
/// Janela do diálogo temático. Não use diretamente: chame DialogoCubeBin.Show(...).
/// </summary>
public partial class JanelaDialogo : Window
{
    public MessageBoxResult Resultado { get; private set; }

    internal JanelaDialogo(string mensagem, string titulo, MessageBoxButton botoes, MessageBoxImage icone,
        MessageBoxResult padrao, string? textoConfirmar, string? textoCancelar, bool perigo)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);

        Title = string.IsNullOrWhiteSpace(titulo) ? "CubeBin" : titulo;
        TxtTitulo.Text = Title;
        TxtMensagem.Text = mensagem;
        AplicarIcone(icone);

        // Esc fecha como "Não"/"Cancelar" (o resultado padrão definido logo abaixo). Feito à mão
        // em vez de IsCancel, porque um botão IsCancel tenta fechar a janela uma segunda vez
        // depois do próprio clique já ter fechado.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                DialogResult = false;
        };

        // Resultado ao fechar pelo X / Esc, como na MessageBox: "Não"/"Cancelar" quando existem.
        Resultado = botoes switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK
        };

        // Ordem do guia: ações à direita, a principal por último.
        switch (botoes)
        {
            case MessageBoxButton.YesNo:
                AdicionarBotao(textoCancelar ?? "Não", MessageBoxResult.No, principal: false, perigo: false, padrao);
                AdicionarBotao(textoConfirmar ?? "Sim", MessageBoxResult.Yes, principal: true, perigo, padrao);
                break;
            case MessageBoxButton.YesNoCancel:
                AdicionarBotao(textoCancelar ?? "Cancelar", MessageBoxResult.Cancel, principal: false, perigo: false, padrao);
                AdicionarBotao("Não", MessageBoxResult.No, principal: false, perigo: false, padrao);
                AdicionarBotao(textoConfirmar ?? "Sim", MessageBoxResult.Yes, principal: true, perigo, padrao);
                break;
            case MessageBoxButton.OKCancel:
                AdicionarBotao(textoCancelar ?? "Cancelar", MessageBoxResult.Cancel, principal: false, perigo: false, padrao);
                AdicionarBotao(textoConfirmar ?? "OK", MessageBoxResult.OK, principal: true, perigo, padrao);
                break;
            default:
                AdicionarBotao(textoConfirmar ?? "OK", MessageBoxResult.OK, principal: true, perigo: false, padrao);
                break;
        }
    }

    private void AdicionarBotao(string texto, MessageBoxResult resultado, bool principal, bool perigo,
        MessageBoxResult padrao)
    {
        var botao = new Button
        {
            Content = texto,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 88
        };

        if (perigo)
        {
            botao.Style = (Style)FindResource("BotaoPerigoStyle");
            botao.Tag = FindResource("IconeLixeira");
        }
        else if (principal)
        {
            botao.Style = (Style)FindResource("PrimaryButtonStyle");
        }

        // Botão padrão (Enter): o que a chamada pediu; sem pedido, o principal. Nas exclusões o
        // código pede "Não" como padrão, então Enter nunca apaga nada por engano.
        var ehPadrao = padrao == MessageBoxResult.None ? principal : padrao == resultado;
        botao.IsDefault = ehPadrao;
        if (ehPadrao)
            Loaded += (_, _) => botao.Focus();

        botao.Click += (_, _) =>
        {
            Resultado = resultado;
            DialogResult = true;
        };

        PainelBotoes.Children.Add(botao);
    }

    private void AplicarIcone(MessageBoxImage icone)
    {
        // MessageBoxImage tem valores repetidos (Error = Hand = Stop; Warning = Exclamation;
        // Information = Asterisk), então o switch usa só um nome de cada.
        var (chaveIcone, chaveCor, chaveFundo) = icone switch
        {
            MessageBoxImage.Error => ("IconeErro", "PerigoBrush", "PerigoSuaveBrush"),
            MessageBoxImage.Warning => ("IconeAlerta", "AtencaoBrush", "AtencaoSuaveBrush"),
            MessageBoxImage.Question => ("IconeDuvida", "AccentLightBrush", "AccentSoftBrush"),
            _ => ("IconeInfo", "AccentLightBrush", "AccentSoftBrush")
        };

        IconeDialogo.Data = (Geometry)FindResource(chaveIcone);
        IconeDialogo.Stroke = (Brush)FindResource(chaveCor);
        FundoIcone.Background = (Brush)FindResource(chaveFundo);
    }
}
