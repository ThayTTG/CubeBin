using System.IO;
using System.Windows;
using System.Windows.Threading;
using OrganizadorArquivos.App.Services;
using OrganizadorArquivos.Core;
using OrganizadorArquivos.Core.Models;
using OrganizadorArquivos.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace OrganizadorArquivos.App;

public partial class App : System.Windows.Application
{
    private TrayIconManager? _trayIconManager;
    private BackgroundScheduler? _backgroundScheduler;
    private ConfigService? _configService;
    private INotificationService? _notificationService;
    private OrganizadorService? _organizadorService;
    private GoogleDriveService? _googleDriveService;
    private readonly InicializacaoService _inicializacaoService = new();
    private readonly RecallService _recallService = new();

    private static readonly string CaminhoLogCrash = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OrganizadorArquivos", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Captura qualquer exceção não tratada (thread da UI e threads de background) e grava
        // em crash.log antes de encerrar — sem isso, uma falha na inicialização derruba o processo
        // silenciosamente, sem janela, sem ícone de bandeja e sem nada visível no Gerenciador de Tarefas.
        DispatcherUnhandledException += (_, args) =>
        {
            RegistrarCrash(args.Exception);
            MessageBox.Show(
                $"O CubeBin encontrou um erro e vai fechar.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                RegistrarCrash(ex);
        };

        try
        {
            IniciarServicos();
        }
        catch (Exception ex)
        {
            RegistrarCrash(ex);
            MessageBox.Show(
                $"Não foi possível iniciar o CubeBin.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro na inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void IniciarServicos()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var caminhoConfig = Path.Combine(appData, "OrganizadorArquivos", "config.json");

        _configService = new ConfigService(caminhoConfig);
        var config = _configService.CarregarOuCriarPadrao();

        // Sincroniza o registro do Windows com o que está salvo no config.json — cobre o caso de
        // alguém ter removido a entrada manualmente fora do app.
        SincronizarInicializacaoComWindows(config);

        _notificationService = new ToastNotificationService();
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

        _trayIconManager = new TrayIconManager(
            abrirConfiguracoes: AbrirJanelaPrincipal,
            executarAgora: () => _ = ExecutarOrganizacaoAgoraAsync(),
            sair: () => Shutdown());
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
            MessageBox.Show(
                $"Não foi possível concluir a organização.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro ao organizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SincronizarInicializacaoComWindows(AppConfig config)
    {
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
        var janela = MainWindow ?? new MainWindow(
            _configService!, _notificationService!, _organizadorService!, _inicializacaoService,
            _recallService, _googleDriveService!, ExecutarOrganizacaoAgoraAsync);
        MainWindow = janela;
        janela.Show();
        janela.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backgroundScheduler?.Parar();
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }

    private static void RegistrarCrash(Exception ex)
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
