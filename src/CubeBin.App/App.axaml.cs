using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CubeBin.App.Services;
using CubeBin.Core;
using CubeBin.Core.Models;
using CubeBin.Core.Services;

namespace CubeBin.App;

/// <summary>
/// O app em si: sobe os serviços do Core, os agendadores e o ícone da bandeja. A janela principal
/// só é criada quando alguém pede para abrir (menu da bandeja), igual à versão 1. Os avisos de
/// erro grave usam a caixa de mensagem NATIVA do Windows de propósito: se o próprio visual for a
/// causa do erro, a mensagem ainda precisa conseguir aparecer. O resto do app usa DialogoCubeBin.
/// </summary>
public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private BackgroundScheduler? _backgroundScheduler;
    private AgenteScheduler? _agenteScheduler;
    private ConfigService? _configService;
    private INotificationService? _notificationService;
    private OrganizadorService? _organizadorService;
    private GoogleDriveService? _googleDriveService;
    private readonly InicializacaoService _inicializacaoService = new();
    private readonly RecallService _recallService = new();

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public static readonly string CaminhoLogCrash = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CubeBin", "crash.log");

    /// <summary>A janela principal, se já foi aberta alguma vez (fechar só esconde).</summary>
    public MainWindow? JanelaPrincipal => _desktop?.MainWindow as MainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        _desktop = desktop;
        // O app continua residente na bandeja mesmo com a janela fechada (seção 7 da spec).
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) => AoSair();

        // Captura qualquer exceção não tratada na thread da tela e grava em crash.log antes de
        // encerrar — sem isso, uma falha na inicialização derruba o processo silenciosamente.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            RegistrarCrash(args.Exception);
            Program.AvisoDoSistema(
                $"O CubeBin encontrou um erro e vai fechar.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro inesperado");
            args.Handled = true;
            Encerrar();
        };

        try
        {
            IniciarServicos();
        }
        catch (Exception ex)
        {
            RegistrarCrash(ex);
            Program.AvisoDoSistema(
                $"Não foi possível iniciar o CubeBin.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro na inicialização");
            Encerrar();
            return;
        }

        // "CubeBin.exe --abrir" já abre a janela (útil para o launcher e para testar a tela).
        if (desktop.Args?.Contains("--abrir", StringComparer.OrdinalIgnoreCase) == true)
            AbrirJanelaPrincipal();
    }

    private void IniciarServicos()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var caminhoConfig = Path.Combine(appData, "CubeBin", "config.json");

        _configService = new ConfigService(caminhoConfig);
        var config = _configService.CarregarOuCriarPadrao();

        // Sincroniza o início automático (registro do Windows / autostart do Linux) com o que está
        // salvo no config.json — cobre o caso de alguém ter removido a entrada manualmente fora do app.
        SincronizarInicializacaoComWindows(config);

        _notificationService = CriarServicoDeNotificacao();
        _googleDriveService = new GoogleDriveService(config.PastaCredenciaisGoogleDrive);

        // Se as credenciais embutidas já estiverem preenchidas e já houve uma conexão bem-sucedida
        // antes, isso restaura a sessão em segundo plano sem abrir o navegador de novo (o token
        // salvo se renova sozinho). Se não houver token válido ainda, falha silenciosamente — o
        // usuário conecta manualmente pela UI quando quiser.
        if (GoogleDriveCredentials.EstaConfigurado)
            _ = TentarReconectarGoogleDriveAsync();

        _organizadorService = new OrganizadorService(
            _configService,
            new BackupService(),
            new ClassificadorService(),
            new PendenciaService(),
            _recallService,
            _googleDriveService,
            new IdleDetectionService(),
            _notificationService);

        _backgroundScheduler = new BackgroundScheduler(_configService, _organizadorService);
        _backgroundScheduler.Iniciar();

        // Camada 2 — agente de sugestões: desativado por padrão (config.AgenteAtivo), reaproveita a
        // mesma checagem de ausência do resto do app. Nunca apaga nada sozinho, só sugere.
        _agenteScheduler = new AgenteScheduler(
            _configService, new AgenteSugestaoService(), new SugestaoService(),
            new IdleDetectionService(), _notificationService);
        _agenteScheduler.AnaliseFinalizada += () =>
        {
            if (JanelaPrincipal is MainWindow janela)
                Dispatcher.UIThread.Invoke(janela.AtualizarSugestoesAposAnaliseExterna);
        };
        _agenteScheduler.Iniciar();

        _trayIconManager = new TrayIconManager(
            this,
            abrirConfiguracoes: AbrirJanelaPrincipal,
            executarAgora: () => _ = ExecutarOrganizacaoAgoraAsync(),
            sair: Encerrar);
    }

    /// <summary>
    /// Dispara uma organização imediata a pedido do usuário — menu da bandeja ou botão na UI.
    /// Não faz checagem de ausência nem espera a janela de cancelamento (ver OrganizadorService.ExecutarManualmenteAsync).
    /// </summary>
    public async Task ExecutarOrganizacaoAgoraAsync()
    {
        if (_configService is null || _organizadorService is null) return;

        try
        {
            var config = _configService.CarregarOuCriarPadrao();
            await _organizadorService.ExecutarManualmenteAsync(config);
        }
        catch (Exception ex)
        {
            RegistrarCrash(ex);
            DialogoCubeBin.Show(
                $"Não foi possível concluir a organização.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro ao organizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Toasts nativos no Windows; no Linux, janela de contagem + notify-send.</summary>
    private static INotificationService CriarServicoDeNotificacao()
    {
#if WINDOWS
        return new ToastNotificationService();
#else
        return new LinuxNotificationService();
#endif
    }

    private void SincronizarInicializacaoComWindows(AppConfig config)
    {
        // Windows: registro (HKCU\...\Run). Linux: ~/.config/autostart. Em outros sistemas, nada.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;

        var caminhoExecutavel = Environment.ProcessPath;
        if (string.IsNullOrEmpty(caminhoExecutavel)) return;

        if (config.IniciarComWindows)
            _inicializacaoService.Habilitar(caminhoExecutavel);
        else
            _inicializacaoService.Desabilitar();
    }

    private async Task TentarReconectarGoogleDriveAsync()
    {
        try
        {
            await _googleDriveService!.ConectarAsync();
        }
        catch
        {
            // Falha silenciosa: token pode ter expirado/sido revogado, ou ainda não houve conexão
            // bem-sucedida antes. O usuário pode clicar em "Conectar ao Google Drive" na UI se precisar.
        }
    }

    private void AbrirJanelaPrincipal()
    {
        if (_desktop is null) return;

        var janela = JanelaPrincipal ?? new MainWindow(
            _configService!, _notificationService!, _organizadorService!, _inicializacaoService,
            _recallService, _googleDriveService!, ExecutarOrganizacaoAgoraAsync);
        _desktop.MainWindow = janela;
        janela.Show();
        if (janela.WindowState == WindowState.Minimized)
            janela.WindowState = WindowState.Normal;
        janela.Activate();
    }

    private void Encerrar() => _desktop?.Shutdown();

    private void AoSair()
    {
        _backgroundScheduler?.Parar();
        _agenteScheduler?.Parar();
        _trayIconManager?.Dispose();
    }

    public static void RegistrarCrash(Exception ex)
    {
        try
        {
            var diretorio = Path.GetDirectoryName(CaminhoLogCrash);
            if (!string.IsNullOrEmpty(diretorio))
                Directory.CreateDirectory(diretorio);

            File.AppendAllText(CaminhoLogCrash,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // Se nem o log conseguir ser gravado, não há mais nada a fazer aqui.
        }
    }
}
