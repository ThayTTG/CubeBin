using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Comum;
using Comum.UI;
using Organizador.Core.Models;
using Organizador.Core.Services;
// MessageBox aponta para o diálogo no tema da NokyLabs (mesma forma de chamar da MessageBox nativa).
using MessageBox = Comum.UI.Dialogo;

namespace Organizador.App;

/// <summary>
/// A janela do organizador. Veio da janela do CubeBin 2.x, só com as telas da organização:
/// Organizar (regras e pendências), Pastas, Histórico, Configurações e Sobre. Apagar arquivos,
/// sugestões de limpeza e a nuvem ficaram no CubeBin.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly OrganizadorService _organizadorService;
    private readonly InicializacaoService _inicializacaoService;
    private readonly RecallService _recallService;
    private readonly PendenciaService _pendenciaService = new();
    private readonly ClassificadorService _classificadorService = new();
    private readonly Func<Task> _executarAgora;
    private AppConfig _config;

    /// <summary>
    /// Itens do Recall marcados para devolução mas ainda não salvos (linha amarela). Ordem de
    /// marcação preservada para o "Desfazer" (desfaz só a última marcação).
    /// </summary>
    private readonly List<ItemRecallExibicao> _pilhaPendentesRecall = new();
    private bool _bloqueandoTrocaDeAba;

    // ===================== Sidebar recolhível =====================
    private const double LarguraSidebarAberta = 248;
    private const double LarguraSidebarRecolhida = 68;
    private bool _sidebarRecolhida;
    private List<TextBlock> _textosAbaSidebar = new();

    /// <summary>A página aberta agora (Tag do item do menu lateral).</summary>
    private string _paginaAtual = "organizar";

    public MainWindow()
    {
        // Só para o previewer do Avalonia.
        InitializeComponent();
        _configService = null!;
        _notificationService = null!;
        _organizadorService = null!;
        _inicializacaoService = null!;
        _recallService = null!;
        _executarAgora = () => Task.CompletedTask;
        _config = new AppConfig();
    }

    public MainWindow(
        ConfigService configService,
        INotificationService notificationService,
        OrganizadorService organizadorService,
        InicializacaoService inicializacaoService,
        RecallService recallService,
        Func<Task> executarAgora)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);
        _configService = configService;
        _notificationService = notificationService;
        _organizadorService = organizadorService;
        _inicializacaoService = inicializacaoService;
        _recallService = recallService;
        _executarAgora = executarAgora;
        _config = _configService.CarregarOuCriarPadrao();

        // Mostra a barra enquanto qualquer organização estiver rodando, seja disparada
        // manualmente ou pelo agendador automático.
        _organizadorService.ExecucaoIniciada += () => Dispatcher.UIThread.Post(() => PainelStatus.IsVisible = true);
        _organizadorService.ExecucaoFinalizada += () => Dispatcher.UIThread.Post(() =>
        {
            PainelStatus.IsVisible = false;
            CarregarPendencias();
            CarregarHistorico();

            // Não descarta marcações de devolução ainda não salvas se uma organização automática
            // rodar enquanto o usuário está revisando o Recall.
            if (_pilhaPendentesRecall.Count == 0)
                CarregarRecall();
        });

        Closing += (_, e) =>
        {
            // Fecha para a bandeja em vez de encerrar o processo (App.axaml.cs define ShutdownMode).
            // Só o "Sair" da bandeja (encerramento do app) fecha a janela de verdade.
            if (e.CloseReason != WindowCloseReason.WindowClosing || e.IsProgrammatic) return;
            e.Cancel = true;
            Hide();
        };

        LigarEventos();

        CarregarPastasNaGrid();
        CarregarRegrasNaGrid();
        CarregarConfiguracoesGerais();
        CarregarPendencias();
        CarregarHistorico();
        CarregarRecall();

        _textosAbaSidebar = new List<TextBlock>
        {
            TxtLabelOrganizar, TxtLabelPastas, TxtLabelHistorico, TxtLabelConfiguracoes, TxtLabelSobre
        };

        Title = InfoApp.Nome;
        TxtNomeApp.Text = InfoApp.Nome;
        TxtNomeAppSobre.Text = InfoApp.Nome;
        CardVersao.AppName = InfoApp.Nome;
        TxtVersaoSobre.Text = $"Versão {InfoApp.Versao}";
        CardVersao.Version = $"versão {InfoApp.Versao}";
        BotaoRecolherSidebar.Click += (_, _) => AlternarSidebarRecolhida();
    }

    /// <summary>
    /// Liga os eventos que não dá para declarar no XAML: o menu lateral, as sub-abas do Organizar,
    /// os cliques com o botão direito nas tabelas e os duplos cliques.
    /// </summary>
    private void LigarEventos()
    {
        foreach (var nav in new[] { NavOrganizar, NavPastas, NavHistorico, NavConfiguracoes, NavSobre })
        {
            nav.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true, Tag: string pagina }) TrocarDePagina(pagina);
            };
        }

        SubAbaRegras.IsCheckedChanged += (_, _) => AtualizarSubAbaOrganizar();
        SubAbaPendencias.IsCheckedChanged += (_, _) => AtualizarSubAbaOrganizar();

        // Botão direito: chega antes da seleção da tabela (Tunnel), como o PreviewMouseRightButtonDown do WPF.
        GridRegras.AddHandler(PointerPressedEvent, GridRegras_PointerPressed, RoutingStrategies.Tunnel);
        GridRecall.AddHandler(PointerPressedEvent, GridRecall_PointerPressed, RoutingStrategies.Tunnel);

        GridPendencias.DoubleTapped += GridPendencias_DoubleTapped;
        GridHistorico.DoubleTapped += GridHistorico_DoubleTapped;
    }

    private void AtualizarSubAbaOrganizar()
    {
        PainelRegras.IsVisible = SubAbaRegras.IsChecked == true;
        PainelPendencias.IsVisible = SubAbaPendencias.IsChecked == true;
    }

    private void AlternarSidebarRecolhida()
    {
        _sidebarRecolhida = !_sidebarRecolhida;

        GradeJanela.ColumnDefinitions[0].Width = new GridLength(_sidebarRecolhida ? LarguraSidebarRecolhida : LarguraSidebarAberta);
        PainelSidebar.Margin = _sidebarRecolhida ? new Thickness(8, 20, 8, 14) : new Thickness(14, 20, 14, 14);

        var visivel = !_sidebarRecolhida;
        foreach (var texto in _textosAbaSidebar)
            texto.IsVisible = visivel;

        // Recolhido: só os ícones (o nome de cada aba aparece na dica ao passar o mouse).
        PainelNomeApp.IsVisible = visivel;
        CardVersao.IsVisible = visivel;
        TxtRecolherMenu.IsVisible = visivel;
        TxtTituloMenu.IsVisible = visivel;

        if (this.FindResource(_sidebarRecolhida ? "IconePainelAbrir" : "IconePainelFechar") is Geometry icone)
            IconeRecolher.Data = icone;
        ToolTip.SetTip(BotaoRecolherSidebar, _sidebarRecolhida ? "Expandir menu" : "Recolher menu");
    }

    // ===================== SOBRE =====================

    private void BtnSobreSite_Click(object? sender, RoutedEventArgs e) => AbrirLink(InfoApp.UrlSite);

    private void BtnSobreGithub_Click(object? sender, RoutedEventArgs e) => AbrirLink(InfoApp.UrlCodigo);

    private void BtnSobreProblema_Click(object? sender, RoutedEventArgs e) => AbrirLink(InfoApp.UrlProblema);

    private static void AbrirLink(string url)
    {
        if (!InfoApp.AbrirNoNavegador(url))
        {
            MessageBox.Show($"Não deu para abrir o navegador. Copie o endereço:\n{url}", InfoApp.Nome,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Troca a página visível. Bloqueia sair da aba Histórico (que agora absorve o Recall) enquanto
    /// houver marcações de devolução pendentes de salvar — o usuário precisa salvar ou desfazer antes.
    /// </summary>
    private void TrocarDePagina(string pagina)
    {
        if (_bloqueandoTrocaDeAba || pagina == _paginaAtual) return;

        if (_paginaAtual == "historico" && _pilhaPendentesRecall.Count > 0)
        {
            // Depois que o clique atual terminar: volta a marcação do menu para o Histórico e avisa.
            Dispatcher.UIThread.Post(() =>
            {
                _bloqueandoTrocaDeAba = true;
                NavHistorico.IsChecked = true;
                _bloqueandoTrocaDeAba = false;
                AvisarESegurarNoRecall();
            });
            return;
        }

        _paginaAtual = pagina;
        var paginas = new Dictionary<string, Control>
        {
            ["organizar"] = PaginaOrganizar,
            ["pastas"] = PaginaPastas,
            ["historico"] = PaginaHistorico,
            ["configuracoes"] = PaginaConfiguracoes,
            ["sobre"] = PaginaSobre
        };

        foreach (var (chave, controle) in paginas)
            controle.IsVisible = chave == pagina;

        // A mesma entrada suave dos outros apps da NokyLabs.
        NokyLabs.UI.Anim.PlayEntrance(paginas[pagina]);
    }

    private static void AvisarESegurarNoRecall()
    {
        MessageBox.Show(
            "Há arquivos marcados para devolver ao local de origem que ainda não foram salvos.\n\n" +
            "Salve ou desfaça essas marcações antes de sair da aba Histórico.",
            "Alterações não salvas no Recall", MessageBoxButton.OK, MessageBoxImage.Warning);
    }


    // ===================== PASTAS MONITORADAS =====================

    private List<PastaExibicao> _pastasExibicao = new();

    private void CarregarPastasNaGrid()
    {
        _pastasExibicao = _config.TodasAsPastas
            .Select(p => new PastaExibicao(p, p.Removivel ? "Personalizada" : "Padrão"))
            .ToList();

        GridPastas.ItemsSource = _pastasExibicao;
        AtualizarComboContextoRegras();
    }

    private PastaExibicao? PastaSelecionada => GridPastas.SelectedItem as PastaExibicao;

    private async void BtnAdicionarPasta_Click(object? sender, RoutedEventArgs e)
    {
        var pastaEscolhida = await EscolherPastaAsync("Selecionar pasta para monitorar");
        if (pastaEscolhida is null) return;

        var nome = Path.GetFileName(pastaEscolhida.TrimEnd(Path.DirectorySeparatorChar));
        var pasta = new PastaMonitorada
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Nome = string.IsNullOrWhiteSpace(nome) ? pastaEscolhida : nome,
            Caminho = pastaEscolhida,
            Removivel = true
        };

        _configService.AdicionarPastaPersonalizada(_config, pasta);
        _configService.Salvar(_config);
        CarregarPastasNaGrid();
    }

    private void BtnRemoverPasta_Click(object? sender, RoutedEventArgs e)
    {
        var selecionada = PastaSelecionada;
        if (selecionada is null) return;

        if (!selecionada.Pasta.Removivel)
        {
            MessageBox.Show("Pastas padrão não podem ser removidas. Use \"Limpar pasta padrão\" para resetá-la.",
                "Ação não permitida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _configService.RemoverPastaPersonalizada(_config, selecionada.Pasta.Id);
            _configService.Salvar(_config);
            CarregarPastasNaGrid();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLimparPastaPadrao_Click(object? sender, RoutedEventArgs e)
    {
        var selecionada = PastaSelecionada;
        if (selecionada is null || selecionada.Pasta.Removivel) return;

        _configService.LimparPastaPadrao(_config, selecionada.Pasta.Id);
        _configService.Salvar(_config);
        CarregarPastasNaGrid();
    }

    private void BtnRestaurarPastaPadrao_Click(object? sender, RoutedEventArgs e)
    {
        var selecionada = PastaSelecionada;
        if (selecionada is null || selecionada.Pasta.Removivel) return;

        _configService.RestaurarPastaPadrao(_config, selecionada.Pasta.Id);
        _configService.Salvar(_config);
        CarregarPastasNaGrid();
    }

    private void BtnSalvarPastas_Click(object? sender, RoutedEventArgs e)
    {
        // Ativar organizar_subpastas exige confirmação explícita do risco (seção 6 da spec).
        foreach (var item in _pastasExibicao)
        {
            if (item.OrganizarSubpastas && !item.Pasta.OrganizarSubpastas)
            {
                var confirmado = _notificationService.ConfirmarRiscoSubpastas(item.Nome);
                if (!confirmado)
                {
                    item.OrganizarSubpastas = false;
                }
            }
        }

        _configService.Salvar(_config);
        CarregarPastasNaGrid();
        MessageBox.Show("Pastas salvas.", InfoApp.Nome, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Item de exibição na grid — evita expor lógica de negócio direto no XAML.</summary>
    private class PastaExibicao
    {
        public PastaMonitorada Pasta { get; }
        public string TipoExibicao { get; }

        public PastaExibicao(PastaMonitorada pasta, string tipoExibicao)
        {
            Pasta = pasta;
            TipoExibicao = tipoExibicao;
        }

        public string Nome => Pasta.Nome;
        public string Caminho => Pasta.Caminho;
        public bool OrganizarSubpastas
        {
            get => Pasta.OrganizarSubpastas;
            set => Pasta.OrganizarSubpastas = value;
        }
    }

    // ===================== REGRAS DE EXTENSÃO =====================

    private List<RegraExibicao> _regrasExibicao = new();

    /// <summary>Null = editando as regras padrão; caso contrário, editando o override de uma pasta específica.</summary>
    private PastaMonitorada? _pastaContextoRegras;

    /// <summary>Item do combo de contexto — identifica a pasta pelo Id (não pelo nome, que pode repetir entre pastas personalizadas).</summary>
    private record OpcaoContextoRegras(string Id, string Rotulo)
    {
        public override string ToString() => Rotulo;
    }

    private const string IdContextoPadrao = "__padrao__";

    private void AtualizarComboContextoRegras()
    {
        var opcaoAtual = ComboContextoRegras.SelectedItem as OpcaoContextoRegras;
        var itens = new List<OpcaoContextoRegras> { new(IdContextoPadrao, "Padrão") };
        itens.AddRange(_config.TodasAsPastas.Select(p => new OpcaoContextoRegras(p.Id, p.Nome)));
        ComboContextoRegras.ItemsSource = itens;

        var manterId = opcaoAtual?.Id ?? IdContextoPadrao;
        ComboContextoRegras.SelectedItem = itens.FirstOrDefault(i => i.Id == manterId) ?? itens[0];
    }

    private void ComboContextoRegras_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var opcao = ComboContextoRegras.SelectedItem as OpcaoContextoRegras;
        _pastaContextoRegras = opcao is null || opcao.Id == IdContextoPadrao
            ? null
            : _config.TodasAsPastas.FirstOrDefault(p => p.Id == opcao.Id);

        BtnUsarPadrao.IsEnabled = _pastaContextoRegras is not null;
        CarregarRegrasNaGrid();
    }

    private void CarregarRegrasNaGrid()
    {
        var origem = _pastaContextoRegras?.RegrasPersonalizadas is { Count: > 0 } personalizadas
            ? personalizadas
            : _config.RegrasExtensao;

        _regrasExibicao = origem.Values.Select(r => new RegraExibicao(r)).ToList();
        GridRegras.ItemsSource = _regrasExibicao;
    }

    private void BtnUsarPadrao_Click(object? sender, RoutedEventArgs e)
    {
        if (_pastaContextoRegras is null) return;
        _pastaContextoRegras.RegrasPersonalizadas = null;
        _configService.Salvar(_config);
        CarregarRegrasNaGrid();
    }

    private void BtnAdicionarRegra_Click(object? sender, RoutedEventArgs e)
    {
        var novaChave = $"nova_regra_{_regrasExibicao.Count + 1}";
        var regra = new RegraExtensao { Chave = novaChave, Extensoes = new List<string>(), Destino = "" };
        _regrasExibicao.Add(new RegraExibicao(regra));
        GridRegras.ItemsSource = null;
        GridRegras.ItemsSource = _regrasExibicao;
    }

    private void BtnRemoverRegra_Click(object? sender, RoutedEventArgs e)
    {
        if (GridRegras.SelectedItem is not RegraExibicao selecionada) return;
        _regrasExibicao.Remove(selecionada);
        GridRegras.ItemsSource = null;
        GridRegras.ItemsSource = _regrasExibicao;
    }

    private void BtnSalvarRegras_Click(object? sender, RoutedEventArgs e)
    {
        var novoDicionario = new Dictionary<string, RegraExtensao>();
        foreach (var item in _regrasExibicao)
        {
            var regra = item.ParaRegraExtensao();
            if (string.IsNullOrWhiteSpace(regra.Chave)) continue;
            novoDicionario[regra.Chave] = regra;
        }

        if (_pastaContextoRegras is null)
            _config.RegrasExtensao = novoDicionario;
        else
            _pastaContextoRegras.RegrasPersonalizadas = novoDicionario;

        _configService.Salvar(_config);
        CarregarRegrasNaGrid();
        MessageBox.Show("Regras salvas.", InfoApp.Nome, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Botão direito numa célula das Regras: seleciona a linha clicada e mostra um menu com
    /// "Editar &lt;coluna&gt;" (entra em edição daquele campo específico) e "Editar subpastas por
    /// extensão". O clique único continua só selecionando, e o duplo clique continua editando o
    /// texto normalmente — comportamento padrão da tabela, sem interferência nossa.
    /// </summary>
    private void GridRegras_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(GridRegras).Properties.IsRightButtonPressed) return;

        var origem = e.Source as Visual;
        var linha = origem?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (linha?.DataContext is not RegraExibicao regra) return;

        var celula = origem?.FindAncestorOfType<DataGridCell>(includeSelf: true);
        var coluna = celula is null ? null : GridRegras.Columns.FirstOrDefault(c =>
            c.GetCellContent(linha) is Control conteudo &&
            (ReferenceEquals(conteudo, celula.Content) || conteudo.FindAncestorOfType<DataGridCell>() == celula));

        GridRegras.SelectedItem = regra;
        var nomeColuna = coluna?.Header?.ToString() ?? "campo";

        var menu = new ContextMenu();

        var itemEditar = new MenuItem { Header = $"Editar {nomeColuna}", IsEnabled = coluna is { IsReadOnly: false } };
        itemEditar.Click += (_, _) =>
        {
            if (coluna is null) return;
            GridRegras.SelectedItem = regra;
            GridRegras.CurrentColumn = coluna;
            GridRegras.Focus();
            Dispatcher.UIThread.Post(() => GridRegras.BeginEdit(), DispatcherPriority.Input);
        };
        menu.Items.Add(itemEditar);

        var itemSubpastas = new MenuItem { Header = "Editar subpastas por extensão" };
        itemSubpastas.Click += (_, _) => AbrirEditorSubpastas();
        menu.Items.Add(itemSubpastas);

        menu.Items.Add(new Separator());

        var itemRemover = new MenuItem { Header = "Remover regra selecionada" };
        itemRemover.Click += BtnRemoverRegra_Click;
        menu.Items.Add(itemRemover);

        menu.Open(GridRegras);
        e.Handled = true;
    }

    /// <summary>
    /// Abre o diálogo de subpastas por extensão para a regra selecionada (item 1 do feedback de uso).
    /// Acessível pelo menu de botão direito (item 1.1).
    /// </summary>
    private async void AbrirEditorSubpastas()
    {
        // Se uma célula (ex: "Extensões") ainda estiver sendo editada, confirma antes de continuar.
        GridRegras.CommitEdit(DataGridEditingUnit.Cell, true);
        GridRegras.CommitEdit(DataGridEditingUnit.Row, true);

        if (GridRegras.SelectedItem is not RegraExibicao selecionada) return;

        var extensoes = selecionada.ExtensoesTexto
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (extensoes.Count == 0)
        {
            MessageBox.Show("Adicione ao menos uma extensão nesta regra antes de configurar subpastas.",
                InfoApp.Nome, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialogo = new SubpastasWindow(extensoes, selecionada.Subpastas);
        if (await dialogo.ShowDialog<bool>(this))
        {
            selecionada.Subpastas = dialogo.Resultado;
            GridRegras.ItemsSource = null;
            GridRegras.ItemsSource = _regrasExibicao;
            GridRegras.SelectedItem = selecionada;
        }
    }

    private class RegraExibicao
    {
        public string Chave { get; set; }
        public string ExtensoesTexto { get; set; }
        public string Destino { get; set; }

        /// <summary>Extensão (minúscula) -> nome da subpasta (item 1 do feedback de uso).</summary>
        public Dictionary<string, string> Subpastas { get; set; }

        public string SubpastasResumo =>
            Subpastas.Count == 0 ? "—" : string.Join(", ", Subpastas.Select(kv => $"{kv.Key}→{kv.Value}"));

        public RegraExibicao(RegraExtensao regra)
        {
            Chave = regra.Chave;
            ExtensoesTexto = string.Join(", ", regra.Extensoes);
            Destino = regra.Destino;
            Subpastas = new Dictionary<string, string>(regra.Subpastas);
        }

        public RegraExtensao ParaRegraExtensao()
        {
            var extensoes = ExtensoesTexto
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                .Distinct()
                .ToList();

            // Só mantém subpastas de extensões que ainda existem na regra (evita entradas órfãs
            // se o usuário remover uma extensão do campo de texto depois de configurar a subpasta).
            var subpastasValidas = Subpastas
                .Where(kv => extensoes.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            return new RegraExtensao
            {
                Chave = Chave.Trim(),
                Extensoes = extensoes,
                Destino = Destino.Trim(),
                Subpastas = subpastasValidas
            };
        }
    }

    // ===================== CONFIGURAÇÕES GERAIS =====================

    private void CarregarConfiguracoesGerais()
    {
        TxtExecucoesPorDia.Text = _config.ExecucoesPorDia.ToString();
        TxtHorarioInicio.Text = _config.HorarioInicio.ToString("HH:mm");
        TxtTempoAusencia.Text = _config.TempoAusenciaMinutos.ToString();
        TxtJanelaCancelamento.Text = _config.JanelaCancelamentoSegundos.ToString();
        TxtDiretorioTemporario.Text = _config.DiretorioTemporario;
        TxtDiretorioBackup.Text = _config.Backup.DiretorioCsv;
        TxtManterUltimosN.Text = _config.Backup.ManterUltimosN.ToString();
        ChkIniciarComWindows.IsChecked = _config.IniciarComWindows;
    }

    private void BtnSalvarConfigGerais_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtExecucoesPorDia.Text, out var execucoesPorDia) || execucoesPorDia <= 0 || execucoesPorDia > 48 ||
            !TimeOnly.TryParseExact((TxtHorarioInicio.Text ?? "").Trim(), "HH:mm", out var horarioInicio) ||
            !int.TryParse(TxtTempoAusencia.Text, out var ausencia) || ausencia <= 0 ||
            !int.TryParse(TxtJanelaCancelamento.Text, out var janela) || janela < 0 ||
            !int.TryParse(TxtManterUltimosN.Text, out var manterN) || manterN <= 0)
        {
            MessageBox.Show(
                "Verifique os campos: \"vezes por dia\" precisa ser um número entre 1 e 48, \"horário de início\" " +
                "precisa estar no formato HH:mm (ex: 08:00), e os demais campos numéricos devem ser maiores que zero.",
                "Valores inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.ExecucoesPorDia = execucoesPorDia;
        _config.HorarioInicio = horarioInicio;
        _config.TempoAusenciaMinutos = ausencia;
        _config.JanelaCancelamentoSegundos = janela;
        _config.DiretorioTemporario = (TxtDiretorioTemporario.Text ?? "").Trim();
        _config.Backup.DiretorioCsv = (TxtDiretorioBackup.Text ?? "").Trim();
        _config.Backup.ManterUltimosN = manterN;
        _config.IniciarComWindows = ChkIniciarComWindows.IsChecked == true;

        _configService.Salvar(_config);
        AtualizarInicializacaoComWindows();

        var horarios = string.Join(", ", BackgroundScheduler.ObterHorariosDoDia(_config).Select(h => h.ToString("HH:mm")));
        MessageBox.Show(
            $"Configurações salvas. Próximos horários agendados hoje: {horarios}.\n\n" +
            "O agendador em segundo plano já lê a configuração mais recente a cada verificação — não precisa reabrir o app.",
            InfoApp.Nome, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Aplica o valor de IniciarComWindows no registro do Windows (item 3 do feedback de uso).
    /// Observação: Environment.ProcessPath aponta para dotnet.exe quando rodando via "dotnet run" —
    /// para funcionar de verdade, use o .exe publicado/compilado diretamente.
    /// </summary>
    private void AtualizarInicializacaoComWindows()
    {
        // O registro do Windows só existe no Windows (fora dele o app roda só para testar a tela).
        if (!OperatingSystem.IsWindows()) return;

        var caminhoExecutavel = Environment.ProcessPath;
        if (string.IsNullOrEmpty(caminhoExecutavel)) return;

        if (_config.IniciarComWindows)
            _inicializacaoService.Habilitar(caminhoExecutavel);
        else
            _inicializacaoService.Desabilitar();
    }

    private async void BtnExecutarAgora_Click(object? sender, RoutedEventArgs e)
    {
        BtnExecutarAgora.IsEnabled = false;
        BtnExecutarAgora.Content = "Organizando...";

        try
        {
            await _executarAgora();
        }
        finally
        {
            BtnExecutarAgora.IsEnabled = true;
            BtnExecutarAgora.Content = "Executar organização agora";

            // A execução pode ter gerado pendências e um novo backup — atualiza as outras abas.
            CarregarPendencias();
            CarregarHistorico();
        }
    }

    // ===================== PENDÊNCIAS =====================

    private void CarregarPendencias()
    {
        var pendencias = _pendenciaService.Carregar(_config.ArquivoFilaPendencias);
        GridPendencias.ItemsSource = pendencias.Where(p => p.Status == "Pendente").ToList();
    }

    private void BtnRecarregarPendencias_Click(object? sender, RoutedEventArgs e) => CarregarPendencias();

    private void BtnIgnorarPendencia_Click(object? sender, RoutedEventArgs e)
    {
        if (GridPendencias.SelectedItem is not ArquivoPendente selecionada) return;

        _pendenciaService.AtualizarStatus(_config.ArquivoFilaPendencias, selecionada.CaminhoOriginal, "Ignorado");
        CarregarPendencias();
    }

    /// <summary>
    /// Duplo clique numa pendência: menu flutuante com as regras de extensão já cadastradas.
    /// Ao escolher uma, a extensão do arquivo passa a integrar aquela regra e o arquivo é
    /// movido do temporário para a pasta de destino correspondente.
    /// </summary>
    private void GridPendencias_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GridPendencias.SelectedItem is not ArquivoPendente pendente) return;

        var menu = new ContextMenu();

        foreach (var regra in _config.RegrasExtensao.Values.OrderBy(r => r.Destino))
        {
            // "_" num menu vira atalho de teclado: dobrado, ele aparece como está (ex.: Documentos_Office).
            var item = new MenuItem { Header = $"{regra.Destino}  ({regra.Chave})".Replace("_", "__") };
            item.Click += (_, _) => ResolverPendenciaComRegra(pendente, regra);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            MessageBox.Show(
                "Ainda não há nenhuma regra de extensão cadastrada. Crie uma na aba \"Regras de extensão\" primeiro.",
                InfoApp.Nome, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        menu.Open(GridPendencias);
    }

    private void ResolverPendenciaComRegra(ArquivoPendente pendente, RegraExtensao regra)
    {
        var pastaOrigem = _config.TodasAsPastas.FirstOrDefault(p => p.Id == pendente.PastaOrigemId);
        if (pastaOrigem is null)
        {
            MessageBox.Show("A pasta de origem original desse arquivo não existe mais na configuração.",
                "Não foi possível mover", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(pendente.CaminhoOriginal))
        {
            MessageBox.Show("O arquivo não foi encontrado (pode já ter sido movido ou removido manualmente).",
                "Não foi possível mover", MessageBoxButton.OK, MessageBoxImage.Warning);
            _pendenciaService.AtualizarStatus(_config.ArquivoFilaPendencias, pendente.CaminhoOriginal, "Resolvido");
            CarregarPendencias();
            return;
        }

        try
        {
            // A extensão passa a integrar a regra escolhida, para que arquivos futuros dela não caiam mais em pendências.
            var extensao = pendente.Extensao.ToLowerInvariant();
            if (!regra.Extensoes.Any(e => e.Equals(extensao, StringComparison.OrdinalIgnoreCase)))
                regra.Extensoes.Add(extensao);

            var pastaDestino = regra.Subpastas.TryGetValue(extensao, out var subpasta) && !string.IsNullOrWhiteSpace(subpasta)
                ? Path.Combine(pastaOrigem.Caminho, regra.Destino, subpasta)
                : Path.Combine(pastaOrigem.Caminho, regra.Destino);
            Directory.CreateDirectory(pastaDestino);

            var nomeFinal = _classificadorService.ResolverNomeDestino(pendente.NomeArquivo, pastaDestino, pastaOrigem);
            var caminhoFinal = Path.Combine(pastaDestino, nomeFinal);
            ArquivoUtils.MoverArquivo(pendente.CaminhoOriginal, caminhoFinal, sobrescrever: false);

            // A partir daqui o arquivo já foi movido com sucesso — o restante (recall, status da
            // pendência, salvar config) é bookkeeping que não deve reportar "falha" se o arquivo
            // já está no lugar certo.
            try
            {
                var caminhoOrigemVerdadeiro = Path.Combine(pastaOrigem.Caminho, pendente.NomeArquivo);
                _recallService.Adicionar(_config.ArquivoRecall, new ItemRecall
                {
                    NomeArquivo = nomeFinal,
                    CaminhoOrigem = caminhoOrigemVerdadeiro,
                    CaminhoAtual = caminhoFinal,
                    MovidoEm = DateTime.Now
                });
            }
            catch
            {
                // Falha ao registrar no recall.json não deve ser reportada como falha ao mover o arquivo.
            }

            _pendenciaService.AtualizarStatus(_config.ArquivoFilaPendencias, pendente.CaminhoOriginal, "Resolvido");
            _configService.Salvar(_config);

            CarregarRegrasNaGrid();
            CarregarPendencias();
            CarregarRecall();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível mover o arquivo: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===================== HISTÓRICO =====================

    private void CarregarHistorico()
    {
        if (!Directory.Exists(_config.Backup.DiretorioCsv))
        {
            GridHistorico.ItemsSource = Array.Empty<BackupExibicao>();
            AtualizarEstadoVazioHistorico(0);
            return;
        }

        var arquivos = new DirectoryInfo(_config.Backup.DiretorioCsv)
            .GetFiles("backup_*.csv")
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupExibicao(f.Name, f.CreationTime, (f.Length + 1023) / 1024))
            .ToList();

        GridHistorico.ItemsSource = arquivos;
        AtualizarEstadoVazioHistorico(arquivos.Count);
    }

    private void AtualizarEstadoVazioHistorico(int quantidade)
    {
        var vazio = quantidade == 0;
        GridHistorico.IsVisible = !vazio;
        PainelVazioHistorico.IsVisible = vazio;
    }

    private void BtnRecarregarHistorico_Click(object? sender, RoutedEventArgs e) => CarregarHistorico();

    /// <summary>
    /// Duplo clique num backup: tenta abrir o CSV no aplicativo padrão; se não houver um
    /// associado (ou a abertura falhar), abre o Explorer com o arquivo já selecionado.
    /// </summary>
    private void GridHistorico_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GridHistorico.SelectedItem is not BackupExibicao selecionado) return;

        var caminhoCompleto = Path.Combine(_config.Backup.DiretorioCsv, selecionado.Nome);
        if (!File.Exists(caminhoCompleto))
        {
            MessageBox.Show("Esse arquivo de backup não foi encontrado no disco.", InfoApp.Nome,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(caminhoCompleto) { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{caminhoCompleto}\"") { UseShellExecute = true });
        }
    }

    private record BackupExibicao(string Nome, DateTime CriadoEm, long TamanhoKb);

    // ===================== RECALL (absorvido pela aba Histórico) =====================

    private List<ItemRecallExibicao> _todosItensRecall = new();

    /// <summary>Quando preenchido, mostra só itens do Recall com MovidoEm próximo a esse horário —
    /// é a correlação "ver itens desta execução" a partir de um backup CSV selecionado em Histórico.
    /// Heurística por proximidade de horário (não há um vínculo direto gravado entre os dois),
    /// já que o CSV e o recall.json são gerados quase no mesmo instante pela mesma execução.</summary>
    private DateTime? _filtroExecucaoRecall;

    private void CarregarRecall()
    {
        // Qualquer recarga do disco invalida marcações em memória ainda não salvas (os objetos
        // antigos deixam de existir na nova lista) — por isso a pilha de pendentes é limpa aqui.
        _pilhaPendentesRecall.Clear();

        var todos = _recallService.Carregar(_config.ArquivoRecall)
            .OrderByDescending(i => i.MovidoEm)
            .Select(i => new ItemRecallExibicao(i))
            .ToList();

        _todosItensRecall = _filtroExecucaoRecall is DateTime marco
            ? todos.Where(i => Math.Abs((i.Item.MovidoEm - marco).TotalMinutes) <= 5).ToList()
            : todos;

        GridRecall.ItemsSource = _todosItensRecall;
        ConstruirArvoreRecall(_todosItensRecall);
        AtualizarEstadoBotoesRecall();
    }

    private void BtnRecarregarRecall_Click(object? sender, RoutedEventArgs e) => CarregarRecall();

    /// <summary>Filtra o Recall pelos itens de uma execução específica, a partir do backup CSV
    /// selecionado acima — a "revisão a partir do próprio CSV" pedida na reorganização.</summary>
    private void BtnVerItensExecucao_Click(object? sender, RoutedEventArgs e)
    {
        if (GridHistorico.SelectedItem is not BackupExibicao selecionado)
        {
            MessageBox.Show("Selecione um backup na lista acima primeiro.", InfoApp.Nome,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _filtroExecucaoRecall = selecionado.CriadoEm;
        CarregarRecall();
    }

    private void BtnLimparFiltroRecall_Click(object? sender, RoutedEventArgs e)
    {
        _filtroExecucaoRecall = null;
        CarregarRecall();
    }

    /// <summary>
    /// Monta a árvore de navegação (item 1 do feedback de uso): raiz por pasta monitorada, depois
    /// pela estrutura de subpastas de destino (regra/subpasta), com os arquivos como folhas.
    /// Clicar num nó de pasta filtra a grid; clicar num arquivo seleciona a linha correspondente.
    /// </summary>
    private void ConstruirArvoreRecall(List<ItemRecallExibicao> itens)
    {
        TreeRecall.Items.Clear();

        var raizTodos = new TreeViewItem { Header = $"Todos os arquivos ({itens.Count})", Tag = itens, IsExpanded = true };
        TreeRecall.Items.Add(raizTodos);

        var pastasConhecidas = _config.TodasAsPastas
            .OrderByDescending(p => p.Caminho.Length)
            .ToList();

        var nos = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in itens)
        {
            var pastaCorrespondente = pastasConhecidas.FirstOrDefault(p =>
                item.CaminhoAtual.StartsWith(p.Caminho + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            var nomeRaiz = pastaCorrespondente?.Nome ?? "Outros locais";
            var diretorioArquivo = Path.GetDirectoryName(item.CaminhoAtual) ?? "";
            var relativo = pastaCorrespondente is not null
                ? diretorioArquivo[Math.Min(pastaCorrespondente.Caminho.Length, diretorioArquivo.Length)..].Trim('\\')
                : "";

            var chaveAcumulada = nomeRaiz;
            if (!nos.TryGetValue(chaveAcumulada, out var noAtual))
            {
                noAtual = new TreeViewItem { Header = nomeRaiz, Tag = new List<ItemRecallExibicao>() };
                nos[chaveAcumulada] = noAtual;
                TreeRecall.Items.Add(noAtual);
            }
            ((List<ItemRecallExibicao>)noAtual.Tag!).Add(item);

            foreach (var segmento in relativo.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                chaveAcumulada += "\\" + segmento;
                if (!nos.TryGetValue(chaveAcumulada, out var noFilho))
                {
                    noFilho = new TreeViewItem { Header = segmento, Tag = new List<ItemRecallExibicao>() };
                    nos[chaveAcumulada] = noFilho;
                    noAtual.Items.Add(noFilho);
                }
                ((List<ItemRecallExibicao>)noFilho.Tag!).Add(item);
                noAtual = noFilho;
            }

            noAtual.Items.Add(new TreeViewItem { Header = item.NomeArquivo, Tag = item });
        }
    }

    private void TreeRecall_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TreeRecall.SelectedItem is not TreeViewItem no) return;

        if (no.Tag is ItemRecallExibicao itemUnico)
        {
            GridRecall.ItemsSource = _todosItensRecall;
            GridRecall.SelectedItem = itemUnico;
            GridRecall.ScrollIntoView(itemUnico, null);
        }
        else if (no.Tag is List<ItemRecallExibicao> itensDaPasta)
        {
            GridRecall.ItemsSource = itensDaPasta;
        }
    }

    /// <summary>
    /// Botão direito num item do Recall: marca/desmarca a devolução ao local de origem (linha
    /// fica amarela). A movimentação física só acontece de fato ao clicar em "Salvar".
    /// Respeita seleção múltipla (item 1 do feedback de uso): clicar com o botão direito em cima
    /// de um item que já fazia parte de uma seleção múltipla existente mantém a seleção inteira,
    /// em vez de colapsar para um item só — igual ao comportamento do Explorer do Windows.
    /// </summary>
    private void GridRecall_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(GridRecall).Properties.IsRightButtonPressed) return;

        var linha = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (linha?.DataContext is not ItemRecallExibicao itemClicado) return;

        if (!GridRecall.SelectedItems.Contains(itemClicado))
            GridRecall.SelectedItem = itemClicado;

        var selecionados = GridRecall.SelectedItems.Cast<ItemRecallExibicao>().ToList();
        var elegiveisParaMarcar = selecionados.Where(i => i.Item.Status != "Restaurado" && !i.PendenteRestauro).ToList();
        var elegiveisParaDesmarcar = selecionados.Where(i => i.PendenteRestauro).ToList();

        var menu = new ContextMenu();

        if (elegiveisParaMarcar.Count > 0)
        {
            var texto = elegiveisParaMarcar.Count == 1
                ? "Marcar para devolver ao local de origem"
                : $"Marcar {elegiveisParaMarcar.Count} arquivos para devolver ao local de origem";
            var itemMarcar = new MenuItem { Header = texto };
            itemMarcar.Click += (_, _) =>
            {
                foreach (var item in elegiveisParaMarcar) MarcarPendenteRecall(item);
            };
            menu.Items.Add(itemMarcar);
        }

        if (elegiveisParaDesmarcar.Count > 0)
        {
            var texto = elegiveisParaDesmarcar.Count == 1
                ? "Desmarcar devolução"
                : $"Desmarcar {elegiveisParaDesmarcar.Count} devoluções";
            var itemDesmarcar = new MenuItem { Header = texto };
            itemDesmarcar.Click += (_, _) =>
            {
                foreach (var item in elegiveisParaDesmarcar) DesmarcarPendenteRecall(item);
            };
            menu.Items.Add(itemDesmarcar);
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Já foi devolvido ao local de origem", IsEnabled = false });

        menu.Open(GridRecall);
        e.Handled = true;
    }

    private void MarcarPendenteRecall(ItemRecallExibicao item)
    {
        item.PendenteRestauro = true;
        if (!_pilhaPendentesRecall.Contains(item))
            _pilhaPendentesRecall.Add(item);
        AtualizarEstadoBotoesRecall();
    }

    private void DesmarcarPendenteRecall(ItemRecallExibicao item)
    {
        item.PendenteRestauro = false;
        _pilhaPendentesRecall.Remove(item);
        AtualizarEstadoBotoesRecall();
    }

    private void AtualizarEstadoBotoesRecall()
    {
        var temPendentes = _pilhaPendentesRecall.Count > 0;
        BtnSalvarRecall.IsEnabled = temPendentes;
        BtnDesfazerRecall.IsEnabled = temPendentes;
        BtnCancelarRecall.IsEnabled = temPendentes;
    }

    /// <summary>Confirma todas as devoluções marcadas: move os arquivos de fato e recarrega do disco.</summary>
    private async void BtnSalvarRecall_Click(object? sender, RoutedEventArgs e)
    {
        if (_pilhaPendentesRecall.Count == 0) return;

        BtnSalvarRecall.IsEnabled = false;

        var falhas = new List<string>();
        foreach (var item in _pilhaPendentesRecall.ToList())
        {
            try
            {
                await _recallService.RestaurarAsync(_config.ArquivoRecall, item.Item);
            }
            catch (Exception ex)
            {
                falhas.Add($"{item.NomeArquivo}: {ex.Message}");
            }
        }

        CarregarRecall(); // também limpa _pilhaPendentesRecall e reconstrói a árvore

        if (falhas.Count > 0)
        {
            MessageBox.Show(
                "Alguns arquivos não puderam ser devolvidos:\n\n" + string.Join("\n", falhas),
                "Recall", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Desfaz só a última marcação (a mais recente), como um Ctrl+Z de um passo.</summary>
    private void BtnDesfazerRecall_Click(object? sender, RoutedEventArgs e)
    {
        if (_pilhaPendentesRecall.Count == 0) return;

        var ultimo = _pilhaPendentesRecall[^1];
        _pilhaPendentesRecall.RemoveAt(_pilhaPendentesRecall.Count - 1);
        ultimo.PendenteRestauro = false;
        AtualizarEstadoBotoesRecall();
    }

    /// <summary>Descarta todas as marcações pendentes de uma vez, sem mover nenhum arquivo.</summary>
    private void BtnCancelarRecall_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in _pilhaPendentesRecall)
            item.PendenteRestauro = false;

        _pilhaPendentesRecall.Clear();
        AtualizarEstadoBotoesRecall();
    }

    /// <summary>
    /// Item de exibição do Recall — embrulha o ItemRecall com o estado de marcação em memória
    /// (ainda não salvo) e expõe a cor da linha (item 3 do feedback de uso) via notificação de
    /// mudança, para o DataTrigger do DataGrid reagir sem precisar recarregar a grid inteira.
    /// </summary>
    private class ItemRecallExibicao : INotifyPropertyChanged
    {
        public ItemRecall Item { get; }

        public ItemRecallExibicao(ItemRecall item) => Item = item;

        public string NomeArquivo => Item.NomeArquivo;
        public string CaminhoOrigem => Item.CaminhoOrigem;
        public string CaminhoAtual => Item.CaminhoAtual;
        public DateTime MovidoEm => Item.MovidoEm;

        private bool _pendenteRestauro;
        public bool PendenteRestauro
        {
            get => _pendenteRestauro;
            set
            {
                if (_pendenteRestauro == value) return;
                _pendenteRestauro = value;
                OnPropertyChanged(nameof(PendenteRestauro));
                OnPropertyChanged(nameof(CorLinha));
                OnPropertyChanged(nameof(SituacaoTexto));
            }
        }

        /// <summary>"Azul" = organizado, "Amarelo" = marcado para devolver (não salvo), "Verde" = já devolvido.</summary>
        public string CorLinha =>
            Item.Status == "Restaurado" ? "Verde" :
            PendenteRestauro ? "Amarelo" : "Azul";

        /// <summary>O mesmo estado de CorLinha, escrito: a cor nunca é a única forma de saber.</summary>
        public string SituacaoTexto =>
            Item.Status == "Restaurado" ? "Devolvido" :
            PendenteRestauro ? "Será devolvido" : "Organizado";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string nome) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
    }

    /// <summary>Escolhe uma pasta no seletor do sistema. Devolve null se o usuário cancelar.</summary>
    private async Task<string?> EscolherPastaAsync(string titulo)
    {
        var pastas = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = titulo });
        return pastas.Count > 0 ? pastas[0].TryGetLocalPath() : null;
    }
}
