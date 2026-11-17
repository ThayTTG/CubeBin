using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core;
using CubeBin.Core.Plataforma;
using IOPath = System.IO.Path;

namespace CubeBin.App;

/// <summary>
/// O app: sobe os serviços, o ícone da bandeja e o agendador por ausência. Fechar a janela só a
/// esconde (o CubeBin continua na bandeja para o modo automático); "Sair" no menu da bandeja encerra.
/// Só existe um CubeBin aberto por vez: abrir de novo só mostra a janela do que já está rodando.
/// </summary>
public partial class App : Application
{
    /// <summary>%LOCALAPPDATA%\CubeBin (Windows) ou ~/.local/share/CubeBin (Linux).</summary>
    public static readonly string PastaDados = Ambiente.Padrao().PastaDados;

    public static readonly string CaminhoLogCrash = IOPath.Combine(PastaDados, "crash.log");

    /// <summary>Os serviços do app (criados ao iniciar).</summary>
    public static ServicosApp? Servicos { get; private set; }

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Bandeja? _bandeja;
    private AgendadorAutomatico? _agendador;
    private Mutex? _instanciaUnica;
    private FileSystemWatcher? _sinalAbrir;
    private readonly Inicializacao _inicializacao = new(InfoApp.NomeNaInicializacao);

    private static string CaminhoSinal => IOPath.Combine(PastaDados, "abrir.sinal");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        _desktop = desktop;

        if (!PegarInstanciaUnica())
        {
            // Já tem um CubeBin rodando: pede para ele mostrar a janela e sai.
            try { File.WriteAllText(CaminhoSinal, DateTime.Now.ToString("O")); } catch { }
            desktop.Shutdown();
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) => AoSair();

        var icone = Bandeja.CarregarIcone("avares://CubeBin/app.ico");
        Dialogo.Configurar(InfoApp.Nome, icone);

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            RegistrarCrash(args.Exception);
            Dialogo.Show($"Algo deu errado, mas nenhum arquivo foi perdido.\n\nDetalhes em:\n{CaminhoLogCrash}",
                "Erro inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            IniciarServicos(icone);
        }
        catch (Exception ex)
        {
            RegistrarCrash(ex);
            Program.AvisoDoSistema($"Não foi possível iniciar o CubeBin.\n\nDetalhes em:\n{CaminhoLogCrash}", "Erro na inicialização");
            desktop.Shutdown();
            return;
        }

        // Pelo "abrir junto com o sistema" o CubeBin começa só na bandeja; aberto pela pessoa, mostra a janela.
        var soBandeja = desktop.Args?.Contains("--bandeja", StringComparer.OrdinalIgnoreCase) == true;
        if (!soBandeja) AbrirJanela();
        if (_desktop.MainWindow is MainWindow janela) Captura.TalvezCapturar(janela, () => { _saindo = true; desktop.Shutdown(); });
    }

    private void IniciarServicos(WindowIcon? icone)
    {
        IAvisos avisos;
#if WINDOWS
        avisos = new AvisosWindows();
#else
        avisos = new AvisosLinux();
#endif
        Servicos = new ServicosApp(Ambiente.Padrao(), avisos);
        var config = Servicos.Config.Carregar();
        SincronizarInicializacao(config.Automacao.IniciarComOSistema);
        Servicos.Config.Alterada += c => SincronizarInicializacao(c.Automacao.IniciarComOSistema);

        _agendador = new AgendadorAutomatico(Servicos, id => AbrirJanela("cubos", id), () => AbrirJanela("liberar"));
        _agendador.Iniciar();
        _ = Servicos.ReconectarNuvemAsync();

        _bandeja = new Bandeja(this, InfoApp.Nome, icone,
            abrir: () => AbrirJanela(),
            textoAcaoPrincipal: "Organizar agora",
            acaoPrincipal: () => AbrirJanela("organizar"),
            sair: () => _desktop?.Shutdown());

        Directory.CreateDirectory(PastaDados);
        _sinalAbrir = new FileSystemWatcher(PastaDados, IOPath.GetFileName(CaminhoSinal))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _sinalAbrir.Created += (_, _) => Dispatcher.UIThread.Post(() => AbrirJanela());
        _sinalAbrir.Changed += (_, _) => Dispatcher.UIThread.Post(() => AbrirJanela());
    }

    /// <summary>Mostra a janela (criando se preciso) e, se pedido, uma página.</summary>
    public void AbrirJanela(string? pagina = null, object? parametro = null)
    {
        if (_desktop is null || Servicos is null) return;
        if (_desktop.MainWindow is not MainWindow janela)
        {
            janela = new MainWindow(Servicos);
            janela.Closing += (_, e) =>
            {
                // Fechar só esconde: o CubeBin continua cuidando dos arquivos pela bandeja.
                if (!_saindo)
                {
                    e.Cancel = true;
                    janela.Hide();
                }
            };
            _desktop.MainWindow = janela;
        }
        janela.Show();
        if (janela.WindowState == WindowState.Minimized) janela.WindowState = WindowState.Normal;
        janela.Activate();
        if (pagina is not null) janela.Navegar(pagina, parametro);
    }

    private bool _saindo;

    private bool PegarInstanciaUnica()
    {
        try
        {
            _instanciaUnica = new Mutex(initiallyOwned: true, "CubeBin-NokyLabs-instancia-unica", out var criado);
            return criado;
        }
        catch (Exception)
        {
            return true; // sem como saber: segue (melhor abrir do que não abrir)
        }
    }

    private void SincronizarInicializacao(bool ligado)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || IOPath.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;
            if (ligado) _inicializacao.Habilitar(exe, "--bandeja");
            else if (_inicializacao.EstaHabilitado()) _inicializacao.Desabilitar();
        }
        catch (Exception ex)
        {
            RegistrarCrash(ex);
        }
    }

    private void AoSair()
    {
        _saindo = true;
        _agendador?.Parar();
        _bandeja?.Dispose();
        _sinalAbrir?.Dispose();
        try { _instanciaUnica?.ReleaseMutex(); } catch { }
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
