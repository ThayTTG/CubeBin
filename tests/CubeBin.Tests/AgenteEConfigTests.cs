using Comum;
using CubeBin.App;
using CubeBin.Core.Services;
using Xunit;

namespace CubeBin.Tests;

public class AgendamentoDoAgenteTests
{
    [Fact]
    public void AgenteUsaAMesmaContaDoOrganizador()
    {
        Assert.Equal(new[] { new TimeOnly(3, 0), new TimeOnly(15, 0) },
            AgenteScheduler.ObterHorariosDoDia(2, new TimeOnly(3, 0)));
        Assert.Equal(Agendamento.ObterHorariosDoDia(5, new TimeOnly(7, 15)),
            AgenteScheduler.ObterHorariosDoDia(5, new TimeOnly(7, 15)));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(2_400_000_000, "2.2 GB")]
    public void FormataTamanhos(long bytes, string esperado) =>
        Assert.Equal(esperado, FormatarBytes.Formatar(bytes).Replace(',', '.'));
}

/// <summary>
/// O CubeBin 3 lê o config.json da 2.x (que tinha a organização junto), fica só com a parte da
/// faxina e da nuvem, e deixa uma cópia intacta para o organizador ler a parte dele.
/// </summary>
public class ConfigEMigracaoTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "cubebin-testes-" + Guid.NewGuid().ToString("N"));
    private string CaminhoConfig => Path.Combine(_pasta, "config.json");

    public ConfigEMigracaoTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch { }
    }

    private const string ConfigDaVersao2 = """
    {
      "versaoConfig": 1,
      "tempoAusenciaMinutos": 15,
      "iniciarComWindows": true,
      "pastasPadrao": [ { "id": "downloads", "nome": "Downloads", "caminho": "C:\\Users\\teste\\Downloads", "removivel": false } ],
      "pastasPersonalizadas": [
        { "id": "aulas", "nome": "Aulas", "caminho": "D:\\Aulas", "removivel": true },
        { "id": "repetida", "nome": "Downloads de novo", "caminho": "C:\\Users\\teste\\Downloads", "removivel": true }
      ],
      "regrasExtensao": { "imagens": { "chave": "imagens", "extensoes": [".png"], "destino": "Imagens" } },
      "backup": { "diretorioCsv": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\backups", "manterUltimosN": 12 },
      "pastaCredenciaisGoogleDrive": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\google-credentials",
      "agenteAtivo": true,
      "agenteExecucoesPorDia": 3,
      "agenteDiasArquivoGrandeParado": 90,
      "arquivoSugestoes": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\sugestoes.json",
      "caminhoDriveBackups": "Meus/Backups",
      "arquivoPacotesBackup": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\pacotes.json"
    }
    """;

    [Fact]
    public void TrazOAgenteEANuvemDaVersao2()
    {
        File.WriteAllText(CaminhoConfig, ConfigDaVersao2);

        var config = new ConfigService(CaminhoConfig).CarregarOuCriarPadrao();

        Assert.Equal(3, config.VersaoConfig);
        Assert.True(config.AgenteAtivo);
        Assert.Equal(3, config.AgenteExecucoesPorDia);
        Assert.Equal(90, config.AgenteDiasArquivoGrandeParado);
        Assert.Equal(15, config.TempoAusenciaMinutos);
        Assert.True(config.IniciarComWindows);
        Assert.Equal("Meus/Backups", config.CaminhoDriveBackups);
        Assert.EndsWith("google-credentials", config.PastaCredenciaisGoogleDrive);
        Assert.EndsWith("pacotes.json", config.ArquivoPacotesBackup);
    }

    [Fact]
    public void AsPastasDoOrganizadorViramAsPastasAnalisadasSemRepetir()
    {
        File.WriteAllText(CaminhoConfig, ConfigDaVersao2);

        var config = new ConfigService(CaminhoConfig).CarregarOuCriarPadrao();

        Assert.Equal(new[] { "downloads", "aulas" }, config.PastasAnalisadas.Select(p => p.Id));
    }

    [Fact]
    public void OsRegistrosDeExclusaoGanhamPastaPropria()
    {
        File.WriteAllText(CaminhoConfig, ConfigDaVersao2);

        var config = new ConfigService(CaminhoConfig).CarregarOuCriarPadrao();

        // O "backups" da 2.x era o histórico da organização: fica com o organizador.
        Assert.Equal(Path.Combine(_pasta, "registros"), config.Backup.DiretorioCsv);
    }

    [Fact]
    public void GuardaACopiaIntactaParaOOrganizador()
    {
        File.WriteAllText(CaminhoConfig, ConfigDaVersao2);

        new ConfigService(CaminhoConfig).CarregarOuCriarPadrao();

        var copia = Path.Combine(_pasta, MigracaoDaVersao2.NomeDaCopia);
        Assert.Equal(ConfigDaVersao2, File.ReadAllText(copia));
        Assert.True(MigracaoDaVersao2.EhConfigDaVersao2(copia));
        // O config.json do CubeBin agora é só da faxina.
        Assert.DoesNotContain("regrasExtensao", File.ReadAllText(CaminhoConfig));
    }

    [Fact]
    public void InstalacaoNovaAnalisaOsDownloads()
    {
        var config = new ConfigService(CaminhoConfig).CarregarOuCriarPadrao();

        Assert.False(config.AgenteAtivo);
        Assert.Single(config.PastasAnalisadas);
        Assert.Equal("downloads", config.PastasAnalisadas[0].Id);
        Assert.False(File.Exists(Path.Combine(_pasta, MigracaoDaVersao2.NomeDaCopia)));
    }

    [Fact]
    public void PastaAnalisadaEntraSemRepetirESai()
    {
        var servico = new ConfigService(CaminhoConfig);
        var config = servico.CarregarOuCriarPadrao();

        servico.AdicionarPasta(config, _pasta);
        servico.AdicionarPasta(config, _pasta);
        Assert.Equal(2, config.PastasAnalisadas.Count);

        var id = config.PastasAnalisadas.Single(p => p.Caminho == _pasta).Id;
        servico.RemoverPasta(config, id);
        Assert.DoesNotContain(config.PastasAnalisadas, p => p.Caminho == _pasta);
    }
}
