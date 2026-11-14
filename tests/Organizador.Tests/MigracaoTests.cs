using Comum;
using Organizador.Core.Services;
using Xunit;

namespace Organizador.Tests;

/// <summary>
/// A separação do CubeBin em dois apps não pode perder nada de quem já usava a 2.x: as pastas, as
/// regras e o agendamento precisam chegar ao organizador, venha ele antes ou depois do CubeBin 3.
/// </summary>
public class MigracaoTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "organizador-migracao-" + Guid.NewGuid().ToString("N"));
    private string PastaCubeBin => Path.Combine(_raiz, "CubeBin");
    private string CaminhoConfigOrganizador => Path.Combine(_raiz, "NokyLabs", "Organizador", "config.json");

    public MigracaoTests() => Directory.CreateDirectory(PastaCubeBin);

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch { }
    }

    /// <summary>Um config.json como o que o CubeBin 2.x gravava (tipoDestino como número: 1 = Google Drive).</summary>
    private const string ConfigDaVersao2 = """
    {
      "versaoConfig": 1,
      "diretorioTemporario": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\temp",
      "execucoesPorDia": 2,
      "horarioInicio": "09:30:00",
      "tempoAusenciaMinutos": 15,
      "janelaCancelamentoSegundos": 60,
      "iniciarComWindows": true,
      "pastasPadrao": [ { "id": "downloads", "nome": "Downloads", "caminho": "C:\\Users\\teste\\Downloads", "removivel": false, "editavel": true, "organizarSubpastas": false, "extensoesMonitoradas": ["*"] } ],
      "pastasPersonalizadas": [ { "id": "aulas", "nome": "Aulas", "caminho": "D:\\Aulas", "removivel": true, "editavel": true, "organizarSubpastas": false, "extensoesMonitoradas": ["*"],
        "regrasPersonalizadas": { "slides": { "chave": "slides", "extensoes": [".pptx"], "destino": "", "tipoDestino": 1, "caminhoDrive": "Aulas/Slides", "subpastas": {} } } } ],
      "regrasExtensao": {
        "imagens": { "chave": "imagens", "extensoes": [".png", ".jpg"], "destino": "Imagens", "tipoDestino": 0, "caminhoDrive": null, "subpastas": {} },
        "fotos": { "chave": "fotos", "extensoes": [".heic"], "destino": "", "tipoDestino": 1, "caminhoDrive": "CubeBin/Fotos", "subpastas": {} }
      },
      "backup": { "diretorioCsv": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\backups", "manterUltimosN": 30 },
      "arquivoFilaPendencias": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\pendencias.json",
      "arquivoRecall": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\recall.json",
      "pastaCredenciaisGoogleDrive": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\google-credentials",
      "agenteAtivo": true,
      "arquivoSugestoes": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\sugestoes.json",
      "arquivoPacotesBackup": "C:\\Users\\teste\\AppData\\Local\\CubeBin\\pacotes.json"
    }
    """;

    [Fact]
    public void TrazPastasRegrasEAgendamentoDaVersao2()
    {
        File.WriteAllText(Path.Combine(PastaCubeBin, "config.json"), ConfigDaVersao2);

        var servico = new ConfigService(CaminhoConfigOrganizador, PastaCubeBin);
        var config = servico.CarregarOuCriarPadrao();

        Assert.True(servico.MigrouDaVersao2);
        Assert.Equal(2, config.ExecucoesPorDia);
        Assert.Equal(new TimeOnly(9, 30), config.HorarioInicio);
        Assert.Equal(15, config.TempoAusenciaMinutos);
        Assert.True(config.IniciarComWindows);
        Assert.Contains(config.PastasPadrao, p => p.Id == "downloads");
        Assert.Contains(config.PastasPersonalizadas, p => p.Id == "aulas");
        Assert.Equal("Imagens", config.RegrasExtensao["imagens"].Destino);
        // Pendências e histórico continuam onde estavam, para nada se perder.
        Assert.EndsWith("pendencias.json", config.ArquivoFilaPendencias);
        Assert.Contains("CubeBin", config.ArquivoRecall);
        Assert.True(File.Exists(CaminhoConfigOrganizador));
    }

    [Fact]
    public void RegrasDoDriveViramPastasLocaisComAviso()
    {
        File.WriteAllText(Path.Combine(PastaCubeBin, "config.json"), ConfigDaVersao2);

        var servico = new ConfigService(CaminhoConfigOrganizador, PastaCubeBin);
        var config = servico.CarregarOuCriarPadrao();

        // Sem "destino": usa a última pasta do caminho no Drive.
        Assert.Equal("Fotos", config.RegrasExtensao["fotos"].Destino);
        Assert.Equal("Slides", config.PastasPersonalizadas.Single(p => p.Id == "aulas").RegrasPersonalizadas!["slides"].Destino);
        Assert.NotNull(servico.AvisoDeMigracao);
        Assert.Contains("Fotos", servico.AvisoDeMigracao);
        Assert.Contains("Aulas", servico.AvisoDeMigracao);
    }

    [Fact]
    public void NaoMexeNoConfigAntigoEGuardaUmaCopia()
    {
        var original = Path.Combine(PastaCubeBin, "config.json");
        File.WriteAllText(original, ConfigDaVersao2);

        new ConfigService(CaminhoConfigOrganizador, PastaCubeBin).CarregarOuCriarPadrao();

        Assert.Equal(ConfigDaVersao2, File.ReadAllText(original));
        Assert.Equal(ConfigDaVersao2, File.ReadAllText(Path.Combine(PastaCubeBin, MigracaoDaVersao2.NomeDaCopia)));
    }

    [Fact]
    public void FuncionaMesmoSeOCubeBin3JaAbriuAntes()
    {
        // O CubeBin 3 abriu primeiro: guardou a cópia da 2.x e reescreveu o config.json só com a parte dele.
        File.WriteAllText(Path.Combine(PastaCubeBin, MigracaoDaVersao2.NomeDaCopia), ConfigDaVersao2);
        File.WriteAllText(Path.Combine(PastaCubeBin, "config.json"), """{ "versaoConfig": 3, "agenteAtivo": true }""");

        var servico = new ConfigService(CaminhoConfigOrganizador, PastaCubeBin);
        var config = servico.CarregarOuCriarPadrao();

        Assert.True(servico.MigrouDaVersao2);
        Assert.Contains(config.PastasPersonalizadas, p => p.Id == "aulas");
        Assert.Equal("Imagens", config.RegrasExtensao["imagens"].Destino);
    }

    [Fact]
    public void SoMigraUmaVez()
    {
        File.WriteAllText(Path.Combine(PastaCubeBin, "config.json"), ConfigDaVersao2);
        new ConfigService(CaminhoConfigOrganizador, PastaCubeBin).CarregarOuCriarPadrao();

        var segundaVez = new ConfigService(CaminhoConfigOrganizador, PastaCubeBin);
        segundaVez.CarregarOuCriarPadrao();

        Assert.False(segundaVez.MigrouDaVersao2);
        Assert.Null(segundaVez.AvisoDeMigracao);
    }

    [Fact]
    public void SemCubeBinAntigoComecaDoPadrao()
    {
        var servico = new ConfigService(CaminhoConfigOrganizador, PastaCubeBin);
        var config = servico.CarregarOuCriarPadrao();

        Assert.False(servico.MigrouDaVersao2);
        Assert.Contains(".zip", config.RegrasExtensao["compactados"].Extensoes);
        // Os arquivos de um organizador novo ficam na pasta dele, não na do CubeBin.
        Assert.StartsWith(Path.GetDirectoryName(CaminhoConfigOrganizador)!, config.ArquivoRecall);
    }
}
