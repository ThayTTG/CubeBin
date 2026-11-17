using CubeBin.Core.Plataforma;
using Xunit;

namespace CubeBin.Tests;

/// <summary>Regras de plataforma que não dependem do sistema (rodam no Windows e no Linux).</summary>
public class PastasXdgTests
{
    private const string Home = "/home/ana";

    [Fact]
    public void LeAsPastasTraduzidas()
    {
        var conteudo = """
            # This file is written by xdg-user-dirs-update
            XDG_DESKTOP_DIR="$HOME/Área de trabalho"
            XDG_DOWNLOAD_DIR="$HOME/Transferências"
            XDG_PICTURES_DIR="$HOME/Imagens"
            XDG_MUSIC_DIR="$HOME/Música"
            """;

        Assert.Equal("/home/ana/Transferências", PastasXdg.LerDoConteudo(conteudo, "DOWNLOAD", Home));
        Assert.Equal("/home/ana/Área de trabalho", PastasXdg.LerDoConteudo(conteudo, "DESKTOP", Home));
        Assert.Equal("/home/ana/Imagens", PastasXdg.LerDoConteudo(conteudo, PastasDoSistema.ChaveXdg(PastaPadrao.Imagens), Home));
        Assert.Equal("/home/ana/Música", PastasXdg.LerDoConteudo(conteudo, PastasDoSistema.ChaveXdg(PastaPadrao.Musicas), Home));
        // Vídeos não está no arquivo: quem chama usa o nome padrão.
        Assert.Null(PastasXdg.LerDoConteudo(conteudo, PastasDoSistema.ChaveXdg(PastaPadrao.Videos), Home));
    }

    [Fact]
    public void AceitaCaminhoAbsoluto()
    {
        Assert.Equal("/dados/baixados",
            PastasXdg.LerDoConteudo("XDG_DOWNLOAD_DIR=\"/dados/baixados/\"", "DOWNLOAD", Home));
    }

    [Theory]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/\"")]
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME/Documentos\"")]
    [InlineData("# XDG_DOWNLOAD_DIR=\"$HOME/Baixados\"")]
    [InlineData("XDG_DOWNLOAD_DIR=\"relativo/Baixados\"")]
    public void SemDownloadsValidoDevolveNull(string conteudo)
    {
        Assert.Null(PastasXdg.LerDoConteudo(conteudo, "DOWNLOAD", Home));
    }
}

public class PastasDoSistemaTests
{
    [Fact]
    public void GarantirCriaAPastaQueFaltaERegistra()
    {
        using var teste = new PastaDeTeste();
        var caminho = PastasDoSistema.Garantir(PastaPadrao.Videos, out var criada);
        Assert.True(criada);
        Assert.True(Directory.Exists(caminho));
        Assert.StartsWith(teste.Raiz, caminho);

        PastasDoSistema.Garantir(PastaPadrao.Videos, out criada);
        Assert.False(criada);
    }

    [Fact]
    public void PrefixosDoPrd()
    {
        Assert.Equal("Doc", PastasDoSistema.PrefixoPadrao(PastaPadrao.Documentos));
        Assert.Equal("Desk", PastasDoSistema.PrefixoPadrao(PastaPadrao.AreaDeTrabalho));
    }
}

public class OciosidadeLinuxTests
{
    [Theory]
    [InlineData("(uint64 125000,)\n", 125000)]
    [InlineData("(uint32 4200,)\n", 4200)]
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
        Assert.Equal("\"/home/ana/Meus Apps/CubeBin\"", Inicializacao.EscaparExec("/home/ana/Meus Apps/CubeBin"));
    }

    [Fact]
    public void AtalhoTemOsCamposDoPadraoXdg()
    {
        var texto = Inicializacao.MontarAtalhoAutostart("CubeBin", "/opt/cubebin/CubeBin");
        Assert.StartsWith("[Desktop Entry]\n", texto);
        Assert.Contains("Exec=\"/opt/cubebin/CubeBin\"\n", texto);
    }

    [Fact]
    public void HabilitarEDesabilitarNoLinuxMexeSoNoArquivoDoAutostart()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var teste = new PastaDeTeste();
        var servico = new Inicializacao("CubeBin") { PastaAutostart = teste.Caminho("autostart") };
        Assert.False(servico.EstaHabilitado());
        servico.Habilitar("/tmp/CubeBin");
        Assert.True(servico.EstaHabilitado());
        servico.Desabilitar();
        Assert.False(servico.EstaHabilitado());
    }
}

public class AmbienteTests
{
    [Fact]
    public void PastaDeDadosEhSempreUmCaminhoCompleto()
    {
        Assert.True(Path.IsPathRooted(CubeBin.Core.Ambiente.PastaDadosPadrao()));
    }
}
