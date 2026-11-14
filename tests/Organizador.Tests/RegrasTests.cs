using Comum;
using Comum.UI;
using Organizador.App;
using Organizador.Core.Models;
using Organizador.Core.Services;
using Xunit;

namespace Organizador.Tests;

public class AgendamentoTests
{
    [Fact]
    public void DuasVezesPorDiaAPartirDoMeioDia()
    {
        var config = new AppConfig { ExecucoesPorDia = 2, HorarioInicio = new TimeOnly(12, 0) };
        Assert.Equal(new[] { new TimeOnly(12, 0), new TimeOnly(0, 0) }, BackgroundScheduler.ObterHorariosDoDia(config));
    }

    [Fact]
    public void QuatroVezesPorDiaAPartirDasOito()
    {
        var config = new AppConfig { ExecucoesPorDia = 4, HorarioInicio = new TimeOnly(8, 0) };
        var horarios = BackgroundScheduler.ObterHorariosDoDia(config).Select(h => h.ToString("HH:mm"));
        Assert.Equal(new[] { "08:00", "14:00", "20:00", "02:00" }, horarios);
    }

    [Fact]
    public void ZeroExecucoesViraUma()
    {
        var config = new AppConfig { ExecucoesPorDia = 0, HorarioInicio = new TimeOnly(9, 30) };
        Assert.Equal(new[] { new TimeOnly(9, 30) }, BackgroundScheduler.ObterHorariosDoDia(config));
    }
}

public class ConfiguracaoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "organizador-testes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch { }
    }

    [Fact]
    public void CriaConfigPadraoComRegrasEDownloads()
    {
        Directory.CreateDirectory(_pasta);
        var config = new ConfigService(Path.Combine(_pasta, "config.json")).CarregarOuCriarPadrao();

        Assert.Contains(config.PastasPadrao, p => p.Id == "downloads" && !p.Removivel);
        Assert.Contains(".zip", config.RegrasExtensao["compactados"].Extensoes);
        Assert.True(File.Exists(Path.Combine(_pasta, "config.json")));
    }

    [Fact]
    public void PastaPadraoNuncaEhRemovida()
    {
        Directory.CreateDirectory(_pasta);
        var servico = new ConfigService(Path.Combine(_pasta, "config.json"));
        var config = servico.CarregarOuCriarPadrao();

        Assert.Throws<InvalidOperationException>(() => servico.RemoverPastaPersonalizada(config, "downloads"));
    }

    [Fact]
    public void PastaPersonalizadaEntraESai()
    {
        Directory.CreateDirectory(_pasta);
        var servico = new ConfigService(Path.Combine(_pasta, "config.json"));
        var config = servico.CarregarOuCriarPadrao();

        servico.AdicionarPastaPersonalizada(config, new PastaMonitorada { Id = "aulas", Nome = "Aulas", Caminho = _pasta });
        servico.Salvar(config);
        var relida = servico.CarregarOuCriarPadrao();
        Assert.Contains(relida.PastasPersonalizadas, p => p.Id == "aulas" && p.Removivel);

        servico.RemoverPastaPersonalizada(relida, "aulas");
        Assert.DoesNotContain(relida.PastasPersonalizadas, p => p.Id == "aulas");
    }
}

public class ClassificacaoTests
{
    [Fact]
    public void RarComSubpastaVaiParaCompactadosWinRar()
    {
        var config = new AppConfig
        {
            RegrasExtensao = new()
            {
                ["compactados"] = new RegraExtensao
                {
                    Chave = "compactados", Extensoes = new() { ".zip", ".rar" }, Destino = "Compactados",
                    Subpastas = new() { [".rar"] = "WinRAR" }
                }
            }
        };
        var classificador = new ClassificadorService();
        var resultado = classificador.Classificar(new FileInfo("projeto.RAR"), config, new PastaMonitorada());

        Assert.Equal(ResultadoClassificacao.Classificado, resultado.Resultado);
        Assert.Equal(Path.Combine("Downloads", "Compactados", "WinRAR"), classificador.ResolverCaminhoDestino("Downloads", resultado));
    }

    [Fact]
    public void ExtensaoSemRegraViraPendencia()
    {
        var resultado = new ClassificadorService().Classificar(new FileInfo("modelo.blend"), new AppConfig(), new PastaMonitorada());
        Assert.Equal(ResultadoClassificacao.SemRegra, resultado.Resultado);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(2_400_000_000, "2.2 GB")]
    public void FormataTamanhos(long bytes, string esperado) =>
        Assert.Equal(esperado, FormatarBytes.Formatar(bytes).Replace(',', '.'));
}

public class DialogoTests
{
    // A caixa de mensagem nativa (plano B do Dialogo) usa os mesmos números do Windows.
    [Fact]
    public void ValoresIguaisAosDoWindows()
    {
        Assert.Equal(4, (int)MessageBoxButton.YesNo);
        Assert.Equal(0x30, (int)MessageBoxImage.Warning);
        Assert.Equal(6, (int)MessageBoxResult.Yes);
        Assert.Equal(7, (int)MessageBoxResult.No);
    }
}
