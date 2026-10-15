using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using CubeBin.Core;
using CubeBin.Core.Models;
using CubeBin.Core.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using TextBlock = System.Windows.Controls.TextBlock;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TreeViewItem = System.Windows.Controls.TreeViewItem;
using TabItem = System.Windows.Controls.TabItem;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using DataGridCell = System.Windows.Controls.DataGridCell;
using DataGridRow = System.Windows.Controls.DataGridRow;
using DataGridCellInfo = System.Windows.Controls.DataGridCellInfo;
using DataGridEditingUnit = System.Windows.Controls.DataGridEditingUnit;

namespace CubeBin.App;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly OrganizadorService _organizadorService;
    private readonly InicializacaoService _inicializacaoService;
    private readonly RecallService _recallService;
    private readonly GoogleDriveService _googleDriveService;
    private readonly PendenciaService _pendenciaService = new();
    private readonly ClassificadorService _classificadorService = new();
    private readonly BackupService _backupService = new();
    private readonly AgenteSugestaoService _agenteSugestaoService = new();
    private readonly SugestaoService _sugestaoService = new();
    private readonly PacoteBackupService _pacoteBackupService = new();
    private BackupNuvemService _backupNuvemService = null!;
    private readonly Func<Task> _executarAgora;
    private AppConfig _config;

    /// <summary>
    /// Itens do Recall marcados para devolução mas ainda não salvos (linha amarela). Ordem de
    /// marcação preservada para o "Desfazer" (desfaz só a última marcação).
    /// </summary>
    private readonly List<ItemRecallExibicao> _pilhaPendentesRecall = new();
    private bool _bloqueandoTrocaDeAba;

    // ===================== Sidebar recolhível (item 3 do refinamento visual) =====================
    private ColumnDefinition? _colunaSidebar;
    private Button? _botaoRecolherSidebar;
    private bool _sidebarRecolhida;
    private List<TextBlock> _textosAbaSidebar = new();

    public MainWindow(
        ConfigService configService,
        INotificationService notificationService,
        OrganizadorService organizadorService,
        InicializacaoService inicializacaoService,
        RecallService recallService,
        GoogleDriveService googleDriveService,
        Func<Task> executarAgora)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);
        ColunaTipoDestino.ItemsSource = new[] { "Local", "Google Drive" };
        _configService = configService;
        _notificationService = notificationService;
        _organizadorService = organizadorService;
        _inicializacaoService = inicializacaoService;
        _recallService = recallService;
        _googleDriveService = googleDriveService;
        _backupNuvemService = new BackupNuvemService(_googleDriveService, _pacoteBackupService);
        _executarAgora = executarAgora;
        _config = _configService.CarregarOuCriarPadrao();

        // Indicador visual (item 2 do feedback de uso): mostra a barra enquanto qualquer
        // organização estiver rodando, seja disparada manualmente ou pelo agendador automático.
        _organizadorService.ExecucaoIniciada += () => Dispatcher.Invoke(() => PainelStatus.Visibility = Visibility.Visible);
        _googleDriveService.ProgressoAlterado += (nome, mbps) => Dispatcher.Invoke(() =>
        {
            PainelStatus.Visibility = Visibility.Visible;
            TxtStatusOrganizando.Text = $"{nome} — {mbps:F1} Mbps";
        });
        _organizadorService.ExecucaoFinalizada += () => Dispatcher.Invoke(() =>
        {
            PainelStatus.Visibility = Visibility.Collapsed;
            TxtStatusOrganizando.Text = "Organizando arquivos...";
            CarregarPendencias();
            CarregarHistorico();

            // Não descarta marcações de devolução ainda não salvas se uma organização automática
            // rodar enquanto o usuário está revisando o Recall.
            if (_pilhaPendentesRecall.Count == 0)
                CarregarRecall();
        });

        Closing += (_, e) =>
        {
            // Fecha para a bandeja em vez de encerrar o processo (App.xaml define ShutdownMode).
            e.Cancel = true;
            Hide();
        };

        CarregarPastasNaGrid();
        CarregarRegrasNaGrid();
        CarregarConfiguracoesGerais();
        CarregarPendencias();
        CarregarHistorico();
        CarregarRecall();
        CarregarConfigAgente();
        CarregarSugestoes();
        CarregarCubos();

        _textosAbaSidebar = new List<TextBlock>
        {
            TxtLabelOrganizar, TxtLabelLimpar, TxtLabelDeletar, TxtLabelSugestoes,
            TxtLabelCubos, TxtLabelHistorico, TxtLabelConfiguracoes, TxtLabelLogin
        };

        // As partes nomeadas dentro do template do TabControl (coluna da sidebar, botão de
        // recolher) só existem depois que o template é aplicado — Loaded garante isso.
        Loaded += (_, _) => InicializarSidebarRecolhivel();
    }

    /// <summary>
    /// Localiza as partes do template do TabControl principal (item 3 do refinamento visual) via
    /// FrameworkTemplate.FindName — o jeito padrão do WPF de alcançar elementos nomeados dentro de
    /// um ControlTemplate a partir do code-behind, depois que o template já foi aplicado.
    /// </summary>
    private void InicializarSidebarRecolhivel()
    {
        AbaPrincipal.ApplyTemplate();
        _colunaSidebar = AbaPrincipal.Template.FindName("ColunaSidebar", AbaPrincipal) as ColumnDefinition;
        _botaoRecolherSidebar = AbaPrincipal.Template.FindName("BotaoRecolherSidebar", AbaPrincipal) as Button;

        if (_botaoRecolherSidebar is not null)
            _botaoRecolherSidebar.Click += (_, _) => AlternarSidebarRecolhida();
    }

    private void AlternarSidebarRecolhida()
    {
        _sidebarRecolhida = !_sidebarRecolhida;

        if (_colunaSidebar is not null)
            _colunaSidebar.Width = new GridLength(_sidebarRecolhida ? 56 : 196);

        var visibilidade = _sidebarRecolhida ? Visibility.Collapsed : Visibility.Visible;
        foreach (var texto in _textosAbaSidebar)
            texto.Visibility = visibilidade;
    }

    /// <summary>
    /// Bloqueia sair da aba Histórico (que agora absorve o Recall) enquanto houver marcações de
    /// devolução pendentes de salvar — o usuário precisa salvar ou desfazer antes de continuar.
    /// </summary>
    private void AbaPrincipal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_bloqueandoTrocaDeAba) return;

        if (e.RemovedItems.Count > 0 && ReferenceEquals(e.RemovedItems[0], TabHistorico) && _pilhaPendentesRecall.Count > 0)
        {
            AvisarESegurarNoRecall();
            _bloqueandoTrocaDeAba = true;
            AbaPrincipal.SelectedItem = TabHistorico;
            _bloqueandoTrocaDeAba = false;
        }
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

    private void BtnAdicionarPasta_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Selecionar pasta para monitorar" };
        if (dialogo.ShowDialog() != true) return;

        var nome = Path.GetFileName(dialogo.FolderName.TrimEnd(Path.DirectorySeparatorChar));
        var pasta = new PastaMonitorada
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Nome = string.IsNullOrWhiteSpace(nome) ? dialogo.FolderName : nome,
            Caminho = dialogo.FolderName,
            Removivel = true
        };

        _configService.AdicionarPastaPersonalizada(_config, pasta);
        _configService.Salvar(_config);
        CarregarPastasNaGrid();
    }

    private void BtnRemoverPasta_Click(object sender, RoutedEventArgs e)
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

    private void BtnLimparPastaPadrao_Click(object sender, RoutedEventArgs e)
    {
        var selecionada = PastaSelecionada;
        if (selecionada is null || selecionada.Pasta.Removivel) return;

        _configService.LimparPastaPadrao(_config, selecionada.Pasta.Id);
        _configService.Salvar(_config);
        CarregarPastasNaGrid();
    }

    private void BtnRestaurarPastaPadrao_Click(object sender, RoutedEventArgs e)
    {
        var selecionada = PastaSelecionada;
        if (selecionada is null || selecionada.Pasta.Removivel) return;

        _configService.RestaurarPastaPadrao(_config, selecionada.Pasta.Id);
        _configService.Salvar(_config);
        CarregarPastasNaGrid();
    }

    private void BtnSalvarPastas_Click(object sender, RoutedEventArgs e)
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
        MessageBox.Show("Pastas salvas.", "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void BtnUsarPadrao_Click(object sender, RoutedEventArgs e)
    {
        if (_pastaContextoRegras is null) return;
        _pastaContextoRegras.RegrasPersonalizadas = null;
        _configService.Salvar(_config);
        CarregarRegrasNaGrid();
    }

    private void BtnAdicionarRegra_Click(object sender, RoutedEventArgs e)
    {
        var novaChave = $"nova_regra_{_regrasExibicao.Count + 1}";
        var regra = new RegraExtensao { Chave = novaChave, Extensoes = new List<string>(), Destino = "" };
        _regrasExibicao.Add(new RegraExibicao(regra));
        GridRegras.ItemsSource = null;
        GridRegras.ItemsSource = _regrasExibicao;
    }

    private void BtnRemoverRegra_Click(object sender, RoutedEventArgs e)
    {
        if (GridRegras.SelectedItem is not RegraExibicao selecionada) return;
        _regrasExibicao.Remove(selecionada);
        GridRegras.ItemsSource = null;
        GridRegras.ItemsSource = _regrasExibicao;
    }

    private void BtnSalvarRegras_Click(object sender, RoutedEventArgs e)
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
        MessageBox.Show("Regras salvas.", "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Botão direito numa célula das Regras: seleciona a célula clicada e mostra um menu com
    /// "Editar &lt;coluna&gt;" (entra em edição daquele campo específico) e "Editar subpastas por
    /// extensão". O clique único continua só selecionando, e o duplo clique continua editando o
    /// texto normalmente — comportamento padrão do DataGrid do WPF, sem interferência nossa.
    /// </summary>
    private void GridRegras_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var celula = EncontrarAncestral<DataGridCell>(e.OriginalSource as DependencyObject);
        if (celula is null) return;

        var linha = EncontrarAncestral<DataGridRow>(celula);
        if (linha?.Item is not RegraExibicao regra) return;

        GridRegras.SelectedItem = regra;
        var coluna = celula.Column;
        var nomeColuna = coluna?.Header?.ToString() ?? "campo";

        var menu = new ContextMenu { PlacementTarget = GridRegras };

        var itemEditar = new MenuItem { Header = $"Editar {nomeColuna}" };
        itemEditar.Click += (_, _) =>
        {
            if (coluna is null) return;
            GridRegras.CurrentCell = new DataGridCellInfo(regra, coluna);
            GridRegras.BeginEdit();
        };
        menu.Items.Add(itemEditar);

        var itemSubpastas = new MenuItem { Header = "Editar subpastas por extensão" };
        itemSubpastas.Click += (_, _) => AbrirEditorSubpastas();
        menu.Items.Add(itemSubpastas);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var itemRemover = new MenuItem { Header = "Remover regra selecionada" };
        itemRemover.Click += BtnRemoverRegra_Click;
        menu.Items.Add(itemRemover);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static T? EncontrarAncestral<T>(DependencyObject? atual) where T : DependencyObject
    {
        while (atual is not null && atual is not T)
            atual = VisualTreeHelper.GetParent(atual);
        return atual as T;
    }

    /// <summary>
    /// Abre o diálogo de subpastas por extensão para a regra selecionada (item 1 do feedback de uso).
    /// Acessível por duplo clique na linha ou pelo menu de botão direito (item 1.1).
    /// </summary>
    private void AbrirEditorSubpastas()
    {
        // Se uma célula (ex: "Extensões") ainda estiver sendo editada, o WPF não permite
        // Items.Refresh() com a transação de edição aberta — confirma antes de continuar.
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
                "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialogo = new SubpastasWindow(extensoes, selecionada.Subpastas) { Owner = this };
        if (dialogo.ShowDialog() == true)
        {
            selecionada.Subpastas = dialogo.Resultado;
            GridRegras.CommitEdit(DataGridEditingUnit.Row, true);
            GridRegras.Items.Refresh();
        }
    }

    private class RegraExibicao
    {
        public string Chave { get; set; }
        public string ExtensoesTexto { get; set; }
        public string Destino { get; set; }

        /// <summary>"Local" ou "Google Drive" — controla se Destino é uma pasta local ou CaminhoDrive é usado.</summary>
        public string TipoTexto { get; set; }

        /// <summary>Caminho de pastas dentro do Google Drive, só relevante quando TipoTexto = "Google Drive".</summary>
        public string CaminhoDrive { get; set; }

        /// <summary>Extensão (minúscula) -> nome da subpasta (item 1 do feedback de uso).</summary>
        public Dictionary<string, string> Subpastas { get; set; }

        public string SubpastasResumo =>
            Subpastas.Count == 0 ? "—" : string.Join(", ", Subpastas.Select(kv => $"{kv.Key}→{kv.Value}"));

        public RegraExibicao(RegraExtensao regra)
        {
            Chave = regra.Chave;
            ExtensoesTexto = string.Join(", ", regra.Extensoes);
            Destino = regra.Destino;
            TipoTexto = regra.TipoDestino == TipoDestino.GoogleDrive ? "Google Drive" : "Local";
            CaminhoDrive = regra.CaminhoDrive ?? string.Empty;
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
                TipoDestino = TipoTexto == "Google Drive" ? TipoDestino.GoogleDrive : TipoDestino.Local,
                CaminhoDrive = string.IsNullOrWhiteSpace(CaminhoDrive) ? null : CaminhoDrive.Trim(),
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

        AtualizarStatusGoogleDrive();
    }

    private void BtnSalvarConfigGerais_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtExecucoesPorDia.Text, out var execucoesPorDia) || execucoesPorDia <= 0 || execucoesPorDia > 48 ||
            !TimeOnly.TryParseExact(TxtHorarioInicio.Text.Trim(), "HH:mm", out var horarioInicio) ||
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
        _config.DiretorioTemporario = TxtDiretorioTemporario.Text.Trim();
        _config.Backup.DiretorioCsv = TxtDiretorioBackup.Text.Trim();
        _config.Backup.ManterUltimosN = manterN;
        _config.IniciarComWindows = ChkIniciarComWindows.IsChecked == true;

        _configService.Salvar(_config);
        AtualizarInicializacaoComWindows();

        var horarios = string.Join(", ", BackgroundScheduler.ObterHorariosDoDia(_config).Select(h => h.ToString("HH:mm")));
        MessageBox.Show(
            $"Configurações salvas. Próximos horários agendados hoje: {horarios}.\n\n" +
            "O agendador em segundo plano já lê a configuração mais recente a cada verificação — não precisa reabrir o app.",
            "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Aplica o valor de IniciarComWindows no registro do Windows (item 3 do feedback de uso).
    /// Observação: Environment.ProcessPath aponta para dotnet.exe quando rodando via "dotnet run" —
    /// para funcionar de verdade, use o .exe publicado/compilado diretamente.
    /// </summary>
    private void AtualizarInicializacaoComWindows()
    {
        var caminhoExecutavel = Environment.ProcessPath;
        if (string.IsNullOrEmpty(caminhoExecutavel)) return;

        if (_config.IniciarComWindows)
            _inicializacaoService.Habilitar(caminhoExecutavel);
        else
            _inicializacaoService.Desabilitar();
    }

    private async void AtualizarStatusGoogleDrive()
    {
        var conectado = _googleDriveService.EstaConectado();

        if (!conectado)
        {
            TxtLoginStatus.Text = "Não conectado";
            TxtLoginStatus.Visibility = Visibility.Visible;
            ChipContaConectada.Visibility = Visibility.Collapsed;
            TxtLoginArmazenamento.Text = "";
            return;
        }

        // Conectado: o chip substitui o texto simples "Conectado", dando um indicativo visual mais
        // claro de conta ativa (item 2 do refinamento visual).
        TxtLoginStatus.Visibility = Visibility.Collapsed;

        try
        {
            var info = await _googleDriveService.ObterInfoContaAsync();
            TxtChipEmail.Text = info.Email;
            ChipContaConectada.Visibility = Visibility.Visible;
            TxtLoginArmazenamento.Text = info.TotalBytes is > 0
                ? $"Armazenamento: {info.UsadoBytes / 1_073_741_824.0:F1} GB usados de {info.TotalBytes / 1_073_741_824.0:F1} GB"
                : $"Armazenamento usado: {info.UsadoBytes / 1_073_741_824.0:F1} GB";
        }
        catch
        {
            // Conectado, mas não deu pra buscar os dados da conta agora (ex.: sem rede no momento) —
            // mostra o chip mesmo assim, só sem o e-mail, em vez de não indicar nada.
            TxtChipEmail.Text = "Conectado";
            ChipContaConectada.Visibility = Visibility.Visible;
            TxtLoginArmazenamento.Text = "";
        }
    }

    /// <summary>
    /// Faz login no Google usando as credenciais embutidas do app (abre o navegador só na
    /// primeira vez — depois disso o token salvo se renova sozinho, sem precisar logar de novo).
    /// </summary>
    private async void BtnConectarGoogleDrive_Click(object sender, RoutedEventArgs e)
    {
        if (!GoogleDriveCredentials.EstaConfigurado)
        {
            MessageBox.Show(
                "As credenciais do Google Drive ainda não foram configuradas neste build do app " +
                "(GoogleDriveCredentials.cs). Veja o README para o passo a passo.",
                "Google Drive", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnLoginConectar.IsEnabled = false;
        TxtLoginStatus.Text = "Conectando... (verifique se uma aba abriu no navegador)";

        try
        {
            await _googleDriveService.ConectarAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível conectar ao Google Drive: {ex.Message}", "Google Drive",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AtualizarStatusGoogleDrive();
            BtnLoginConectar.IsEnabled = true;
        }
    }

    private async void BtnExecutarAgora_Click(object sender, RoutedEventArgs e)
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

    private void BtnRecarregarPendencias_Click(object sender, RoutedEventArgs e) => CarregarPendencias();

    private void BtnIgnorarPendencia_Click(object sender, RoutedEventArgs e)
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
    private void GridPendencias_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridPendencias.SelectedItem is not ArquivoPendente pendente) return;

        var menu = new ContextMenu { PlacementTarget = GridPendencias };

        foreach (var regra in _config.RegrasExtensao.Values.OrderBy(r => r.Destino))
        {
            var item = new MenuItem { Header = $"{regra.Destino}  ({regra.Chave})" };
            item.Click += (_, _) => ResolverPendenciaComRegra(pendente, regra);
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            MessageBox.Show(
                "Ainda não há nenhuma regra de extensão cadastrada. Crie uma na aba \"Regras de extensão\" primeiro.",
                "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        menu.IsOpen = true;
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
        GridHistorico.Visibility = vazio ? Visibility.Collapsed : Visibility.Visible;
        PainelVazioHistorico.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnRecarregarHistorico_Click(object sender, RoutedEventArgs e) => CarregarHistorico();

    /// <summary>
    /// Duplo clique num backup: tenta abrir o CSV no aplicativo padrão; se não houver um
    /// associado (ou a abertura falhar), abre o Explorer com o arquivo já selecionado.
    /// </summary>
    private void GridHistorico_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridHistorico.SelectedItem is not BackupExibicao selecionado) return;

        var caminhoCompleto = Path.Combine(_config.Backup.DiretorioCsv, selecionado.Nome);
        if (!File.Exists(caminhoCompleto))
        {
            MessageBox.Show("Esse arquivo de backup não foi encontrado no disco.", "CubeBin",
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

    private void BtnRecarregarRecall_Click(object sender, RoutedEventArgs e) => CarregarRecall();

    /// <summary>Filtra o Recall pelos itens de uma execução específica, a partir do backup CSV
    /// selecionado acima — a "revisão a partir do próprio CSV" pedida na reorganização.</summary>
    private void BtnVerItensExecucao_Click(object sender, RoutedEventArgs e)
    {
        if (GridHistorico.SelectedItem is not BackupExibicao selecionado)
        {
            MessageBox.Show("Selecione um backup na lista acima primeiro.", "CubeBin",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _filtroExecucaoRecall = selecionado.CriadoEm;
        CarregarRecall();
    }

    private void BtnLimparFiltroRecall_Click(object sender, RoutedEventArgs e)
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

    private void TreeRecall_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem no) return;

        if (no.Tag is ItemRecallExibicao itemUnico)
        {
            GridRecall.ItemsSource = _todosItensRecall;
            GridRecall.SelectedItem = itemUnico;
            GridRecall.ScrollIntoView(itemUnico);
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
    private void GridRecall_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var linha = EncontrarAncestral<DataGridRow>(e.OriginalSource as DependencyObject);
        if (linha?.Item is not ItemRecallExibicao itemClicado) return;

        if (!GridRecall.SelectedItems.Contains(itemClicado))
            GridRecall.SelectedItem = itemClicado;

        var selecionados = GridRecall.SelectedItems.Cast<ItemRecallExibicao>().ToList();
        var elegiveisParaMarcar = selecionados.Where(i => i.Item.Status != "Restaurado" && !i.PendenteRestauro).ToList();
        var elegiveisParaDesmarcar = selecionados.Where(i => i.PendenteRestauro).ToList();

        var menu = new ContextMenu { PlacementTarget = GridRecall };

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

        menu.IsOpen = true;
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
    private async void BtnSalvarRecall_Click(object sender, RoutedEventArgs e)
    {
        if (_pilhaPendentesRecall.Count == 0) return;

        BtnSalvarRecall.IsEnabled = false;

        var falhas = new List<string>();
        foreach (var item in _pilhaPendentesRecall.ToList())
        {
            try
            {
                await _recallService.RestaurarAsync(_config.ArquivoRecall, item.Item, _googleDriveService);
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
    private void BtnDesfazerRecall_Click(object sender, RoutedEventArgs e)
    {
        if (_pilhaPendentesRecall.Count == 0) return;

        var ultimo = _pilhaPendentesRecall[^1];
        _pilhaPendentesRecall.RemoveAt(_pilhaPendentesRecall.Count - 1);
        ultimo.PendenteRestauro = false;
        AtualizarEstadoBotoesRecall();
    }

    /// <summary>Descarta todas as marcações pendentes de uma vez, sem mover nenhum arquivo.</summary>
    private void BtnCancelarRecall_Click(object sender, RoutedEventArgs e)
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
            }
        }

        /// <summary>"Azul" = organizado, "Amarelo" = marcado para devolver (não salvo), "Verde" = já devolvido.</summary>
        public string CorLinha =>
            Item.Status == "Restaurado" ? "Verde" :
            PendenteRestauro ? "Amarelo" : "Azul";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string nome) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
    }

    // ===================== DELETAR =====================

    private readonly List<ItemParaExcluir> _itensParaExcluir = new();

    private void AtualizarGridDeletar()
    {
        GridDeletar.ItemsSource = null;
        GridDeletar.ItemsSource = _itensParaExcluir;
        TxtResumoDeletar.Text = _itensParaExcluir.Count == 0
            ? "Nenhum item selecionado."
            : $"{_itensParaExcluir.Count} item(ns) selecionado(s) para exclusão definitiva.";
        BtnExcluirDefinitivamente.IsEnabled = _itensParaExcluir.Count > 0;
        BtnDeletarEnviarNuvem.IsEnabled = _itensParaExcluir.Count > 0;

        var vazio = _itensParaExcluir.Count == 0;
        GridDeletar.Visibility = vazio ? Visibility.Collapsed : Visibility.Visible;
        PainelVazioDeletar.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Zona de arraste real (item 2 do refinamento visual): arrastar arquivos/pastas do Explorer
    /// pra cima do estado vazio já adiciona à lista, sem precisar passar pelos botões.
    /// </summary>
    private void PainelVazioDeletar_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            RetanguloTracejadoDeletar.Stroke = (System.Windows.Media.Brush)FindResource("AccentBrush");
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void PainelVazioDeletar_DragLeave(object sender, DragEventArgs e)
    {
        RetanguloTracejadoDeletar.Stroke = (System.Windows.Media.Brush)FindResource("BorderStrongBrush");
    }

    private void PainelVazioDeletar_Drop(object sender, DragEventArgs e)
    {
        RetanguloTracejadoDeletar.Stroke = (System.Windows.Media.Brush)FindResource("BorderStrongBrush");

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var caminhos = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var caminho in caminhos)
        {
            if (_itensParaExcluir.Any(i => i.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase))) continue;

            if (File.Exists(caminho))
            {
                _itensParaExcluir.Add(new ItemParaExcluir(Path.GetFileName(caminho), caminho, "Arquivo"));
            }
            else if (Directory.Exists(caminho))
            {
                var caminhoLimpo = caminho.TrimEnd(Path.DirectorySeparatorChar);
                var nome = Path.GetFileName(caminhoLimpo);
                _itensParaExcluir.Add(new ItemParaExcluir(string.IsNullOrWhiteSpace(nome) ? caminho : nome, caminho, "Pasta"));
            }
        }

        AtualizarGridDeletar();
    }

    private void BtnDeletarAdicionarArquivos_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Title = "Selecionar arquivos para excluir", Multiselect = true };
        if (dialogo.ShowDialog() != true) return;

        foreach (var caminho in dialogo.FileNames)
        {
            if (_itensParaExcluir.Any(i => i.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase))) continue;
            _itensParaExcluir.Add(new ItemParaExcluir(Path.GetFileName(caminho), caminho, "Arquivo"));
        }

        AtualizarGridDeletar();
    }

    private void BtnDeletarAdicionarPasta_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Selecionar pasta para excluir" };
        if (dialogo.ShowDialog() != true) return;

        var caminho = dialogo.FolderName.TrimEnd(Path.DirectorySeparatorChar);
        if (_itensParaExcluir.Any(i => i.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase)))
        {
            AtualizarGridDeletar();
            return;
        }

        var nome = Path.GetFileName(caminho);
        _itensParaExcluir.Add(new ItemParaExcluir(string.IsNullOrWhiteSpace(nome) ? caminho : nome, caminho, "Pasta"));
        AtualizarGridDeletar();
    }

    private void BtnDeletarRemoverDaLista_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in GridDeletar.SelectedItems.Cast<ItemParaExcluir>().ToList())
            _itensParaExcluir.Remove(item);

        AtualizarGridDeletar();
    }

    /// <summary>
    /// Exclusão manual e definitiva (módulo novo da releitura "Camada 1"). Diferente de
    /// Organizar/Limpar, isto NUNCA roda automaticamente — é sempre um clique explícito do
    /// usuário, dado que é irreversível e não passa pelo Recall.
    /// </summary>
    private void BtnExcluirDefinitivamente_Click(object sender, RoutedEventArgs e)
    {
        if (_itensParaExcluir.Count == 0) return;

        var confirmar = MessageBox.Show(
            $"Você está prestes a excluir definitivamente {_itensParaExcluir.Count} item(ns).\n\n" +
            "Essa ação NÃO pode ser desfeita pelo CubeBin — não passa pelo Recall. Um backup em CSV " +
            "do que será apagado é gerado antes, mas ele é só um registro em texto, não uma cópia " +
            "dos arquivos.\n\nConfirma a exclusão definitiva?",
            "Confirmar exclusão definitiva", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes) return;

        try
        {
            GerarBackupAntesDeExcluir();
        }
        catch (Exception ex)
        {
            var continuar = MessageBox.Show(
                $"Não foi possível gerar o backup em CSV antes da exclusão: {ex.Message}\n\n" +
                "Excluir mesmo assim, sem esse registro?",
                "Falha ao gerar backup", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);

            if (continuar != MessageBoxResult.Yes) return;
        }

        var excluidos = 0;
        var falhas = new List<string>();

        foreach (var item in _itensParaExcluir.ToList())
        {
            try
            {
                if (item.Tipo == "Pasta")
                    Directory.Delete(item.Caminho, recursive: true);
                else
                    File.Delete(item.Caminho);

                excluidos++;
                _itensParaExcluir.Remove(item);
            }
            catch (Exception ex)
            {
                falhas.Add($"{item.Nome}: {ex.Message}");
            }
        }

        AtualizarGridDeletar();

        _notificationService.NotificarMensagemSimples(
            "CubeBin — Exclusão concluída",
            falhas.Count == 0
                ? $"{excluidos} item(ns) excluído(s) definitivamente."
                : $"{excluidos} excluído(s), {falhas.Count} falharam.");

        if (falhas.Count > 0)
        {
            MessageBox.Show("Alguns itens não puderam ser excluídos:\n\n" + string.Join("\n", falhas),
                "Exclusão parcial", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        CarregarHistorico();
    }

    /// <summary>Gera o backup CSV (seção 4 da spec) do que será apagado, antes de excluir de verdade —
    /// mesma exigência de "backup antes de qualquer operação" que já vale para Organizar.</summary>
    private void GerarBackupAntesDeExcluir()
    {
        var arquivos = new List<FileInfo>();
        var pastaOrigemPorCaminho = new Dictionary<string, string>();

        foreach (var item in _itensParaExcluir)
        {
            if (item.Tipo == "Arquivo")
            {
                var arquivo = new FileInfo(item.Caminho);
                if (!arquivo.Exists) continue;
                arquivos.Add(arquivo);
                pastaOrigemPorCaminho[arquivo.FullName] = "Deletar (arquivo selecionado)";
            }
            else
            {
                if (!Directory.Exists(item.Caminho)) continue;
                foreach (var arquivo in new DirectoryInfo(item.Caminho).GetFiles("*", SearchOption.AllDirectories))
                {
                    arquivos.Add(arquivo);
                    pastaOrigemPorCaminho[arquivo.FullName] = $"Deletar (dentro de {item.Nome})";
                }
            }
        }

        _backupService.GerarBackupCsv(arquivos, pastaOrigemPorCaminho, _config.Backup);
    }

    private record ItemParaExcluir(string Nome, string Caminho, string Tipo);

    /// <summary>
    /// Envia arquivos para um pacote de backup na nuvem e só apaga localmente após confirmação de
    /// sucesso do upload (Camada 3). Compartilhado entre Deletar e Sugestões para não duplicar o
    /// fluxo de confirmação/erro/relatório em dois lugares.
    /// </summary>
    private async Task EnviarArquivosParaNuvemEExcluirAsync(List<string> caminhosArquivos, Action aposSucessoLocal)
    {
        if (caminhosArquivos.Count == 0)
        {
            MessageBox.Show("Nenhum arquivo válido encontrado para enviar.", "CubeBin",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_googleDriveService.EstaConectado())
        {
            MessageBox.Show("Conecte-se ao Google Drive na aba Login antes de enviar para a nuvem.",
                "Não conectado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmar = MessageBox.Show(
            $"Enviar {caminhosArquivos.Count} arquivo(s) para o Google Drive num pacote de backup, e só " +
            "então excluir localmente?\n\nSe o envio falhar ou for interrompido, nenhum arquivo local será apagado.",
            "Confirmar backup e exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes) return;

        var resultado = await _backupNuvemService.EnviarEDeletarAsync(
            caminhosArquivos, _config.ArquivoPacotesBackup, _config.CaminhoDriveBackups);

        if (!resultado.Sucesso)
        {
            MessageBox.Show(
                $"O envio para a nuvem falhou: {resultado.MensagemErro}\n\n" +
                "Nenhum arquivo local foi apagado. Você pode tentar novamente clicando no mesmo botão.",
                "Falha no envio", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        aposSucessoLocal();
        CarregarHistorico();
        CarregarCubos();

        var relatorio = $"Pacote enviado: {resultado.Pacote!.NomeArquivoZip}\n" +
                         $"{resultado.Deletados.Count} arquivo(s) enviado(s) e removido(s) localmente.";

        if (resultado.FalhasAoDeletar.Count > 0)
        {
            relatorio += $"\n\n{resultado.FalhasAoDeletar.Count} arquivo(s) já estão salvos na nuvem, mas não " +
                         "puderam ser removidos localmente agora:\n" + string.Join("\n", resultado.FalhasAoDeletar);
        }

        MessageBox.Show(relatorio, "Backup na nuvem concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        _notificationService.NotificarMensagemSimples(
            "CubeBin — Backup na nuvem concluído",
            $"{resultado.Deletados.Count} arquivo(s) enviados e removidos localmente.");
    }

    private async void BtnDeletarEnviarNuvem_Click(object sender, RoutedEventArgs e)
    {
        if (_itensParaExcluir.Count == 0) return;

        var caminhosArquivos = new List<string>();
        foreach (var item in _itensParaExcluir)
        {
            if (item.Tipo == "Arquivo")
            {
                if (File.Exists(item.Caminho)) caminhosArquivos.Add(item.Caminho);
            }
            else if (Directory.Exists(item.Caminho))
            {
                caminhosArquivos.AddRange(Directory.GetFiles(item.Caminho, "*", SearchOption.AllDirectories));
            }
        }

        BtnDeletarEnviarNuvem.IsEnabled = false;
        BtnDeletarEnviarNuvem.Content = "Enviando...";

        try
        {
            await EnviarArquivosParaNuvemEExcluirAsync(caminhosArquivos, () =>
            {
                _itensParaExcluir.Clear();
                AtualizarGridDeletar();
            });
        }
        finally
        {
            BtnDeletarEnviarNuvem.IsEnabled = _itensParaExcluir.Count > 0;
            BtnDeletarEnviarNuvem.Content = "Enviar à nuvem e excluir";
        }
    }

    // ===================== SUGESTÕES (Camada 2 — agente) =====================

    private List<SugestaoExibicao> _sugestoesExibicao = new();

    private void CarregarConfigAgente()
    {
        ChkAgenteAtivo.IsChecked = _config.AgenteAtivo;
        TxtAgenteDiasTemporario.Text = _config.AgenteDiasTemporarioNaoUsado.ToString();
        TxtAgenteDiasGrande.Text = _config.AgenteDiasArquivoGrandeParado.ToString();
        TxtAgenteTamanhoMinimoMb.Text = (_config.AgenteTamanhoMinimoGrandeBytes / 1_048_576).ToString();
        TxtAgenteExecucoesPorDia.Text = _config.AgenteExecucoesPorDia.ToString();
    }

    private void BtnSalvarConfigAgente_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtAgenteDiasTemporario.Text, out var diasTemp) || diasTemp <= 0 ||
            !int.TryParse(TxtAgenteDiasGrande.Text, out var diasGrande) || diasGrande <= 0 ||
            !int.TryParse(TxtAgenteTamanhoMinimoMb.Text, out var tamanhoMb) || tamanhoMb <= 0 ||
            !int.TryParse(TxtAgenteExecucoesPorDia.Text, out var execucoes) || execucoes <= 0 || execucoes > 48)
        {
            MessageBox.Show(
                "Verifique os campos numéricos do agente: todos devem ser números maiores que zero " +
                "(vezes por dia até 48).",
                "Valores inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.AgenteAtivo = ChkAgenteAtivo.IsChecked == true;
        _config.AgenteDiasTemporarioNaoUsado = diasTemp;
        _config.AgenteDiasArquivoGrandeParado = diasGrande;
        _config.AgenteTamanhoMinimoGrandeBytes = (long)tamanhoMb * 1_048_576;
        _config.AgenteExecucoesPorDia = execucoes;

        _configService.Salvar(_config);
        MessageBox.Show("Configurações do agente salvas.", "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CarregarSugestoes()
    {
        var itens = _sugestaoService.Carregar(_config.ArquivoSugestoes)
            .Where(s => s.Status is "Pendente" or "Aprovado")
            .OrderByDescending(s => s.TamanhoBytes)
            .Select(s => new SugestaoExibicao(s))
            .ToList();

        _sugestoesExibicao = itens;
        GridSugestoes.ItemsSource = itens;

        var totalBytes = itens.Sum(i => i.Sugestao.TamanhoBytes);
        TxtResumoSugestoes.Text = itens.Count == 0
            ? "Nenhuma sugestão pendente."
            : $"{itens.Count} sugestão(ões) — até {FormatarBytes.Formatar(totalBytes)} podem ser liberados.";

        BtnExcluirAprovadas.IsEnabled = itens.Any(i => i.Sugestao.Status == "Aprovado");
        BtnSugestoesEnviarNuvem.IsEnabled = BtnExcluirAprovadas.IsEnabled;

        var vazio = itens.Count == 0;
        GridSugestoes.Visibility = vazio ? Visibility.Collapsed : Visibility.Visible;
        PainelVazioSugestoes.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Chamado pelo App (via AgenteScheduler.AnaliseFinalizada) quando uma análise em
    /// segundo plano termina — só recarrega a grid se a janela já estiver aberta.</summary>
    public void AtualizarSugestoesAposAnaliseExterna() => CarregarSugestoes();

    private void BtnRecarregarSugestoes_Click(object sender, RoutedEventArgs e) => CarregarSugestoes();

    private async void BtnAnalisarAgora_Click(object sender, RoutedEventArgs e)
    {
        BtnAnalisarAgora.IsEnabled = false;
        BtnAnalisarAgora.Content = "Analisando...";
        BtnAnalisarAgoraVazio.IsEnabled = false;
        BtnAnalisarAgoraVazio.Content = "Analisando...";

        try
        {
            var novas = await Task.Run(() => _agenteSugestaoService.Analisar(_config));
            _sugestaoService.MesclarNovasSugestoes(_config.ArquivoSugestoes, novas);
            CarregarSugestoes();

            MessageBox.Show(
                novas.Count == 0 ? "Nenhuma sugestão nova encontrada." : $"{novas.Count} sugestão(ões) nova(s) encontrada(s).",
                "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnAnalisarAgora.IsEnabled = true;
            BtnAnalisarAgora.Content = "Analisar agora";
            BtnAnalisarAgoraVazio.IsEnabled = true;
            BtnAnalisarAgoraVazio.Content = "Analisar agora";
        }
    }

    private void BtnAprovarSugestoes_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in GridSugestoes.SelectedItems.Cast<SugestaoExibicao>().ToList())
            _sugestaoService.AtualizarStatus(_config.ArquivoSugestoes, item.Sugestao.Id, "Aprovado");

        CarregarSugestoes();
    }

    private void BtnIgnorarSugestoes_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in GridSugestoes.SelectedItems.Cast<SugestaoExibicao>().ToList())
            _sugestaoService.AtualizarStatus(_config.ArquivoSugestoes, item.Sugestao.Id, "Ignorado");

        CarregarSugestoes();
    }

    /// <summary>Exclusão definitiva dos itens aprovados — mesma filosofia do módulo Deletar:
    /// confirmação explícita, backup CSV antes, nunca automático.</summary>
    private void BtnExcluirAprovadas_Click(object sender, RoutedEventArgs e)
    {
        var aprovadas = _sugestoesExibicao.Where(i => i.Sugestao.Status == "Aprovado").ToList();
        if (aprovadas.Count == 0) return;

        var totalBytes = aprovadas.Sum(i => i.Sugestao.TamanhoBytes);
        var confirmar = MessageBox.Show(
            $"Excluir definitivamente {aprovadas.Count} arquivo(s) aprovado(s), liberando " +
            $"{FormatarBytes.Formatar(totalBytes)}?\n\n" +
            "Essa ação não pode ser desfeita pelo CubeBin — não passa pelo Recall. Um backup em CSV " +
            "do que será apagado é gerado antes.",
            "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirmar != MessageBoxResult.Yes) return;

        try
        {
            var arquivosParaBackup = aprovadas
                .Select(a => new FileInfo(a.Sugestao.Caminho))
                .Where(f => f.Exists)
                .ToList();
            var pastaOrigemPorCaminho = arquivosParaBackup.ToDictionary(f => f.FullName, _ => "Sugestão do agente");
            _backupService.GerarBackupCsv(arquivosParaBackup, pastaOrigemPorCaminho, _config.Backup);
        }
        catch (Exception ex)
        {
            var continuar = MessageBox.Show(
                $"Não foi possível gerar o backup em CSV antes da exclusão: {ex.Message}\n\n" +
                "Excluir mesmo assim, sem esse registro?",
                "Falha ao gerar backup", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No);

            if (continuar != MessageBoxResult.Yes) return;
        }

        var excluidas = 0;
        var falhas = new List<string>();

        foreach (var item in aprovadas)
        {
            try
            {
                if (File.Exists(item.Sugestao.Caminho))
                    File.Delete(item.Sugestao.Caminho);

                _sugestaoService.AtualizarStatus(_config.ArquivoSugestoes, item.Sugestao.Id, "Excluido");
                excluidas++;
            }
            catch (Exception ex)
            {
                falhas.Add($"{item.Nome}: {ex.Message}");
            }
        }

        CarregarSugestoes();
        CarregarHistorico();

        _notificationService.NotificarMensagemSimples(
            "CubeBin — Limpeza concluída",
            falhas.Count == 0
                ? $"{excluidas} arquivo(s) excluído(s), {FormatarBytes.Formatar(totalBytes)} liberados."
                : $"{excluidas} excluído(s), {falhas.Count} falharam.");

        if (falhas.Count > 0)
        {
            MessageBox.Show("Alguns arquivos não puderam ser excluídos:\n\n" + string.Join("\n", falhas),
                "Exclusão parcial", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private class SugestaoExibicao
    {
        public SugestaoLimpeza Sugestao { get; }

        public SugestaoExibicao(SugestaoLimpeza sugestao) => Sugestao = sugestao;

        public string Nome => Sugestao.Nome;
        public string Motivo => Sugestao.Motivo;
        public string Status => Sugestao.Status;
        public string TamanhoTexto => FormatarBytes.Formatar(Sugestao.TamanhoBytes);

        public string TipoTexto => Sugestao.Tipo switch
        {
            TipoSugestao.Duplicado => "Duplicado",
            TipoSugestao.TemporarioNaoUsado => "Temporário/cache",
            TipoSugestao.ArquivoGrandeParado => "Arquivo grande parado",
            _ => Sugestao.Tipo.ToString()
        };
    }

    private async void BtnSugestoesEnviarNuvem_Click(object sender, RoutedEventArgs e)
    {
        var aprovadas = _sugestoesExibicao.Where(i => i.Sugestao.Status == "Aprovado").ToList();
        if (aprovadas.Count == 0) return;

        var caminhosArquivos = aprovadas.Select(a => a.Sugestao.Caminho).Where(File.Exists).ToList();

        BtnSugestoesEnviarNuvem.IsEnabled = false;
        BtnSugestoesEnviarNuvem.Content = "Enviando...";

        try
        {
            await EnviarArquivosParaNuvemEExcluirAsync(caminhosArquivos, () =>
            {
                foreach (var item in aprovadas)
                    _sugestaoService.AtualizarStatus(_config.ArquivoSugestoes, item.Sugestao.Id, "Excluido");
                CarregarSugestoes();
            });
        }
        finally
        {
            BtnSugestoesEnviarNuvem.IsEnabled = _sugestoesExibicao.Any(i => i.Sugestao.Status == "Aprovado");
            BtnSugestoesEnviarNuvem.Content = "Enviar aprovadas à nuvem";
        }
    }

    // ===================== CUBOS (Camada 4) =====================

    private void CarregarCubos()
    {
        var pacotes = _pacoteBackupService.Carregar(_config.ArquivoPacotesBackup)
            .OrderByDescending(p => p.EnviadoEm)
            .ToList();

        var maiorTamanho = pacotes.Count > 0 ? pacotes.Max(p => p.TamanhoBytes) : 0;

        ListaCubos.ItemsSource = pacotes.Select(p => new CuboExibicao(p, maiorTamanho)).ToList();

        var vazio = pacotes.Count == 0;
        ScrollCubos.Visibility = vazio ? Visibility.Collapsed : Visibility.Visible;
        PainelVazioCubos.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnRecarregarCubos_Click(object sender, RoutedEventArgs e) => CarregarCubos();

    private void Cubo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement elemento || elemento.DataContext is not CuboExibicao cubo) return;

        var janela = new DetalhesPacoteWindow(cubo.Pacote, _googleDriveService) { Owner = this };
        janela.ShowDialog();
    }

    private class CuboExibicao
    {
        public PacoteBackup Pacote { get; }
        public string NomeArquivoZip => Pacote.NomeArquivoZip;
        public string TamanhoTexto => FormatarBytes.Formatar(Pacote.TamanhoBytes);

        /// <summary>Opacidade proporcional ao tamanho do pacote em relação ao maior já enviado —
        /// pacotes grandes ficam mais sólidos, pequenos mais translúcidos (Camada 4).</summary>
        public double OpacidadeCubo { get; }

        public string ResumoCategorias { get; }

        public CuboExibicao(PacoteBackup pacote, long maiorTamanho)
        {
            Pacote = pacote;

            var proporcao = maiorTamanho > 0 ? (double)pacote.TamanhoBytes / maiorTamanho : 1.0;
            OpacidadeCubo = 0.35 + proporcao * 0.65; // nunca totalmente invisível, nunca "estoura" acima de 1.0

            var porCategoria = pacote.Itens
                .GroupBy(i => i.Categoria)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            ResumoCategorias = porCategoria.Count == 0
                ? "Pacote vazio"
                : string.Join("\n", porCategoria);
        }
    }
}
