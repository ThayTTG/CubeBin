using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Telas;

/// <summary>
/// A tela de regras do PRD: cada extensão é um bloquinho, e a pessoa arrasta o bloquinho até o bloco
/// da pasta onde ela deve ficar — sem editar o JSON. Soltar em "Nova pasta" cria uma pasta
/// personalizada com aquela regra; soltar em "Deixar onde está" faz o tipo nunca ser movido.
/// Clicar num bloquinho abre o mesmo "Mover para…" num menu (para quem não usa o mouse para arrastar).
/// </summary>
public class PainelRegras : UserControl
{
    private const string PrefixoArraste = "cubebin-ext:";
    private readonly ServicosApp _servicos;
    private readonly Grid _grade = new() { ColumnDefinitions = new ColumnDefinitions("*,14,*"), RowDefinitions = new RowDefinitions("Auto,14,Auto,Auto") };

    public PainelRegras(ServicosApp servicos)
    {
        _servicos = servicos;
        Content = new ScrollViewer { Content = _grade };
        Atualizar();
    }

    public void Atualizar()
    {
        var config = _servicos.Config.Carregar();
        _grade.Children.Clear();

        // ---------- Em cima: o cartão grande com os bloquinhos sem pasta ----------

        var semPasta = new WrapPanel();
        foreach (var ext in config.ExtensoesSemPasta.OrderBy(e => e))
            semPasta.Children.Add(CriarBloquinho(ext, novo: true));
        if (semPasta.Children.Count == 0)
            semPasta.Children.Add(Ui.Texto("Nenhum tipo esperando. Quando o CubeBin encontrar um tipo novo, ele aparece aqui.", "pequeno"));

        var adicionar = new TextBox { Watermark = "Adicionar extensão (ex.: .zip)", MinWidth = 160 };
        adicionar.Classes.Add("field");
        adicionar.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(adicionar.Text)) return;
            var ext = Extensoes.Normalizar(adicionar.Text);
            _servicos.Config.Alterar(c => ConfiguracaoService.AnotarSemPasta(c, new[] { ext }));
            Atualizar();
        };

        var cartaoSemPasta = new StackPanel { Spacing = 10 };
        cartaoSemPasta.Children.Add(Ui.Texto("SEM PASTA", "caps"));
        cartaoSemPasta.Children.Add(Ui.Texto("Arraste cada bloquinho até a pasta onde esse tipo de arquivo deve ficar.", "secondary"));
        cartaoSemPasta.Children.Add(semPasta);
        cartaoSemPasta.Children.Add(adicionar);
        var bordaSemPasta = new Border { Child = cartaoSemPasta };
        bordaSemPasta.Classes.Add("card");
        _grade.Children.Add(bordaSemPasta);

        var ignoradas = new WrapPanel();
        foreach (var ext in config.ExtensoesIgnoradas.OrderBy(e => e))
            ignoradas.Children.Add(CriarBloquinho(ext, novo: false));
        var conteudoFica = new StackPanel { Spacing = 8 };
        var cabecalhoFica = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        cabecalhoFica.Children.Add(Ui.Icone("IconeEscudo", 16));
        cabecalhoFica.Children.Add(Ui.Texto("Deixar onde está", "h3"));
        conteudoFica.Children.Add(cabecalhoFica);
        conteudoFica.Children.Add(Ui.Texto("Tipos que o CubeBin nunca move.", "pequeno"));
        conteudoFica.Children.Add(ignoradas);
        var blocoFica = new Border { Child = conteudoFica };
        blocoFica.Classes.Add("bloco");
        TornarAlvo(blocoFica, ext => _servicos.Config.Alterar(c => ConfiguracaoService.IgnorarExtensao(c, ext)));
        Grid.SetColumn(blocoFica, 2);
        _grade.Children.Add(blocoFica);

        var dica = Ui.Texto("Atalhos e programas (.lnk, .exe…) nunca são movidos, qualquer que seja a regra. " +
                            "Quem preferir pode editar tudo no arquivo de configurações (link ao lado).", "pequeno");
        dica.Margin = new Thickness(0, 4, 0, 0);
        Grid.SetRow(dica, 3);
        Grid.SetColumnSpan(dica, 3);
        _grade.Children.Add(dica);

        // ---------- Embaixo: os blocos das pastas, em duas colunas ----------
        var pastas = new UniformGrid { Columns = 2 };
        foreach (var pasta in config.Pastas)
            pastas.Children.Add(CriarBlocoPasta(pasta, config));

        var conteudoNova = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var iconeNova = Ui.Icone("IconePastaMais", 26);
        iconeNova.HorizontalAlignment = HorizontalAlignment.Center;
        conteudoNova.Children.Add(iconeNova);
        conteudoNova.Children.Add(new TextBlock { Text = "Nova pasta", FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        conteudoNova.Children.Add(new TextBlock { Text = "Solte um bloquinho aqui", HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7, FontSize = 12 });
        var blocoNova = new Border { Child = conteudoNova, Margin = new Thickness(0, 0, 12, 12), MinHeight = 110 };
        blocoNova.Classes.Add("soltar");
        TornarAlvo(blocoNova, ext => _ = CriarPastaComRegraAsync(ext), salvar: false);
        pastas.Children.Add(blocoNova);

        Grid.SetRow(pastas, 2);
        Grid.SetColumnSpan(pastas, 3);
        pastas.Margin = new Thickness(0, 0, -12, 0);
        _grade.Children.Add(pastas);
    }

    private Control CriarBlocoPasta(PastaConfig pasta, Configuracoes config)
    {
        var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var icone = Ui.IconeDeBloco(Ui.IconeDaPasta(pasta), 34);
        icone.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icone, Dock.Left);
        cabecalho.Children.Add(icone);
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock { Text = pasta.Nome, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        textos.Children.Add(new TextBlock { Text = pasta.EhPadrao ? "Pasta padrão" : "Pasta personalizada", FontSize = 12, Opacity = 0.7 });
        cabecalho.Children.Add(textos);

        var chips = new WrapPanel();
        var regra = config.Regras.FirstOrDefault(r => r.PastaId == pasta.Id);
        foreach (var ext in regra?.Extensoes ?? new List<string>())
            chips.Children.Add(CriarBloquinho(ext, novo: false));

        var pilha = new StackPanel();
        pilha.Children.Add(cabecalho);
        pilha.Children.Add(chips);
        var bloco = new Border { Child = pilha, Margin = new Thickness(0, 0, 12, 12) };
        bloco.Classes.Add("bloco");
        ToolTip.SetTip(bloco, pasta.CaminhoEfetivo());
        TornarAlvo(bloco, ext => _servicos.Config.Alterar(c => ConfiguracaoService.AtribuirExtensao(c, ext, pasta.Id)));
        return bloco;
    }

    // ---------- Bloquinho arrastável ----------

    private Control CriarBloquinho(string extensao, bool novo)
    {
        var chip = new Border { Child = new TextBlock { Text = extensao } };
        chip.Classes.Add("ext");
        if (novo) chip.Classes.Add("novo");
        ToolTip.SetTip(chip, "Arraste até uma pasta, ou clique para escolher");

        Point? inicio = null;
        var arrastando = false;
        chip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) inicio = e.GetPosition(chip);
        };
        chip.PointerMoved += async (_, e) =>
        {
            if (inicio is null || arrastando) return;
            var agora = e.GetPosition(chip);
            if (Math.Abs(agora.X - inicio.Value.X) + Math.Abs(agora.Y - inicio.Value.Y) < 5) return;
            arrastando = true;
            try
            {
                var dados = new DataTransfer();
                dados.Add(DataTransferItem.CreateText(PrefixoArraste + extensao));
                chip.Opacity = 0.5;
                await DragDrop.DoDragDropAsync(e, dados, DragDropEffects.Move);
            }
            finally
            {
                chip.Opacity = 1;
                arrastando = false;
                inicio = null;
            }
        };
        chip.PointerReleased += (_, _) =>
        {
            if (inicio is not null && !arrastando) MostrarMenu(chip, extensao);
            inicio = null;
        };
        return chip;
    }

    private void MostrarMenu(Control chip, string extensao)
    {
        var config = _servicos.Config.Carregar();
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem { Header = $"Mover {extensao} para…", IsEnabled = false });
        foreach (var pasta in config.Pastas)
        {
            var item = new MenuItem { Header = pasta.Nome };
            item.Click += (_, _) =>
            {
                _servicos.Config.Alterar(c => ConfiguracaoService.AtribuirExtensao(c, extensao, pasta.Id));
                Atualizar();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var nova = new MenuItem { Header = "Nova pasta…" };
        nova.Click += (_, _) => _ = CriarPastaComRegraAsync(extensao);
        menu.Items.Add(nova);
        var ficar = new MenuItem { Header = "Deixar onde está" };
        ficar.Click += (_, _) =>
        {
            _servicos.Config.Alterar(c => ConfiguracaoService.IgnorarExtensao(c, extensao));
            Atualizar();
        };
        menu.Items.Add(ficar);
        menu.ShowAt(chip);
    }

    private void TornarAlvo(Border alvo, Action<string> aoSoltar, bool salvar = true)
    {
        DragDrop.SetAllowDrop(alvo, true);
        alvo.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var texto = e.DataTransfer.TryGetText();
            e.DragEffects = texto?.StartsWith(PrefixoArraste, StringComparison.Ordinal) == true ? DragDropEffects.Move : DragDropEffects.None;
            alvo.Classes.Set("alvo", e.DragEffects != DragDropEffects.None);
        });
        alvo.AddHandler(DragDrop.DragLeaveEvent, (_, _) => alvo.Classes.Set("alvo", false));
        alvo.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            alvo.Classes.Set("alvo", false);
            var texto = e.DataTransfer.TryGetText();
            if (texto is null || !texto.StartsWith(PrefixoArraste, StringComparison.Ordinal)) return;
            aoSoltar(texto[PrefixoArraste.Length..]);
            if (salvar) Atualizar();
        });
    }

    /// <summary>"Nova pasta": pede o nome, cria em Documentos e manda a extensão para ela.</summary>
    private async Task CriarPastaComRegraAsync(string extensao)
    {
        var nome = await JanelaTexto.PedirAsync(TopLevel.GetTopLevel(this) as Window, "Nova pasta",
            $"Os arquivos {extensao} vão para uma pasta nova dentro de Documentos. Qual o nome dela?",
            extensao.TrimStart('.').ToUpperInvariant(), "Criar pasta");
        if (string.IsNullOrWhiteSpace(nome)) return;
        if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || nome is "." or "..")
        {
            Dialogo.Show("Esse nome tem caracteres que não podem ser usados em nomes de pasta.", "Nova pasta");
            return;
        }

        try
        {
            var caminho = Path.Combine(PastasDoSistema.Obter(PastaPadrao.Documentos), nome);
            _servicos.Config.Alterar(c =>
            {
                var pasta = ConfiguracaoService.CriarPastaPersonalizada(c, nome, caminho, monitorar: false);
                ConfiguracaoService.AtribuirExtensao(c, extensao, pasta.Id);
            });
            _servicos.Ambiente.Log($"Pasta personalizada criada: {caminho} (regra: {extensao})");
        }
        catch (Exception ex)
        {
            Dialogo.Show(ex.Message, "Nova pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Atualizar();
    }
}
