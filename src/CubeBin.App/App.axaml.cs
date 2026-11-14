using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Comum;
using Comum.UI;
using CubeBin.App.Services;
using CubeBin.Core;
using CubeBin.Core.Models;
using CubeBin.Core.Services;

namespace CubeBin.App;

/// <summary>
/// O app em si: sobe os serviços do Core, o agendador do agente e o ícone da bandeja. A janela
/// principal só é criada quando alguém pede para abrir (menu da bandeja). Os avisos de erro grave
/// usam a caixa de mensagem NATIVA do Windows de propósito: se o próprio visual for a causa do
/// erro, a mensagem ainda precisa conseguir aparecer. O resto do app usa o Dialogo da Comum.UI.
///
/// Desde a versão 3, o CubeBin não organiza mais nada sozinho: isso virou um app à parte. Aqui
/// só o agente de sugestões roda em segundo plano (e só se a pessoa ligar).
/// </summary>
public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private AgenteScheduler? _agenteScheduler;
    private ConfigService? _configService;
    private INotificationService? _notificationService;
    private GoogleDriveService? _googleDriveService;
    private readonly InicializacaoService _inicializacaoService = new("CubeBin");

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>%LOCALAPPDATA%\CubeBin — a mesma pasta de sempre.</summary>
    public static readonly string PastaDados = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CubeBin");

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

        var icone = TrayIconManager.CarregarIcone("avares://CubeBin/app.ico");
        Dialogo.Configurar("CubeBin", icone);

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
            IniciarServicos(icone);
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

    private void IniciarServicos(WindowIcon? icone)
    {
        _configService = new ConfigService(Path.Combine(PastaDados, "config.json"));
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

        // Agente de sugestões: desativado por padrão (config.AgenteAtivo). Nunca apaga nada
        // sozinho, só sugere.
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
            this, "CubeBin", icone,
            abrir: AbrirJanelaPrincipal,
            textoAcaoPrincipal: "Procurar o que limpar",
            acaoPrincipal: ProcurarOQueLimpar,
            sair: Encerrar);
    }

    /// <summary>Pelo menu da bandeja: roda o agente agora e abre a janela nas Sugestões.</summary>
    private void ProcurarOQueLimpar()
    {
        AbrirJanelaPrincipal();
        JanelaPrincipal?.IrParaSugestoes();
        _agenteScheduler?.AnalisarAgora();
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
            _configService!, _notificationService!, _inicializacaoService, _googleDriveService!);
        _desktop.MainWindow = janela;
        janela.Show();
        if (janela.WindowState == WindowState.Minimized)
            janela.WindowState = WindowState.Normal;
        janela.Activate();
    }

    private void Encerrar() => _desktop?.Shutdown();

    private void AoSair()
    {
        _agenteScheduler?.Parar();
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
