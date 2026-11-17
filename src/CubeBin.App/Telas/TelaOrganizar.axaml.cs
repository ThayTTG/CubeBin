using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Telas;

/// <summary>O fluxo manual do Organizar Arquivos, em 4 etapas (PRD 5.1).</summary>
public partial class TelaOrganizar : UserControl, ITela
{
    private enum Etapa { Previa, Confirmacao, Execucao, Conclusao }

    private readonly ServicosApp _servicos;
    private readonly MainWindow _janela;
    private Etapa _etapa = Etapa.Previa;
    private PlanoOrganizacao? _plano;
    private ResultadoOrganizacao? _resultado;
    private CancellationTokenSource? _cancelar;

    public TelaOrganizar() : this(App.Servicos!, null!) { }

    public TelaOrganizar(ServicosApp servicos, MainWindow janela)
    {
        _servicos = servicos;
        _janela = janela;
        InitializeComponent();
    }

    public void AoEntrar(object? parametro)
    {
        // No meio de uma organização, só volta para ela.
        if (_etapa == Etapa.Execucao) return;
        _ = MontarPreviaAsync();
    }

    // ===================== 1. Prévia =====================

    private async Task MontarPreviaAsync()
    {
        MostrarEtapa(Etapa.Previa);
        PainelCarregando.IsVisible = true;
        TxtCarregando.Text = "Olhando suas pastas…";
        BtnAvancar.IsEnabled = false;
        ListaDestinos.Children.Clear();
        ListaFica.Children.Clear();

        try
        {
            _plano = await Task.Run(() => _servicos.NovoPlanejador().Planejar());
        }
        catch (Exception ex)
        {
            App.RegistrarCrash(ex);
            TxtCarregando.Text = "Não foi possível olhar as pastas agora.";
            return;
        }

        _servicos.AnotarSemPasta(_plano.ExtensoesSemPasta);
        PainelCarregando.IsVisible = false;
        PreencherPrevia(_plano);
        BtnAvancar.IsEnabled = _plano.Movimentos.Count > 0;
    }

    private void PreencherPrevia(PlanoOrganizacao plano)
    {
        var config = _servicos.Config.Carregar();
        var n = plano.Movimentos.Count;
        TxtResumoPrevia.Text = n == 0
            ? "Tudo em ordem por aqui!"
            : $"{Ui.Plural(n, "arquivo", "arquivos")} · {FormatarBytes.Formatar(plano.BytesAMover)}";

        if (n == 0)
        {
            ListaDestinos.Children.Add(Ui.Texto(
                "Nenhum arquivo precisa mudar de lugar agora. Os arquivos das pastas acompanhadas já estão nas pastas certas, " +
                "ou ficaram de fora por um dos motivos ao lado.", "secondary"));
        }

        foreach (var grupo in plano.PorDestino)
            ListaDestinos.Children.Add(CriarBlocoDestino(grupo.Key, grupo.ToList(), config));

        PreencherFica(plano);
    }

    private Control CriarBlocoDestino(string nomeDestino, List<Movimento> movimentos, Configuracoes config)
    {
        var pasta = config.ObterPasta(movimentos[0].PastaDestinoId);
        var cabecalho = new DockPanel();
        var icone = Ui.IconeDeBloco(Ui.IconeDaPasta(pasta));
        icone.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(icone, Dock.Left);
        cabecalho.Children.Add(icone);

        var origens = string.Join(", ", movimentos.Select(m => m.PastaOrigemNome).Distinct());
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        textos.Children.Add(Ui.Texto(nomeDestino, "h3"));
        textos.Children.Add(Ui.Texto($"{Ui.Plural(movimentos.Count, "arquivo", "arquivos")} · " +
                                     $"{FormatarBytes.Formatar(movimentos.Sum(m => m.Tamanho))} · de {origens}", "secondary"));
        if (!Directory.Exists(movimentos[0].PastaDestinoCaminho))
            textos.Children.Add(Ui.Texto("A pasta ainda não existe: será criada.", "pequeno"));
        cabecalho.Children.Add(textos);

        var lista = new StackPanel { Spacing = 0, Margin = new Thickness(52, 6, 0, 0) };
        foreach (var mov in movimentos.Take(300))
        {
            var linha = new Border { Child = Ui.Texto(mov.Renomeado
                ? $"{mov.NomeArquivo}  →  {mov.NomeFinal}   (de {mov.PastaOrigemNome}; o nome já existia)"
                : $"{mov.NomeArquivo}   (de {mov.PastaOrigemNome})", "secondary") };
            linha.Classes.Add("linha");
            ToolTip.SetTip(linha, mov.Origem);
            lista.Children.Add(linha);
        }
        if (movimentos.Count > 300)
            lista.Children.Add(Ui.Texto($"e mais {movimentos.Count - 300}…", "pequeno"));

        var bloco = new Border
        {
            Child = new Expander { Header = cabecalho, Content = lista, HorizontalAlignment = HorizontalAlignment.Stretch },
        };
        bloco.Classes.Add("bloco");
        bloco.Padding = new Thickness(8, 6);
        return bloco;
    }

    private void PreencherFica(PlanoOrganizacao plano)
    {
        var extensoes = plano.SemPasta.GroupBy(Extensoes.DoArquivo).Where(g => g.Key.Length > 0)
            .OrderByDescending(g => g.Count()).ToList();
        if (extensoes.Count > 0)
        {
            ListaFica.Children.Add(Ui.Texto("Sem pasta definida", "rotulo"));
            var chips = new WrapPanel();
            foreach (var g in extensoes)
            {
                var chip = new Border { Child = new TextBlock { Text = $"{g.Key}  ·  {g.Count()}" } };
                chip.Classes.Add("ext");
                chip.Classes.Add("novo");
                chip.Cursor = null;
                ToolTip.SetTip(chip, string.Join("\n", g.Take(10).Select(Path.GetFileName)));
                chips.Children.Add(chip);
            }
            ListaFica.Children.Add(chips);
            var botao = Ui.Botao("Escolher as pastas", "soft", "IconeOrganizar", (_, _) => _janela.Navegar("regras"));
            botao.HorizontalAlignment = HorizontalAlignment.Left;
            ListaFica.Children.Add(botao);
        }

        foreach (var grupo in plano.Pulados.GroupBy(p => p.Motivo).OrderByDescending(g => g.Count()))
        {
            var lista = new StackPanel();
            foreach (var p in grupo.Take(100))
            {
                var linha = new Border { Child = Ui.Texto(Path.GetFileName(p.Caminho), "secondary") };
                linha.Classes.Add("linha");
                ToolTip.SetTip(linha, p.Caminho);
                lista.Children.Add(linha);
            }
            ListaFica.Children.Add(new Expander
            {
                Header = Ui.Texto($"{Protecao.Explicar(grupo.Key)} · {grupo.Count()}", "secondary"),
                Content = lista,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            });
        }

        foreach (var projeto in plano.PastasDeProjeto.Distinct())
            ListaFica.Children.Add(Ui.Aviso($"\"{Path.GetFileName(projeto)}\" parece um projeto (Construct, Unity, código…). " +
                                            "O CubeBin não mexe dentro dela.", "", "IconeEscudo"));
        foreach (var pasta in plano.PastasInexistentes)
            ListaFica.Children.Add(Ui.Aviso($"A pasta \"{pasta}\" não foi encontrada no computador.", "warning", "IconeAlerta"));
        foreach (var pasta in plano.AvisosOneDrive)
            ListaFica.Children.Add(Ui.Aviso($"\"{pasta}\" é sincronizada com o OneDrive e tem muitos arquivos para mover. " +
                                            "Mover tudo de uma vez faz o OneDrive enviar e apagar muita coisa na nuvem.", "warning", "IconeNuvem"));

        if (ListaFica.Children.Count == 0)
            ListaFica.Children.Add(Ui.Texto("Nada ficou de fora.", "secondary"));
    }

    // ===================== 2. Confirmação =====================

    private void MostrarConfirmacao()
    {
        var plano = _plano!;
        var origens = plano.Movimentos.Select(m => m.PastaOrigemNome).Distinct().Count();
        var destinos = plano.Movimentos.Select(m => m.PastaDestinoNome).Distinct().Count();
        TxtConfirmaTitulo.Text = $"Organizar {Ui.Plural(plano.Movimentos.Count, "arquivo", "arquivos")}?";
        TxtConfirmaDetalhe.Text = $"{FormatarBytes.Formatar(plano.BytesAMover)} saindo de {Ui.Plural(origens, "pasta", "pastas")} " +
                                  $"para {Ui.Plural(destinos, "pasta", "pastas")}.";

        ListaConfirmaDestinos.Children.Clear();
        foreach (var g in plano.PorDestino)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            linha.Children.Add(Ui.Icone("IconeSetaDireita", 14));
            linha.Children.Add(Ui.Texto($"{g.Key}: {Ui.Plural(g.Count(), "arquivo", "arquivos")}"));
            ListaConfirmaDestinos.Children.Add(linha);
        }

        var oneDrive = plano.AvisosOneDrive.Count > 0;
        PainelOneDrive.IsVisible = oneDrive;
        ChkOneDrive.IsChecked = false;
        if (oneDrive)
        {
            PainelOneDrive.Children[0] = Ui.Aviso(
                $"Atenção: {string.Join(", ", plano.AvisosOneDrive)} é sincronizada com o OneDrive. Mover muitos arquivos " +
                "faz o OneDrive repetir a mudança na nuvem e em outros computadores.", "warning", "IconeAlerta");
        }
        MostrarEtapa(Etapa.Confirmacao);
        BtnAvancar.IsEnabled = !oneDrive;
    }

    private void ChkOneDrive_Changed(object? sender, RoutedEventArgs e)
    {
        if (_etapa == Etapa.Confirmacao) BtnAvancar.IsEnabled = ChkOneDrive.IsChecked == true;
    }

    // ===================== 3. Execução =====================

    private async Task ExecutarAsync()
    {
        if (!_servicos.TentarOcupar())
        {
            Dialogo.Show("Uma organização já está em andamento. Espere ela terminar e tente de novo.", "Organizar Arquivos");
            return;
        }

        MostrarEtapa(Etapa.Execucao);
        _cancelar = new CancellationTokenSource();
        var plano = _plano!;
        BarraExecucao.Maximum = Math.Max(1, plano.Movimentos.Count);
        var progresso = new Progress<Progresso>(p =>
        {
            BarraExecucao.Value = p.Feitos;
            TxtExecucaoContagem.Text = $"{p.Feitos} de {p.Total}";
            TxtExecucaoArquivo.Text = p.ArquivoAtual;
        });

        try
        {
            var token = _cancelar.Token;
            _resultado = await Task.Run(() => _servicos.NovoOrganizador().Executar(plano, "manual", progresso, token));
            _servicos.AnotarSemPasta(_resultado.ExtensoesSemPasta);
        }
        catch (Exception ex)
        {
            App.RegistrarCrash(ex);
            Dialogo.Show($"A organização parou por um erro inesperado. O que já foi movido está anotado e pode ser desfeito na tela de Cubos.\n\n{ex.Message}",
                "Organizar Arquivos", MessageBoxButton.OK, MessageBoxImage.Error);
            _resultado = null;
        }
        finally
        {
            _servicos.Liberar();
            _cancelar = null;
        }

        MostrarConclusao();
    }

    // ===================== 4. Conclusão =====================

    private void MostrarConclusao()
    {
        MostrarEtapa(Etapa.Conclusao);
        var r = _resultado;
        ListaConclusaoDestinos.Children.Clear();
        ListaConclusaoAvisos.Children.Clear();

        if (r is null)
        {
            TxtConclusaoTitulo.Text = "A organização não terminou";
            TxtConclusaoDetalhe.Text = "Veja os Cubos para conferir ou desfazer o que já foi movido.";
            return;
        }

        var falhas = r.Falhas.Count;
        TxtConclusaoTitulo.Text = r.Cancelado
            ? $"Parado no meio: {Ui.Plural(r.Movidos.Count, "arquivo organizado", "arquivos organizados")}"
            : falhas == 0
                ? $"Pronto! {Ui.Plural(r.Movidos.Count, "arquivo organizado", "arquivos organizados")}"
                : $"{Ui.Plural(r.Movidos.Count, "arquivo organizado", "arquivos organizados")}, {falhas} com problema";
        TxtConclusaoDetalhe.Text = r.Movidos.Count > 0
            ? "Mudou de ideia? O botão Desfazer devolve todos ao lugar de antes."
            : "Nenhum arquivo foi movido.";
        IconeConclusao.Data = Ui.Recurso(falhas == 0 ? "IconeSucesso" : "IconeAlerta") as Geometry;

        foreach (var g in r.Movidos.GroupBy(m => m.PastaDestinoNome).OrderByDescending(g => g.Count()))
        {
            var linha = new DockPanel();
            var abrir = Ui.Botao("Abrir pasta", "link", null, (_, _) => Ui.Abrir(g.First().PastaDestinoCaminho));
            DockPanel.SetDock(abrir, Dock.Right);
            linha.Children.Add(abrir);
            linha.Children.Add(Ui.Texto($"{g.Key}: {Ui.Plural(g.Count(), "arquivo", "arquivos")}"));
            ListaConclusaoDestinos.Children.Add(linha);
        }

        if (falhas > 0)
        {
            ListaConclusaoAvisos.Children.Add(Ui.Texto("NÃO PUDERAM SER MOVIDOS", "caps"));
            foreach (var f in r.Falhas)
            {
                var caixa = new StackPanel();
                caixa.Children.Add(Ui.Texto(f.Nome));
                caixa.Children.Add(Ui.Texto(f.Motivo, "perigo"));
                var borda = new Border { Child = caixa };
                borda.Classes.Add("falha");
                ListaConclusaoAvisos.Children.Add(borda);
            }
        }
        foreach (var pasta in r.PastasCriadas)
            ListaConclusaoAvisos.Children.Add(Ui.Aviso($"A pasta \"{pasta}\" não existia e foi criada.", "", "IconePastaMais"));
        if (r.ExtensoesSemPasta.Count > 0)
            ListaConclusaoAvisos.Children.Add(Ui.Aviso(
                $"Tipos sem pasta ficaram onde estavam: {string.Join(", ", r.ExtensoesSemPasta)}. Escolha a pasta deles na tela de regras.",
                "warning", "IconeDuvida"));
        if (ListaConclusaoAvisos.Children.Count == 0)
            ListaConclusaoAvisos.Children.Add(Ui.Aviso("Tudo certo: nenhum problema e nada esperando por você.", "", "IconeSucesso"));
    }

    private async void BtnDesfazer_Click(object? sender, RoutedEventArgs e)
    {
        if (_resultado?.Cubo is not { } cubo) return;
        var resposta = Dialogo.Show(
            $"Devolver os {cubo.Arquivos} arquivo(s) para onde estavam antes?",
            "Desfazer organização", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes,
            textoConfirmar: "Desfazer", textoCancelar: "Manter organizado");
        if (resposta != MessageBoxResult.Yes) return;

        await _servicos.RestaurarAsync(cubo);
        BtnDesfazer.IsVisible = false;
        TxtConclusaoDetalhe.Text = "Os arquivos voltaram para onde estavam.";
    }

    private void BtnRelatorio_Click(object? sender, RoutedEventArgs e)
    {
        if (_resultado?.CaminhoRelatorio is { Length: > 0 } caminho && !Ui.Abrir(caminho))
            Dialogo.Show($"O relatório está em:\n{caminho}", "Relatório");
    }

    // ===================== Navegação entre etapas =====================

    private void BtnAvancar_Click(object? sender, RoutedEventArgs e)
    {
        switch (_etapa)
        {
            case Etapa.Previa when _plano is { Movimentos.Count: > 0 }:
                MostrarConfirmacao();
                break;
            case Etapa.Confirmacao:
                _ = ExecutarAsync();
                break;
            case Etapa.Conclusao:
                _janela.Navegar("inicio");
                _etapa = Etapa.Previa;
                break;
        }
    }

    private void BtnVoltar_Click(object? sender, RoutedEventArgs e)
    {
        switch (_etapa)
        {
            case Etapa.Previa:
                _janela.Navegar("inicio");
                break;
            case Etapa.Confirmacao:
                MostrarEtapa(Etapa.Previa);
                BtnAvancar.IsEnabled = _plano is { Movimentos.Count: > 0 };
                break;
            case Etapa.Execucao:
                _cancelar?.Cancel();
                BtnVoltar.IsEnabled = false;
                BtnVoltar.Content = "Parando…";
                break;
        }
    }

    private void MostrarEtapa(Etapa etapa)
    {
        _etapa = etapa;
        PainelPrevia.IsVisible = etapa == Etapa.Previa;
        PainelConfirmacao.IsVisible = etapa == Etapa.Confirmacao;
        PainelExecucao.IsVisible = etapa == Etapa.Execucao;
        PainelConclusao.IsVisible = etapa == Etapa.Conclusao;

        var etapas = new[] { Etapa1, Etapa2, Etapa3, Etapa4 };
        for (var i = 0; i < etapas.Length; i++)
        {
            etapas[i].Classes.Set("ativa", i == (int)etapa);
            etapas[i].Classes.Set("feita", i < (int)etapa);
        }

        BtnVoltar.IsEnabled = true;
        BtnAvancar.IsEnabled = true;
        BtnDesfazer.IsVisible = etapa == Etapa.Conclusao && _resultado?.Cubo is { Arquivos: > 0 };
        BtnRelatorio.IsVisible = etapa == Etapa.Conclusao && !string.IsNullOrEmpty(_resultado?.CaminhoRelatorio);
        BtnAvancar.IsVisible = etapa != Etapa.Execucao;

        (BtnVoltar.Content, BtnVoltar.Tag) = etapa switch
        {
            Etapa.Previa => ("Cancelar", Ui.Recurso("IconeX")),
            Etapa.Confirmacao => ("Voltar", Ui.Recurso("IconeSetaEsquerda")),
            Etapa.Execucao => ("Parar", Ui.Recurso("IconeParar")),
            _ => ("", null),
        };
        BtnVoltar.IsVisible = etapa != Etapa.Conclusao;

        (BtnAvancar.Content, BtnAvancar.Tag) = etapa switch
        {
            Etapa.Previa => ("Continuar", Ui.Recurso("IconeSetaDireita")),
            Etapa.Confirmacao => ("Organizar agora", Ui.Recurso("IconeOrganizar")),
            _ => ("Concluir", Ui.Recurso("IconeVisto")),
        };
    }
}
