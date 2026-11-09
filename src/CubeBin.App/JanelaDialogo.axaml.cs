using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace CubeBin.App;

/// <summary>
/// Janela do diálogo temático. Não use diretamente: chame DialogoCubeBin.Show(...).
/// </summary>
public partial class JanelaDialogo : Window
{
    public MessageBoxResult Resultado { get; private set; }

    public JanelaDialogo() : this("", "CubeBin", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, null, null, false)
    {
        // Só para o previewer do Avalonia.
    }

    internal JanelaDialogo(string mensagem, string titulo, MessageBoxButton botoes, MessageBoxImage icone,
        MessageBoxResult padrao, string? textoConfirmar, string? textoCancelar, bool perigo)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);

        Title = string.IsNullOrWhiteSpace(titulo) ? "CubeBin" : titulo;
        TxtTitulo.Text = Title;
        TxtMensagem.Text = mensagem;
        AplicarIcone(icone);

        // Resultado ao fechar pelo X / Esc, como na MessageBox: "Não"/"Cancelar" quando existem.
        Resultado = botoes switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK
        };

        // Esc fecha com o resultado padrão definido logo acima.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
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
        var botao = new Button { Content = texto, MinWidth = 96 };
        botao.Classes.Add("ic");

        if (perigo)
        {
            botao.Classes.Add("danger");
            botao.Tag = this.FindResource("IconeLixeira");
        }
        else
        {
            botao.Classes.Add(principal ? "primary" : "secondary");
        }

        // Botão padrão (Enter): o que a chamada pediu; sem pedido, o principal. Nas exclusões o
        // código pede "Não" como padrão, então Enter nunca apaga nada por engano.
        var ehPadrao = padrao == MessageBoxResult.None ? principal : padrao == resultado;
        botao.IsDefault = ehPadrao;
        if (ehPadrao)
            Opened += (_, _) => botao.Focus();

        botao.Click += (_, _) =>
        {
            Resultado = resultado;
            Close();
        };

        PainelBotoes.Children.Add(botao);
    }

    private void AplicarIcone(MessageBoxImage icone)
    {
        var (chaveIcone, chaveCor, chaveFundo) = icone switch
        {
            MessageBoxImage.Error => ("IconeErro", "DangerBrush", "DangerSoftBrush"),
            MessageBoxImage.Warning => ("IconeAlerta", "WarningBrush", "WarningSoftBrush"),
            MessageBoxImage.Question => ("IconeDuvida", "AccentTextBrush", "AccentSoftBrush"),
            _ => ("IconeInfo", "AccentTextBrush", "AccentSoftBrush")
        };

        if (this.FindResource(chaveIcone) is Geometry geometria) IconeDialogo.Data = geometria;
        if (this.FindResource(chaveCor) is IBrush cor) IconeDialogo.Stroke = cor;
        if (this.FindResource(chaveFundo) is IBrush fundo) FundoIcone.Background = fundo;
    }
}
