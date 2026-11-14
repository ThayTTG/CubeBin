using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Comum;
using Comum.UI;
using CubeBin.Core;
using CubeBin.Core.Models;
using CubeBin.Core.Services;
// MessageBox aponta para o diálogo no tema da NokyLabs (mesma forma de chamar da MessageBox nativa).
using MessageBox = Comum.UI.Dialogo;

namespace CubeBin.App;

/// <summary>
/// A janela do CubeBin a partir da versão 3: Sugestões, Deletar, Cubos, Ajustes, Login e Sobre.
/// A organização de arquivos (regras, pastas, pendências, histórico) virou um app à parte.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly InicializacaoService _inicializacaoService;
    private readonly GoogleDriveService _googleDriveService;
    private readonly BackupService _backupService = new();
    private readonly AgenteSugestaoService _agenteSugestaoService = new();
    private readonly SugestaoService _sugestaoService = new();
    private readonly PacoteBackupService _pacoteBackupService = new();
    private BackupNuvemService _backupNuvemService = null!;
    private AppConfig _config;

    // ===================== Sidebar recolhível =====================
    private const double LarguraSidebarAberta = 248;
    private const double LarguraSidebarRecolhida = 68;
    private bool _sidebarRecolhida;
    private List<TextBlock> _textosAbaSidebar = new();

    /// <summary>A página aberta agora (Tag do item do menu lateral).</summary>
    private string _paginaAtual = "sugestoes";

    public MainWindow()
    {
        // Só para o previewer do Avalonia.
        InitializeComponent();
        _configService = null!;
        _notificationService = null!;
        _inicializacaoService = null!;
        _googleDriveService = null!;
        _config = new AppConfig();
    }

    public MainWindow(
        ConfigService configService,
        INotificationService notificationService,
        InicializacaoService inicializacaoService,
        GoogleDriveService googleDriveService)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);
        _configService = configService;
        _notificationService = notificationService;
        _inicializacaoService = inicializacaoService;
        _googleDriveService = googleDriveService;
        _backupNuvemService = new BackupNuvemService(_googleDriveService, _pacoteBackupService);
        _config = _configService.CarregarOuCriarPadrao();

        // A barra de baixo mostra o arquivo sendo enviado e a velocidade, durante um envio à nuvem.
        _googleDriveService.ProgressoAlterado += (nome, mbps) => Dispatcher.UIThread.Post(() =>
        {
            PainelStatus.IsVisible = true;
            TxtStatus.Text = $"{nome} — {mbps:F1} Mbps";
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

        CarregarConfigAgente();
        CarregarSugestoes();
        CarregarCubos();
        CarregarAjustes();
        AtualizarStatusGoogleDrive();
        AtualizarGridDeletar(); // já começa mostrando a zona de arraste vazia

        _textosAbaSidebar = new List<TextBlock>
        {
            TxtLabelSugestoes, TxtLabelDeletar, TxtLabelCubos, TxtLabelAjustes, TxtLabelLogin, TxtLabelSobre
        };

        TxtVersaoSobre.Text = $"Versão {InfoApp.Versao}";
        CardVersao.Version = $"versão {InfoApp.Versao}";
        BotaoRecolherSidebar.Click += (_, _) => AlternarSidebarRecolhida();
    }

    /// <summary>
    /// Liga os eventos que não dá para declarar no XAML: o menu lateral, o clique nos cubos e a
    /// zona de arraste do Deletar.
    /// </summary>
    private void LigarEventos()
    {
        foreach (var nav in new[] { NavSugestoes, NavDeletar, NavCubos, NavAjustes, NavLogin, NavSobre })
        {
            nav.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true, Tag: string pagina }) TrocarDePagina(pagina);
            };
        }

        ListaCubos.AddHandler(PointerReleasedEvent, Cubo_PointerReleased, RoutingStrategies.Bubble);

        PainelVazioDeletar.AddHandler(DragDrop.DragEnterEvent, PainelVazioDeletar_DragEnter);
        PainelVazioDeletar.AddHandler(DragDrop.DragOverEvent, PainelVazioDeletar_DragEnter);
        PainelVazioDeletar.AddHandler(DragDrop.DragLeaveEvent, PainelVazioDeletar_DragLeave);
        PainelVazioDeletar.AddHandler(DragDrop.DropEvent, PainelVazioDeletar_Drop);
    }

    /// <summary>Usado pelo App quando a pessoa pede "Procurar o que limpar" pela bandeja.</summary>
    public void IrParaSugestoes() => NavSugestoes.IsChecked = true;

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

    private void TrocarDePagina(string pagina)
    {
        if (pagina == _paginaAtual) return;

        _paginaAtual = pagina;
        var paginas = new Dictionary<string, Control>
        {
            ["sugestoes"] = PaginaSugestoes,
            ["deletar"] = PaginaDeletar,
            ["cubos"] = PaginaCubos,
            ["ajustes"] = PaginaAjustes,
            ["login"] = PaginaLogin,
            ["sobre"] = PaginaSobre
        };

        foreach (var (chave, controle) in paginas)
            controle.IsVisible = chave == pagina;

        // A mesma entrada suave dos outros apps da NokyLabs.
        NokyLabs.UI.Anim.PlayEntrance(paginas[pagina]);
    }

    // ===================== SOBRE =====================

    private void BtnSobreSite_Click(object? sender, RoutedEventArgs e) => AbrirLink(InfoApp.UrlSite);

    private void BtnSobreGithub_Click(object? sender, RoutedEventArgs e) => AbrirLink(InfoApp.UrlCodigo);

    private void BtnSobreProblema_Click(object? sender, RoutedEventArgs e) => AbrirLink(InfoApp.UrlProblema);

    private static void AbrirLink(string url)
    {
        if (!InfoApp.AbrirNoNavegador(url))
        {
            MessageBox.Show($"Não deu para abrir o navegador. Copie o endereço:\n{url}", "CubeBin",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ===================== AJUSTES =====================

    private void CarregarAjustes()
    {
        CarregarPastasAnalisadas();
        TxtTempoAusencia.Text = _config.TempoAusenciaMinutos.ToString();
        ChkIniciarComWindows.IsChecked = _config.IniciarComWindows;
        TxtDiretorioRegistros.Text = _config.Backup.DiretorioCsv;
        TxtManterRegistros.Text = _config.Backup.ManterUltimosN.ToString();
    }

    private void CarregarPastasAnalisadas()
    {
        GridPastasAnalisadas.ItemsSource = null;
        GridPastasAnalisadas.ItemsSource = _config.PastasAnalisadas.ToList();

        var vazio = _config.PastasAnalisadas.Count == 0;
        GridPastasAnalisadas.IsVisible = !vazio;
        TxtSemPastasAnalisadas.IsVisible = vazio;
    }

    private async void BtnAdicionarPastaAnalisada_Click(object? sender, RoutedEventArgs e)
    {
        var pastaEscolhida = await EscolherPastaAsync("Escolher uma pasta para o agente analisar");
        if (pastaEscolhida is null) return;

        _configService.AdicionarPasta(_config, pastaEscolhida);
        _configService.Salvar(_config);
        CarregarPastasAnalisadas();
    }

    private void BtnRemoverPastaAnalisada_Click(object? sender, RoutedEventArgs e)
    {
        if (GridPastasAnalisadas.SelectedItem is not PastaAnalisada selecionada) return;

        _configService.RemoverPasta(_config, selecionada.Id);
        _configService.Salvar(_config);
        CarregarPastasAnalisadas();
    }

    private void BtnSalvarAjustes_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtTempoAusencia.Text, out var ausencia) || ausencia <= 0 ||
            !int.TryParse(TxtManterRegistros.Text, out var manterN) || manterN <= 0 ||
            string.IsNullOrWhiteSpace(TxtDiretorioRegistros.Text))
        {
            MessageBox.Show(
                "Verifique os campos: o tempo longe do computador e a quantidade de registros precisam ser " +
                "números maiores que zero, e a pasta dos registros não pode ficar vazia.",
                "Valores inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.TempoAusenciaMinutos = ausencia;
        _config.IniciarComWindows = ChkIniciarComWindows.IsChecked == true;
        _config.Backup.DiretorioCsv = TxtDiretorioRegistros.Text.Trim();
        _config.Backup.ManterUltimosN = manterN;

        _configService.Salvar(_config);
        AtualizarInicializacaoComWindows();
        MessageBox.Show("Ajustes salvos.", "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnAbrirRegistros_Click(object? sender, RoutedEventArgs e)
    {
        var pasta = _config.Backup.DiretorioCsv;
        try
        {
            Directory.CreateDirectory(pasta);
            Process.Start(new ProcessStartInfo(pasta) { UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show($"Não deu para abrir a pasta. Ela fica em:\n{pasta}", "CubeBin",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Aplica o valor de IniciarComWindows no registro do Windows. Observação: Environment.ProcessPath
    /// aponta para dotnet.exe quando rodando via "dotnet run" — para funcionar de verdade, use o .exe
    /// publicado/compilado diretamente.
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

    // ===================== LOGIN (Google Drive) =====================

    private async void AtualizarStatusGoogleDrive()
    {
        var conectado = _googleDriveService.EstaConectado();

        if (!conectado)
        {
            TxtLoginStatus.Text = "Não conectado";
            TxtLoginStatus.IsVisible = true;
            ChipContaConectada.IsVisible = false;
            TxtLoginArmazenamento.Text = "";
            return;
        }

        // Conectado: o chip substitui o texto simples "Conectado", dando um indicativo visual mais
        // claro de conta ativa (item 2 do refinamento visual).
        TxtLoginStatus.IsVisible = false;

        try
        {
            var info = await _googleDriveService.ObterInfoContaAsync();
            TxtChipEmail.Text = info.Email;
            ChipContaConectada.IsVisible = true;
            TxtLoginArmazenamento.Text = info.TotalBytes is > 0
                ? $"Armazenamento: {info.UsadoBytes / 1_073_741_824.0:F1} GB usados de {info.TotalBytes / 1_073_741_824.0:F1} GB"
                : $"Armazenamento usado: {info.UsadoBytes / 1_073_741_824.0:F1} GB";
        }
        catch
        {
            // Conectado, mas não deu pra buscar os dados da conta agora (ex.: sem rede no momento) —
            // mostra o chip mesmo assim, só sem o e-mail, em vez de não indicar nada.
            TxtChipEmail.Text = "Conectado";
            ChipContaConectada.IsVisible = true;
            TxtLoginArmazenamento.Text = "";
        }
    }

    /// <summary>
    /// Faz login no Google usando as credenciais embutidas do app (abre o navegador só na
    /// primeira vez — depois disso o token salvo se renova sozinho, sem precisar logar de novo).
    /// </summary>
    private async void BtnConectarGoogleDrive_Click(object? sender, RoutedEventArgs e)
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
        GridDeletar.IsVisible = !vazio;
        PainelVazioDeletar.IsVisible = vazio;
    }

    /// <summary>
    /// Zona de arraste real (item 2 do refinamento visual): arrastar arquivos/pastas do Explorer
    /// pra cima do estado vazio já adiciona à lista, sem precisar passar pelos botões.
    /// </summary>
    private void PainelVazioDeletar_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (this.FindResource("AccentBrush") is IBrush destaque)
                RetanguloTracejadoDeletar.Stroke = destaque;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void PainelVazioDeletar_DragLeave(object? sender, DragEventArgs e)
    {
        if (this.FindResource("BorderStrongBrush") is IBrush borda)
            RetanguloTracejadoDeletar.Stroke = borda;
    }

    private void PainelVazioDeletar_Drop(object? sender, DragEventArgs e)
    {
        PainelVazioDeletar_DragLeave(sender, e);

        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var caminhos = (e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>())
            .Select(i => i.TryGetLocalPath())
            .Where(c => !string.IsNullOrEmpty(c))
            .Cast<string>()
            .ToArray();

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

    private async void BtnDeletarAdicionarArquivos_Click(object? sender, RoutedEventArgs e)
    {
        var arquivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar arquivos para excluir",
            AllowMultiple = true
        });
        if (arquivos.Count == 0) return;

        foreach (var caminho in arquivos.Select(a => a.TryGetLocalPath()).OfType<string>())
        {
            if (_itensParaExcluir.Any(i => i.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase))) continue;
            _itensParaExcluir.Add(new ItemParaExcluir(Path.GetFileName(caminho), caminho, "Arquivo"));
        }

        AtualizarGridDeletar();
    }

    private async void BtnDeletarAdicionarPasta_Click(object? sender, RoutedEventArgs e)
    {
        var pastaEscolhida = await EscolherPastaAsync("Selecionar pasta para excluir");
        if (pastaEscolhida is null) return;

        var caminho = pastaEscolhida.TrimEnd(Path.DirectorySeparatorChar);
        if (_itensParaExcluir.Any(i => i.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase)))
        {
            AtualizarGridDeletar();
            return;
        }

        var nome = Path.GetFileName(caminho);
        _itensParaExcluir.Add(new ItemParaExcluir(string.IsNullOrWhiteSpace(nome) ? caminho : nome, caminho, "Pasta"));
        AtualizarGridDeletar();
    }

    private void BtnDeletarRemoverDaLista_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in GridDeletar.SelectedItems.Cast<ItemParaExcluir>().ToList())
            _itensParaExcluir.Remove(item);

        AtualizarGridDeletar();
    }

    /// <summary>
    /// Exclusão manual e definitiva. Isto NUNCA roda automaticamente — é sempre um clique
    /// explícito do usuário, dado que é irreversível.
    /// </summary>
    private void BtnExcluirDefinitivamente_Click(object? sender, RoutedEventArgs e)
    {
        if (_itensParaExcluir.Count == 0) return;

        var confirmar = MessageBox.Show(
            $"Você está prestes a excluir definitivamente {_itensParaExcluir.Count} item(ns).\n\n" +
            "Essa ação NÃO pode ser desfeita. Um registro em CSV do que será apagado é gerado " +
            "antes, mas ele é só uma lista em texto, não uma cópia dos arquivos.\n\nConfirma a exclusão definitiva?",
            "Confirmar exclusão definitiva", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
            textoConfirmar: "Excluir definitivamente", textoCancelar: "Cancelar", perigo: true);

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
                "Falha ao gerar backup", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No,
                textoConfirmar: "Excluir mesmo assim", textoCancelar: "Cancelar", perigo: true);

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
    }

    /// <summary>Gera o backup CSV (seção 4 da spec) do que será apagado, antes de excluir de verdade —
    /// a mesma exigência de "registro antes de qualquer operação" que vale para o organizador.</summary>
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
            "Confirmar backup e exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
            textoConfirmar: "Enviar e excluir", textoCancelar: "Cancelar");

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

    private async void BtnDeletarEnviarNuvem_Click(object? sender, RoutedEventArgs e)
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

    private void BtnSalvarConfigAgente_Click(object? sender, RoutedEventArgs e)
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
        GridSugestoes.IsVisible = !vazio;
        PainelVazioSugestoes.IsVisible = vazio;
    }

    /// <summary>Chamado pelo App (via AgenteScheduler.AnaliseFinalizada) quando uma análise em
    /// segundo plano termina — só recarrega a grid se a janela já estiver aberta.</summary>
    public void AtualizarSugestoesAposAnaliseExterna() => CarregarSugestoes();

    private void BtnRecarregarSugestoes_Click(object? sender, RoutedEventArgs e) => CarregarSugestoes();

    private async void BtnAnalisarAgora_Click(object? sender, RoutedEventArgs e)
    {
        if (_config.PastasAnalisadas.Count == 0)
        {
            MessageBox.Show("Ainda não há nenhuma pasta para analisar. Escolha pelo menos uma em Ajustes.",
                "CubeBin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

    private void BtnAprovarSugestoes_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in GridSugestoes.SelectedItems.Cast<SugestaoExibicao>().ToList())
            _sugestaoService.AtualizarStatus(_config.ArquivoSugestoes, item.Sugestao.Id, "Aprovado");

        CarregarSugestoes();
    }

    private void BtnIgnorarSugestoes_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in GridSugestoes.SelectedItems.Cast<SugestaoExibicao>().ToList())
            _sugestaoService.AtualizarStatus(_config.ArquivoSugestoes, item.Sugestao.Id, "Ignorado");

        CarregarSugestoes();
    }

    /// <summary>Exclusão definitiva dos itens aprovados — mesma filosofia do módulo Deletar:
    /// confirmação explícita, backup CSV antes, nunca automático.</summary>
    private void BtnExcluirAprovadas_Click(object? sender, RoutedEventArgs e)
    {
        var aprovadas = _sugestoesExibicao.Where(i => i.Sugestao.Status == "Aprovado").ToList();
        if (aprovadas.Count == 0) return;

        var totalBytes = aprovadas.Sum(i => i.Sugestao.TamanhoBytes);
        var confirmar = MessageBox.Show(
            $"Excluir definitivamente {aprovadas.Count} arquivo(s) aprovado(s), liberando " +
            $"{FormatarBytes.Formatar(totalBytes)}?\n\n" +
            "Essa ação não pode ser desfeita. Um registro em CSV do que será apagado é gerado antes.",
            "Confirmar exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
            textoConfirmar: "Excluir definitivamente", textoCancelar: "Cancelar", perigo: true);

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
                "Falha ao gerar backup", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No,
                textoConfirmar: "Excluir mesmo assim", textoCancelar: "Cancelar", perigo: true);

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

    private async void BtnSugestoesEnviarNuvem_Click(object? sender, RoutedEventArgs e)
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
        ScrollCubos.IsVisible = !vazio;
        PainelVazioCubos.IsVisible = vazio;
    }

    private void BtnRecarregarCubos_Click(object? sender, RoutedEventArgs e) => CarregarCubos();

    private async void Cubo_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if ((e.Source as Control)?.DataContext is not CuboExibicao cubo) return;

        var janela = new DetalhesPacoteWindow(cubo.Pacote, _googleDriveService);
        await janela.ShowDialog(this);
    }

    /// <summary>Escolhe uma pasta no seletor do sistema. Devolve null se o usuário cancelar.</summary>
    private async Task<string?> EscolherPastaAsync(string titulo)
    {
        var pastas = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = titulo });
        return pastas.Count > 0 ? pastas[0].TryGetLocalPath() : null;
    }

    private class CuboExibicao
    {
        public PacoteBackup Pacote { get; }
        public string NomeArquivoZip => Pacote.NomeArquivoZip;
        public string TamanhoTexto => FormatarBytes.Formatar(Pacote.TamanhoBytes);
        public string DataTexto => $"Enviado em {Pacote.EnviadoEm:dd/MM/yyyy HH:mm}";
        public string DetalheTexto => $"{TamanhoTexto} · {Pacote.Itens.Count} arquivo(s)";

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
