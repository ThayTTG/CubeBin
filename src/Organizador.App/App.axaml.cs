using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Comum;
using Comum.UI;
using Organizador.App.Services;
using Organizador.Core.Models;
using Organizador.Core.Services;

namespace Organizador.App;

/// <summary>
/// O app em si: sobe os serviços do Core, o agendador e o ícone da bandeja. A janela principal
/// só é criada quando alguém pede para abrir (menu da bandeja). Os avisos de erro grave usam a
/// caixa de mensagem NATIVA do Windows de propósito: se o próprio visual for a causa do erro, a
/// mensagem ainda precisa conseguir aparecer. O resto do app usa o Dialogo da Comum.UI.
/// </summary>
public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private BackgroundScheduler? _backgroundScheduler;
    private ConfigService? _configService;
    private INotificationService? _notificationService;
    private OrganizadorService? _organizadorService;
    private readonly InicializacaoService _inicializacaoService = new(InfoApp.NomeNaInicializacao);
    private readonly RecallService _recallService = new();

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>%LOCALAPPDATA%\NokyLabs\Organizador — config, pendências, histórico e registros.</summary>
    public static readonly string PastaDados = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NokyLabs", InfoApp.PastaDados);

    public static readonly string CaminhoLogCrash = Path.Combine(PastaDados, "crash.log");

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

        var icone = TrayIconManager.CarregarIcone("avares://Organizador/app.ico");
        Dialogo.Configurar(InfoApp.Nome, icone);

        // Captura qualquer exceção não tratada na thread da tela e grava em crash.log antes de
        // encerrar — sem isso, uma falha na inicialização derruba o processo silenciosamente.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            RegistrarCrash(args.Exception);
            Program.AvisoDoSistema(
                $"O {InfoApp.Nome} encontrou um erro e vai fechar.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro inesperado");
            args.Handled = true;
            Encerrar();
        };

        try
        {
            IniciarServicos(icone);
        }
        catch (Exception ex)
        {
            RegistrarCrash(ex);
            Program.AvisoDoSistema(
                $"Não foi possível iniciar o {InfoApp.Nome}.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro na inicialização");
            Encerrar();
            return;
        }

        var abrirAgora = desktop.Args?.Contains("--abrir", StringComparer.OrdinalIgnoreCase) == true;

        // Quem veio do CubeBin 2.x recebe, uma vez, a explicação do que mudou.
        if (_configService!.MigrouDaVersao2)
        {
            abrirAgora = true;
            Dispatcher.UIThread.Post(AvisarSobreAMigracao, DispatcherPriority.Background);
        }

        // "Organizador.exe --abrir" já abre a janela (útil para o launcher e para testar a tela).
        if (abrirAgora)
            AbrirJanelaPrincipal();
    }

    private void IniciarServicos(Avalonia.Controls.WindowIcon? icone)
    {
        _configService = new ConfigService(
            Path.Combine(PastaDados, "config.json"),
            pastaDaVersao2: MigracaoDaVersao2.PastaPadraoDaVersao2);
        var config = _configService.CarregarOuCriarPadrao();

        // Sincroniza o registro do Windows com o que está salvo no config.json — cobre o caso de
        // alguém ter removido a entrada manualmente fora do app.
        SincronizarInicializacaoComWindows(config);

        _notificationService = new ToastNotificationService();

        _organizadorService = new OrganizadorService(
            _configService,
            new BackupService(),
            new ClassificadorService(),
            new PendenciaService(),
            _recallService,
            new IdleDetectionService(),
            _notificationService);

        _backgroundScheduler = new BackgroundScheduler(_configService, _organizadorService);
        _backgroundScheduler.Iniciar();

        _trayIconManager = new TrayIconManager(
            this, InfoApp.Nome, icone,
            abrir: AbrirJanelaPrincipal,
            textoAcaoPrincipal: "Organizar agora",
            acaoPrincipal: () => _ = ExecutarOrganizacaoAgoraAsync(),
            sair: Encerrar);
    }

    private void AvisarSobreAMigracao()
    {
        if (_configService is null) return;

        var mensagem =
            "A organização de arquivos agora é um app separado do CubeBin. Suas pastas, regras, " +
            "pendências e o histórico de arquivos movidos vieram junto, e tudo continua funcionando do mesmo jeito.\n\n" +
            "Apagar arquivos, as sugestões de limpeza e o backup no Google Drive continuam no CubeBin.";

        if (_configService.AvisoDeMigracao is { } aviso)
            mensagem += "\n\n" + aviso;

        _configService.LimparAvisoDeMigracao();
        Dialogo.Show(mensagem, "O que mudou", MessageBoxButton.OK, MessageBoxImage.Information);
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
            Dialogo.Show(
                $"Não foi possível concluir a organização.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro ao organizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SincronizarInicializacaoComWindows(AppConfig config)
    {
        // O registro do Windows só existe no Windows (fora dele o app roda só para testar a tela).
        if (!OperatingSystem.IsWindows()) return;

        var caminhoExecutavel = Environment.ProcessPath;
        if (string.IsNullOrEmpty(caminhoExecutavel)) return;

        if (config.IniciarComWindows)
            _inicializacaoService.Habilitar(caminhoExecutavel);
        else
            _inicializacaoService.Desabilitar();
    }

    private void AbrirJanelaPrincipal()
    {
        if (_desktop is null) return;

        var janela = JanelaPrincipal ?? new MainWindow(
            _configService!, _notificationService!, _organizadorService!, _inicializacaoService,
            _recallService, ExecutarOrganizacaoAgoraAsync);
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
        _trayIconManager?.Dispose();
    }

    public static void RegistrarCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(PastaDados);
            File.AppendAllText(CaminhoLogCrash, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // Se nem o log conseguir ser gravado, não há mais nada a fazer aqui.
        }
    }
}
