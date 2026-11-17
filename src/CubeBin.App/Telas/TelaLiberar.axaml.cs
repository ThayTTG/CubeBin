using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core.Configuracao;
using CubeBin.Core.LiberarEspaco;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Telas;

/// <summary>O módulo Liberar Espaço: Escolher → Para onde vão? → Execução → Concluído.</summary>
public partial class TelaLiberar : UserControl, ITela
{
    private enum Etapa { Escolher, Destino, Execucao, Concluido }

    private readonly ServicosApp _servicos;
    private readonly MainWindow _janela;
    private Etapa _etapa = Etapa.Escolher;
    private readonly List<string> _escolhidos = new();
    private PastaConfig? _pastaSelecionada;
    private SelecaoLiberar? _selecao;
    private DestinoLiberar _destino = DestinoLiberar.Lixeira;
    private ResultadoLiberar? _resultado;
    private CancellationTokenSource? _cancelar;

    public TelaLiberar() : this(App.Servicos!, null!) { }

    public TelaLiberar(ServicosApp servicos, MainWindow janela)
    {
        _servicos = servicos;
        _janela = janela;
        InitializeComponent();

        ZonaSoltar.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            ZonaSoltar.Classes.Set("alvo", true);
        });
        ZonaSoltar.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ZonaSoltar.Classes.Set("alvo", false));
        ZonaSoltar.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            ZonaSoltar.Classes.Set("alvo", false);
            var arquivos = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? Enumerable.Empty<string>();
            AdicionarEscolhidos(arquivos);
        });
    }

    public void AoEntrar(object? parametro)
    {
        if (_etapa == Etapa.Execucao) return;
        if (parametro is IEnumerable<string> caminhos)
        {
            // Vindo das sugestões do agente: já chega com os arquivos escolhidos.
            _escolhidos.Clear();
            AdicionarEscolhidos(caminhos);
            MostrarEtapa(Etapa.Escolher);
            return;
        }
        if (_etapa is Etapa.Concluido or Etapa.Destino) _escolhidos.Clear();
        MostrarEtapa(Etapa.Escolher);
        _ = PreencherPastasAsync();
        AtualizarEscolhidos();
        AoMostrarEscolher?.Invoke(this);
    }

    /// <summary>Chamado ao mostrar a etapa Escolher (o agente da versão 2.0 usa para mostrar sugestões).</summary>
    public static Action<TelaLiberar>? AoMostrarEscolher { get; set; }

    /// <summary>O cartão das sugestões do agente (preenchido pela versão 2.0).</summary>
    public Border CartaoDeSugestoes => CartaoSugestoes;

    // ===================== 1. Escolher =====================

    private async Task PreencherPastasAsync()
    {
        var config = _servicos.Config.Carregar();
        ListaPastas.Children.Clear();
        _pastaSelecionada = null;
        BtnEsvaziar.IsEnabled = false;

        foreach (var pasta in config.Pastas)
        {
            var info = Ui.Texto("calculando…", "pequeno");
            var linha = new DockPanel();
            var icone = Ui.IconeDeBloco(Ui.IconeDaPasta(pasta), 36);
            icone.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(icone, Dock.Left);
            linha.Children.Add(icone);
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock { Text = pasta.Nome, FontWeight = FontWeight.SemiBold });
            textos.Children.Add(info);
            linha.Children.Add(textos);

            var bloco = new Border { Child = linha, Padding = new Thickness(12, 10) };
            bloco.Classes.Add("bloco");
            bloco.Classes.Add("clicavel");
            bloco.PointerReleased += (_, _) =>
            {
                foreach (var outro in ListaPastas.Children.OfType<Border>()) outro.Classes.Set("selecionado", false);
                bloco.Classes.Set("selecionado", true);
                _pastaSelecionada = pasta;
                BtnEsvaziar.IsEnabled = true;
                BtnEsvaziar.Content = $"Esvaziar {pasta.Nome}";
            };
            ListaPastas.Children.Add(bloco);

            var caminho = pasta.CaminhoEfetivo();
            var (arquivos, bytes) = await Task.Run(() => Medir(caminho));
            info.Text = arquivos < 0 ? "não encontrada" : $"{Ui.Plural(arquivos, "arquivo", "arquivos")} · {FormatarBytes.Formatar(bytes)}";
        }
    }

    private static (int Arquivos, long Bytes) Medir(string caminho)
    {
        if (!Directory.Exists(caminho)) return (-1, 0);
        var arquivos = 0;
        long bytes = 0;
        try
        {
            foreach (var a in new DirectoryInfo(caminho).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                arquivos++;
                bytes += a.Length;
                if (arquivos > 200_000) break; // pasta enorme: o número aproximado basta
            }
        }
        catch { /* conta o que deu */ }
        return (arquivos, bytes);
    }

    private async void BtnEscolherArquivos_Click(object? sender, RoutedEventArgs e)
    {
        var topo = TopLevel.GetTopLevel(this);
        if (topo is null) return;
        var arquivos = await topo.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolher arquivos para liberar espaço",
            AllowMultiple = true,
        });
        AdicionarEscolhidos(arquivos.Select(a => a.TryGetLocalPath()).OfType<string>());
    }

    private async void BtnEscolherPasta_Click(object? sender, RoutedEventArgs e)
    {
        var topo = TopLevel.GetTopLevel(this);
        if (topo is null) return;
        var pastas = await topo.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Escolher uma pasta para liberar espaço",
            AllowMultiple = true,
        });
        AdicionarEscolhidos(pastas.Select(a => a.TryGetLocalPath()).OfType<string>());
    }

    private void AdicionarEscolhidos(IEnumerable<string> caminhos)
    {
        foreach (var c in caminhos)
            if (!_escolhidos.Contains(c)) _escolhidos.Add(c);
        AtualizarEscolhidos();
    }

    private void AtualizarEscolhidos()
    {
        ListaEscolhidos.Children.Clear();
        foreach (var caminho in _escolhidos)
        {
            var linha = new DockPanel();
            var remover = Ui.Botao("", "icon", null, (_, _) =>
            {
                _escolhidos.Remove(caminho);
                AtualizarEscolhidos();
            });
            remover.Content = Ui.Icone("IconeX", 14);
            remover.Width = 30;
            remover.Height = 30;
            ToolTip.SetTip(remover, "Tirar da lista");
            DockPanel.SetDock(remover, Dock.Right);
            linha.Children.Add(remover);
            var icone = Ui.Icone(Directory.Exists(caminho) ? "IconePasta" : "IconeDocumento", 16);
            icone.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icone, Dock.Left);
            linha.Children.Add(icone);
            linha.Children.Add(new TextBlock { Text = Path.GetFileName(caminho), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var borda = new Border { Child = linha };
            borda.Classes.Add("linha");
            ToolTip.SetTip(borda, caminho);
            ListaEscolhidos.Children.Add(borda);
        }
        BtnContinuarArquivos.IsEnabled = _escolhidos.Count > 0;
        BtnContinuarArquivos.Content = _escolhidos.Count == 0
            ? "Continuar com os escolhidos"
            : $"Continuar com {Ui.Plural(_escolhidos.Count, "item", "itens")}";
    }

    private async void BtnEsvaziar_Click(object? sender, RoutedEventArgs e)
    {
        if (_pastaSelecionada is not { } pasta) return;
        var config = _servicos.Config.Carregar();
        _selecao = await Task.Run(() => Liberador.SelecionarConteudoDaPasta(pasta, config));
        MostrarDestino();
    }

    private async void BtnContinuarArquivos_Click(object? sender, RoutedEventArgs e)
    {
        var config = _servicos.Config.Carregar();
        var caminhos = _escolhidos.ToList();
        var dados = _servicos.Ambiente.PastaDados;
        _selecao = await Task.Run(() => Liberador.SelecionarArquivos(caminhos, config, dados));
        MostrarDestino();
    }

    // ===================== 2. Para onde vão? =====================

    private void MostrarDestino()
    {
        var s = _selecao!;
        TxtSelecao.Text = s.Itens.Count == 0
            ? $"{s.Descricao}: nada para liberar"
            : $"{s.Descricao} · {Ui.Plural(s.Arquivos, "arquivo", "arquivos")} · {FormatarBytes.Formatar(s.Bytes)}";

        ListaAvisosSelecao.Children.Clear();
        foreach (var (caminho, motivo) in s.Recusados)
            ListaAvisosSelecao.Children.Add(Ui.Aviso($"\"{Path.GetFileName(caminho)}\" ficou de fora: {motivo}.", "warning", "IconeEscudo"));
        foreach (var projeto in s.PastasDeProjeto)
            ListaAvisosSelecao.Children.Add(Ui.Aviso($"\"{Path.GetFileName(projeto)}\" parece um projeto (Construct, Unity, código…) e ficou de fora.", "", "IconeEscudo"));
        if (s.Pulados.Count > 0)
            ListaAvisosSelecao.Children.Add(Ui.Aviso($"{Ui.Plural(s.Pulados.Count, "arquivo aberto ficou", "arquivos abertos ficaram")} de fora.", "", "IconeInfo"));

        var conectada = _servicos.NuvemConectada;
        BlocoNuvem.Classes.Set("apagado", !conectada);
        AvisoSemConta.IsVisible = !conectada;
        TxtContaNuvem.Text = conectada ? $"Conta: {_servicos.Config.Carregar().Nuvem.ContaConectada}" : "";
        _destino = conectada ? DestinoLiberar.Nuvem : DestinoLiberar.Lixeira;
        if (_servicos.Lixeira is null && _destino == DestinoLiberar.Lixeira) _destino = DestinoLiberar.ApagarDeVez;
        BlocoLixeira.Classes.Set("apagado", _servicos.Lixeira is null);

        MostrarEtapa(Etapa.Destino);
        AtualizarDestino();
    }

    private void AtualizarDestino()
    {
        BlocoNuvem.Classes.Set("destaque", _destino == DestinoLiberar.Nuvem);
        BlocoNuvem.Classes.Set("selecionado", false);
        BlocoLixeira.Classes.Set("selecionado", _destino == DestinoLiberar.Lixeira);
        BlocoApagar.Classes.Set("selecionado", _destino == DestinoLiberar.ApagarDeVez);

        BtnAvancar.Classes.Set("primary", _destino != DestinoLiberar.ApagarDeVez);
        BtnAvancar.Classes.Set("danger", _destino == DestinoLiberar.ApagarDeVez);
        (BtnAvancar.Content, BtnAvancar.Tag) = _destino switch
        {
            DestinoLiberar.Nuvem => ("Enviar para a nuvem", Ui.Recurso("IconeEnviarNuvem")),
            DestinoLiberar.Lixeira => ("Enviar para a Lixeira", Ui.Recurso("IconeLixeira")),
            _ => ("Apagar de vez", Ui.Recurso("IconeArquivoX")),
        };
        BtnAvancar.IsEnabled = _selecao is { Itens.Count: > 0 };
    }

    private void BlocoNuvem_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (!_servicos.NuvemConectada) return;
        _destino = DestinoLiberar.Nuvem;
        AtualizarDestino();
    }

    private void BlocoLixeira_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (_servicos.Lixeira is null) return;
        _destino = DestinoLiberar.Lixeira;
        AtualizarDestino();
    }

    private void BlocoApagar_Click(object? sender, PointerReleasedEventArgs e)
    {
        _destino = DestinoLiberar.ApagarDeVez;
        AtualizarDestino();
    }

    private void BtnConectar_Click(object? sender, RoutedEventArgs e) => _janela.Navegar("configuracoes", "nuvem");

    // ===================== 3. Execução =====================

    private async Task ExecutarAsync()
    {
        var s = _selecao!;
        if (_destino == DestinoLiberar.ApagarDeVez)
        {
            var resposta = Dialogo.Show(
                $"Apagar de vez {Ui.Plural(s.Arquivos, "arquivo", "arquivos")} ({FormatarBytes.Formatar(s.Bytes)})?\n\n" +
                "Eles não vão para a Lixeira e não dá para desfazer.",
                "Apagar de vez", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
                textoConfirmar: "Apagar de vez", textoCancelar: "Cancelar", perigo: true);
            if (resposta != MessageBoxResult.Yes) return;
        }

        if (!_servicos.TentarOcupar())
        {
            Dialogo.Show("O CubeBin está mexendo em arquivos agora. Tente de novo em instantes.", "Liberar Espaço");
            return;
        }

        MostrarEtapa(Etapa.Execucao);
        TxtExecucaoTitulo.Text = _destino switch
        {
            DestinoLiberar.Nuvem => "Enviando para a nuvem…",
            DestinoLiberar.Lixeira => "Enviando para a Lixeira…",
            _ => "Apagando…",
        };
        BarraExecucao.Maximum = Math.Max(1, s.Itens.Count);
        _cancelar = new CancellationTokenSource();
        var progresso = new Progress<Progresso>(p =>
        {
            BarraExecucao.Maximum = Math.Max(1, p.Total);
            BarraExecucao.Value = p.Feitos;
            TxtExecucaoContagem.Text = $"{p.Feitos} de {p.Total}";
            TxtExecucaoArquivo.Text = p.ArquivoAtual;
        });

        try
        {
            var destino = _destino;
            var token = _cancelar.Token;
            _resultado = destino == DestinoLiberar.Nuvem
                ? await _servicos.EnviarParaNuvemAsync(s, progresso, token)
                : await Task.Run(() => _servicos.NovoLiberador().Executar(s, destino, progresso, token));
        }
        catch (Exception ex)
        {
            App.RegistrarCrash(ex);
            Dialogo.Show($"Algo deu errado: {ex.Message}\n\nO que já saiu está anotado no registro e aparece nos Cubos.",
                "Liberar Espaço", MessageBoxButton.OK, MessageBoxImage.Error);
            _resultado = null;
        }
        finally
        {
            _servicos.Liberar();
            _cancelar = null;
        }
        MostrarConcluido();
    }

    // ===================== 4. Concluído =====================

    private void MostrarConcluido()
    {
        MostrarEtapa(Etapa.Concluido);
        ListaConcluidoAvisos.Children.Clear();
        var r = _resultado;
        if (r is null)
        {
            TxtLiberado.Text = "—";
            TxtConcluidoTitulo.Text = "Não terminou";
            TxtConcluidoDetalhe.Text = "Confira a tela de Cubos.";
            return;
        }

        if (r.MotivoAdiamento is { } motivo)
        {
            TxtLiberado.Text = "Envio adiado";
            TxtConcluidoTitulo.Text = "Nada saiu do computador";
            TxtConcluidoDetalhe.Text = $"{motivo}. Seus arquivos continuam onde estavam; o CubeBin tenta de novo quando você pedir.";
            IconeConcluido.Data = Ui.Recurso("IconeAlerta") as Geometry;
            return;
        }

        TxtLiberado.Text = $"{FormatarBytes.Formatar(r.BytesLiberados)} liberados";
        var quantos = Ui.Plural(r.Feitos.Sum(i => i.Arquivos), "arquivo", "arquivos");
        TxtConcluidoTitulo.Text = r.Destino switch
        {
            DestinoLiberar.Nuvem => $"{quantos} guardados na nuvem",
            DestinoLiberar.Lixeira => $"{quantos} na Lixeira",
            _ => $"{quantos} apagados de vez",
        };
        TxtConcluidoDetalhe.Text = r.Destino switch
        {
            DestinoLiberar.Nuvem => "O Cubo está na pasta CubeBin do seu Google Drive. Para trazer de volta, use Restaurar.",
            DestinoLiberar.Lixeira => "Mudou de ideia? Restaurar devolve tudo ao lugar (enquanto a Lixeira não for esvaziada).",
            _ => "Esses arquivos não podem ser restaurados.",
        };
        IconeConcluido.Data = Ui.Recurso(r.Falhas.Count == 0 ? "IconeSucesso" : "IconeAlerta") as Geometry;

        if (r.Falhas.Count > 0)
        {
            ListaConcluidoAvisos.Children.Add(Ui.Texto("NÃO FOI POSSÍVEL", "caps"));
            foreach (var f in r.Falhas)
            {
                var caixa = new StackPanel();
                caixa.Children.Add(Ui.Texto(f.Nome));
                caixa.Children.Add(Ui.Texto(f.Motivo, "perigo"));
                var borda = new Border { Child = caixa };
                borda.Classes.Add("falha");
                ListaConcluidoAvisos.Children.Add(borda);
            }
        }
        if (r.Cancelado)
            ListaConcluidoAvisos.Children.Add(Ui.Aviso("Interrompido no meio: o que faltava continua no computador.", "warning", "IconeAlerta"));
        if (ListaConcluidoAvisos.Children.Count == 0)
            ListaConcluidoAvisos.Children.Add(Ui.Aviso("Tudo certo, sem nenhum problema.", "", "IconeSucesso"));
    }

    private async void BtnRestaurar_Click(object? sender, RoutedEventArgs e)
    {
        if (_resultado?.Cubo is not { } cubo) return;
        var resposta = Dialogo.Show("Devolver tudo para onde estava?", "Restaurar",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, textoConfirmar: "Restaurar", textoCancelar: "Cancelar");
        if (resposta != MessageBoxResult.Yes) return;
        await _servicos.RestaurarAsync(cubo);
        BtnRestaurar.IsVisible = false;
    }

    private void BtnRelatorio_Click(object? sender, RoutedEventArgs e)
    {
        if (_resultado?.CaminhoRelatorio is { Length: > 0 } caminho && !Ui.Abrir(caminho))
            Dialogo.Show($"O relatório está em:\n{caminho}", "Relatório");
    }

    // ===================== Navegação =====================

    private void BtnAvancar_Click(object? sender, RoutedEventArgs e)
    {
        switch (_etapa)
        {
            case Etapa.Destino:
                _ = ExecutarAsync();
                break;
            case Etapa.Concluido:
                _escolhidos.Clear();
                _janela.Navegar("inicio");
                _etapa = Etapa.Escolher;
                break;
        }
    }

    private void BtnVoltar_Click(object? sender, RoutedEventArgs e)
    {
        switch (_etapa)
        {
            case Etapa.Escolher:
                _janela.Navegar("inicio");
                break;
            case Etapa.Destino:
                MostrarEtapa(Etapa.Escolher);
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
        PainelEscolher.IsVisible = etapa == Etapa.Escolher;
        PainelDestino.IsVisible = etapa == Etapa.Destino;
        PainelExecucao.IsVisible = etapa == Etapa.Execucao;
        PainelConcluido.IsVisible = etapa == Etapa.Concluido;

        var etapas = new[] { Etapa1, Etapa2, Etapa3, Etapa4 };
        for (var i = 0; i < etapas.Length; i++)
        {
            etapas[i].Classes.Set("ativa", i == (int)etapa);
            etapas[i].Classes.Set("feita", i < (int)etapa);
        }

        BtnConectar.IsVisible = etapa == Etapa.Destino && !_servicos.NuvemConectada;
        BtnRestaurar.IsVisible = etapa == Etapa.Concluido && _resultado?.Cubo is { PodeRestaurar: true, Arquivos: > 0 };
        BtnRelatorio.IsVisible = etapa == Etapa.Concluido && !string.IsNullOrEmpty(_resultado?.CaminhoRelatorio);
        BtnVoltar.IsVisible = etapa != Etapa.Concluido;
        BtnVoltar.IsEnabled = true;
        BtnAvancar.IsVisible = etapa is Etapa.Destino or Etapa.Concluido;
        BtnAvancar.Classes.Set("danger", false);
        BtnAvancar.Classes.Set("primary", true);
        BtnAvancar.IsEnabled = true;

        (BtnVoltar.Content, BtnVoltar.Tag) = etapa switch
        {
            Etapa.Escolher => ("Cancelar", Ui.Recurso("IconeX")),
            Etapa.Execucao => ("Parar", Ui.Recurso("IconeParar")),
            _ => ("Voltar", Ui.Recurso("IconeSetaEsquerda")),
        };
        if (etapa == Etapa.Concluido) (BtnAvancar.Content, BtnAvancar.Tag) = ("Concluir", Ui.Recurso("IconeVisto"));
    }
}
