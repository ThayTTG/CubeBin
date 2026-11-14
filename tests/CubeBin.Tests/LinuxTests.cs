using CubeBin.App;
using CubeBin.Core.Interop;
using CubeBin.Core.Services;
using Xunit;

namespace CubeBin.Tests;

/// <summary>
/// Regras da versão Linux que não dependem do sistema: rodam também no Windows (e o CI roda
/// todos os testes no Linux).
/// </summary>
public class PastasXdgTests
{
    private const string Home = "/home/ana";

    [Fact]
    public void LeDownloadsTraduzido()
    {
        var conteudo = """
            # This file is written by xdg-user-dirs-update
            XDG_DESKTOP_DIR="$HOME/Área de trabalho"
            XDG_DOWNLOAD_DIR="$HOME/Transferências"
            """;

        Assert.Equal("/home/ana/Transferências", PastasXdg.LerDoConteudo(conteudo, "DOWNLOAD", Home));
        Assert.Equal("/home/ana/Área de trabalho", PastasXdg.LerDoConteudo(conteudo, "DESKTOP", Home));
    }

    [Fact]
    public void AceitaCaminhoAbsoluto()
    {
        Assert.Equal("/dados/baixados",
            PastasXdg.LerDoConteudo("XDG_DOWNLOAD_DIR=\"/dados/baixados/\"", "DOWNLOAD", Home));
    }

    [Theory]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/\"")]   // pasta desativada
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME/Documentos\"")] // outra pasta
    [InlineData("# XDG_DOWNLOAD_DIR=\"$HOME/Baixados\"")]   // comentário
    [InlineData("XDG_DOWNLOAD_DIR=\"relativo/Baixados\"")]  // formato inválido
    public void SemDownloadsValidoDevolveNull(string conteudo)
    {
        Assert.Null(PastasXdg.LerDoConteudo(conteudo, "DOWNLOAD", Home));
    }
}

public class OciosidadeLinuxTests
{
    [Theory]
    [InlineData("(uint64 125000,)\n", 125000)]   // GNOME (Mutter IdleMonitor)
    [InlineData("(uint32 4200,)\n", 4200)]       // KDE (org.freedesktop.ScreenSaver)
    public void LeRespostaDoGdbus(string saida, int milissegundos)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milissegundos), OciosidadeLinux.LerRespostaGdbus(saida));
    }

    [Fact]
    public void RespostaEstranhaDevolveNull()
    {
        Assert.Null(OciosidadeLinux.LerRespostaGdbus("Error: GDBus.Error:org.freedesktop.DBus.Error.ServiceUnknown"));
    }
}

public class AutostartLinuxTests
{
    [Fact]
    public void ExecComEspacoEAcentoFicaEntreAspas()
    {
        Assert.Equal("\"/home/ana/Meus Apps/CubeBin\"", InicializacaoService.EscaparExec("/home/ana/Meus Apps/CubeBin"));
    }

    [Fact]
    public void ExecEscapaCaracteresReservados()
    {
        Assert.Equal("\"/opt/a\\$b\\\"c\\`d\\\\e\"", InicializacaoService.EscaparExec("/opt/a$b\"c`d\\e"));
    }

    [Fact]
    public void AtalhoTemOsCamposDoPadraoXdg()
    {
        var texto = InicializacaoService.MontarAtalhoAutostart("/home/ana/.local/share/NokyLabs/Apps/cubebin/CubeBin");

        Assert.StartsWith("[Desktop Entry]\n", texto);
        Assert.Contains("Type=Application\n", texto);
        Assert.Contains("Exec=\"/home/ana/.local/share/NokyLabs/Apps/cubebin/CubeBin\"\n", texto);
        Assert.Contains("X-GNOME-Autostart-enabled=true\n", texto);
    }

    [Fact]
    public void HabilitarEDesabilitarNoLinuxMexeSoNoArquivoDoAutostart()
    {
        if (!OperatingSystem.IsLinux()) return; // no Windows o serviço usa o registro

        var pasta = Path.Combine(Path.GetTempPath(), "cubebin-autostart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var servico = new InicializacaoService { PastaAutostart = pasta };
            Assert.False(servico.EstaHabilitado());

            servico.Habilitar("/tmp/CubeBin");
            Assert.True(servico.EstaHabilitado());
            Assert.True(File.Exists(Path.Combine(pasta, "cubebin.desktop")));

            servico.Desabilitar();
            Assert.False(servico.EstaHabilitado());
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
        }
    }
}

public class OrganizacaoLinuxTests
{
    [Theory]
    [InlineData(".directory", true)]          // metadados do KDE
    [InlineData(".hidden", true)]             // oculto
    [InlineData("video.mp4.part", true)]      // Firefox baixando
    [InlineData("setup.exe.crdownload", true)] // Chrome baixando
    [InlineData("foto.jpg", false)]
    [InlineData("relatorio.pdf", false)]
    public void IgnoraOcultosEDownloadsEmAndamento(string nome, bool ignorar)
    {
        Assert.Equal(ignorar, OrganizadorService.EhArquivoDeSistemaNoLinux(nome));
    }

    [Fact]
    public void InstaladoresDoLinuxSoEntramNoLinux()
    {
        var extensoes = ConfigService.ExtensoesInstaladores();

        Assert.Contains(".exe", extensoes);
        Assert.Equal(OperatingSystem.IsLinux(), extensoes.Contains(".deb"));
        Assert.Equal(OperatingSystem.IsLinux(), extensoes.Contains(".appimage"));
    }

    [Fact]
    public void ArvoreDoRecallSeparaSubpastasPeloSeparadorDoSistema()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "Downloads");
        var diretorio = Path.Combine(pasta, "Imagens", "2024");

        Assert.Equal(new[] { "Imagens", "2024" }, MainWindow.SegmentosAbaixoDaPasta(pasta, diretorio));
        Assert.Empty(MainWindow.SegmentosAbaixoDaPasta(pasta, pasta));
    }
}
