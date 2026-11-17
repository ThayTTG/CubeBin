using System.Text.Json;
using CubeBin.Core.Agente;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.LiberarEspaco;
using CubeBin.Core.Nuvem;
using CubeBin.Core.Plataforma;
using Xunit;

namespace CubeBin.Tests;

public class EnvioNuvemTests
{
    private static (Cenario c, NuvemLocal nuvem, EnvioNuvem envio) Montar()
    {
        var c = new Cenario();
        var nuvem = new NuvemLocal(c.Pasta.Caminho("drive"));
        return (c, nuvem, new EnvioNuvem(c.Ambiente, c.Cubos, nuvem));
    }

    private static SelecaoLiberar Escolher(Cenario c, params string[] caminhos) =>
        Liberador.SelecionarArquivos(caminhos, c.Config, c.Ambiente.PastaDados);

    [Fact]
    public async Task EnviaConfereEApagaSoDepoisComPastasDoPrd()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "aula.mp4", "vídeo");
        var b = c.Pasta.Arquivo(c.Em(PastaPadrao.Documentos), "prova.pdf", "pdf");

        var r = await envio.EnviarAsync(Escolher(c, a, b));

        Assert.Null(r.MotivoAdiamento);
        Assert.Equal(2, r.Feitos.Count);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        var cubo = r.Cubo!;
        Assert.Equal(TipoCubo.Nuvem, cubo.Tipo);
        Assert.StartsWith("Cubo ", cubo.NomePacote);
        // CubeBin/Cubos com o pacote, CubeBin/Relatórios com relatório e registro, e o índice.
        var drive = c.Pasta.Caminho("drive", "CubeBin");
        Assert.Single(Directory.GetFiles(Path.Combine(drive, "Cubos"), "*.zip"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(drive, "Relatórios")).Length);
        var indice = JsonSerializer.Deserialize<List<Cubo>>(File.ReadAllText(Path.Combine(drive, "indice.json")), ConfiguracaoService.OpcoesJson)!;
        Assert.Equal(cubo.Id, Assert.Single(indice).Id);
        Assert.Equal(2, indice[0].Arquivos);
        Assert.Null(indice[0].CaminhoRegistro); // caminhos locais não vão para a nuvem
    }

    [Fact]
    public async Task SemInternetAdiaEMantemOsArquivos()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "a.zip");
        nuvem.FalharNoProximoEnvio = new HttpRequestException("rede caiu");

        var r = await envio.EnviarAsync(Escolher(c, a));
        Assert.Equal("sem conexão com a internet", r.MotivoAdiamento);
        Assert.True(File.Exists(a));
        Assert.True(r.Cubo!.EnvioAdiado);
        Assert.False(r.Cubo.PodeRestaurar);
        Assert.Empty(Directory.GetFiles(c.Ambiente.PastaEnviosPendentes)); // sem sobras
    }

    [Fact]
    public async Task PacoteQueChegaDiferenteNaoApagaNada()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "a.zip");
        nuvem.CorromperNoProximoEnvio = true;

        var r = await envio.EnviarAsync(Escolher(c, a));
        Assert.Equal("o pacote não chegou inteiro na nuvem", r.MotivoAdiamento);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public async Task DriveCheioAdia()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "a.zip", new string('x', 5000));
        nuvem.Cota = 10;

        var r = await envio.EnviarAsync(Escolher(c, a));
        Assert.Equal("o Google Drive está cheio", r.MotivoAdiamento);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public async Task TentarDeNovoDepoisDeAdiado()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "a.zip");
        nuvem.FalharNoProximoEnvio = new HttpRequestException();
        var adiado = (await envio.EnviarAsync(Escolher(c, a))).Cubo!;

        var r = await envio.TentarDeNovoAsync(adiado, c.Config);
        Assert.Null(r.MotivoAdiamento);
        Assert.False(File.Exists(a));
        Assert.Equal(adiado.Id, r.Cubo!.Id);
        Assert.False(c.Cubos.Obter(adiado.Id)!.EnvioAdiado);
    }

    [Fact]
    public async Task RestauraDaNuvemSemSobrescrever()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "foto.png", "original");
        var pasta = c.Pasta.Caminho("projetos velhos");
        c.Pasta.Arquivo(Path.Combine(pasta, "sub"), "x.txt", "x");
        var r = await envio.EnviarAsync(Escolher(c, a, pasta));
        Assert.False(Directory.Exists(pasta));

        File.WriteAllText(a, "novo arquivo com o mesmo nome");
        var restauracao = await envio.RestaurarAsync(r.Cubo!);

        Assert.Equal(2, restauracao.Restaurados);
        Assert.Equal("novo arquivo com o mesmo nome", File.ReadAllText(a));
        Assert.Equal("original", File.ReadAllText(Path.Combine(c.Em(PastaPadrao.Downloads), "Nuvem-foto.png")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(pasta, "sub", "x.txt")));
        Assert.True(c.Cubos.Obter(r.Cubo!.Id)!.Restaurado);
    }

    [Fact]
    public async Task OutroComputadorVeOsCubosPeloIndiceERestauraComORegistroDaNuvem()
    {
        var (c, nuvem, envio) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "nota.txt", "abc");
        await envio.EnviarAsync(Escolher(c, a));

        // "Outro computador": índice local vazio, sem o registro local.
        var outroAmbiente = new CubeBin.Core.Ambiente(c.Pasta.Caminho("outro-pc"));
        var outrosCubos = new CuboService(outroAmbiente.CaminhoCubos);
        var outroEnvio = new EnvioNuvem(outroAmbiente, outrosCubos, nuvem);
        Assert.Equal(1, await outroEnvio.SincronizarIndiceAsync());

        var cubo = Assert.Single(outrosCubos.Listar());
        Assert.Equal(1, cubo.PorCategoria["Documentos"]);
        var restauracao = await outroEnvio.RestaurarAsync(cubo);
        Assert.Equal(1, restauracao.Restaurados);
        Assert.Equal("abc", File.ReadAllText(a));
    }

    [Fact]
    public void ErrosViramFrasesSimples()
    {
        Assert.Equal("sem conexão com a internet", EnvioNuvem.ExplicarErro(new HttpRequestException()));
        Assert.Contains("expirou", EnvioNuvem.ExplicarErro(new Google.Apis.Auth.OAuth2.Responses.TokenResponseException(
            new Google.Apis.Auth.OAuth2.Responses.TokenErrorResponse { Error = "invalid_grant" })));
    }
}

public class AgenteTests
{
    [Fact]
    public void SugereDuplicadosTemporariosEGrandesParadosSemTocarEmNada()
    {
        using var c = new Cenario();
        c.Config.Agente.TamanhoMinimoGrandeMb = 1;
        var downloads = c.Em(PastaPadrao.Downloads);
        var original = c.Pasta.Arquivo(downloads, "foto.jpg", "mesmo conteúdo", TimeSpan.FromDays(10));
        var copia = c.Pasta.Arquivo(c.Em(PastaPadrao.Documentos), "foto (1).jpg", "mesmo conteúdo", TimeSpan.FromDays(2));
        var temporario = c.Pasta.Arquivo(downloads, "instalacao.log", "log", TimeSpan.FromDays(60));
        var grande = c.Pasta.Arquivo(downloads, "filme.mkv", new string('x', 2 * 1024 * 1024), TimeSpan.FromDays(400));
        File.SetLastAccessTime(grande, DateTime.Now.AddDays(-400));
        File.SetLastAccessTime(temporario, DateTime.Now.AddDays(-60));
        var projeto = Path.Combine(downloads, "Jogo");
        c.Pasta.Arquivo(projeto, "Jogo.c3p");
        c.Pasta.Arquivo(projeto, "build.log", "log", TimeSpan.FromDays(90));

        var r = new AgenteSugestoes(c.Config, c.Protecao).Analisar();

        var dup = Assert.Single(r.Sugestoes, s => s.Tipo == TipoSugestao.Duplicado);
        Assert.Equal(copia, dup.Caminho);
        Assert.Equal(original, dup.DuplicadoDe);
        Assert.Contains(r.Sugestoes, s => s.Tipo == TipoSugestao.TemporarioEsquecido && s.Caminho == temporario);
        Assert.Contains(r.Sugestoes, s => s.Tipo == TipoSugestao.GrandeParado && s.Caminho == grande);
        Assert.DoesNotContain(r.Sugestoes, s => s.Caminho.StartsWith(projeto)); // pasta de projeto
        Assert.True(File.Exists(copia) && File.Exists(temporario) && File.Exists(grande)); // nada sai sozinho
    }

    [Fact]
    public void AnalisaNoMaximoUmaVezPorDiaESoLigado()
    {
        var agente = new AgenteConfig();
        Assert.False(AgenteSugestoes.DeveAnalisar(agente, DateTime.Now));
        agente.Ligado = true;
        Assert.True(AgenteSugestoes.DeveAnalisar(agente, DateTime.Now));
        agente.UltimaAnalise = DateTime.Now.AddHours(-2);
        Assert.False(AgenteSugestoes.DeveAnalisar(agente, DateTime.Now));
    }

    [Fact]
    public void SugestoesGuardadasPerdemOQueJaSumiu()
    {
        using var teste = new PastaDeTeste();
        var existe = teste.Arquivo(teste.Caminho("x"), "a.tmp");
        var caminho = teste.Caminho("sugestoes.json");
        AgenteSugestoes.Salvar(caminho, new ResultadoAgente
        {
            Sugestoes = { new Sugestao { Caminho = existe }, new Sugestao { Caminho = teste.Caminho("sumiu.tmp") } },
        });
        Assert.Single(AgenteSugestoes.Carregar(caminho)!.Sugestoes);
    }
}
