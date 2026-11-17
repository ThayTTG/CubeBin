using CubeBin.Core;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;
using CubeBin.Core.Registros;
using CubeBin.Core.Relatorios;
using Xunit;

namespace CubeBin.Tests;

/// <summary>Monta um CubeBin inteiro dentro da pasta de teste.</summary>
internal sealed class Cenario : IDisposable
{
    public PastaDeTeste Pasta { get; } = new();
    public Ambiente Ambiente { get; }
    public Configuracoes Config { get; }
    public CuboService Cubos { get; }
    public FakeEmUso EmUso { get; } = new();

    public Cenario()
    {
        Ambiente = new Ambiente(Pasta.Caminho("dados"));
        Config = RegrasPadrao.CriarConfiguracaoInicial();
        Cubos = new CuboService(Ambiente.CaminhoCubos);
        foreach (var p in PastasDoSistema.Todas) Directory.CreateDirectory(Pasta.Sistema(p));
    }

    public string Em(PastaPadrao pasta) => Pasta.Sistema(pasta);
    public Protecao Protecao => new(Config.Protecoes, EmUso);
    public PlanoOrganizacao Planejar() => new Planejador(Config, Protecao).Planejar();
    public ResultadoOrganizacao Organizar(string modo = "manual") =>
        new Organizador(Ambiente, Cubos, Protecao).Executar(Planejar(), modo);

    public void Dispose() => Pasta.Dispose();
}

internal sealed class FakeEmUso : IArquivosEmUso
{
    public HashSet<string> Abertos { get; } = new();
    public bool EstaEmUso(string caminho) => Abertos.Contains(caminho);
}

public class ConfiguracaoTests
{
    [Fact]
    public void JaVemComAsRegrasPadraoSemPrecisarDefinirExtensao()
    {
        var config = RegrasPadrao.CriarConfiguracaoInicial();
        Assert.Equal(6, config.Pastas.Count(p => p.EhPadrao));
        Assert.Equal("imagens", config.RegraDaExtensao(".JPG")!.PastaId);
        Assert.Equal("videos", config.RegraDaExtensao("mp4")!.PastaId);
        Assert.Equal("musicas", config.RegraDaExtensao(".mp3")!.PastaId);
        Assert.Equal("documentos", config.RegraDaExtensao(".pdf")!.PastaId);
        Assert.False(config.Automacao.Ligada); // desligado na primeira vez
        Assert.All(config.Pastas, p => Assert.False(p.PodeRemover));
    }

    [Fact]
    public void JsonEditadoAMaoSemPastaPadraoGanhaElaDeVolta()
    {
        using var teste = new PastaDeTeste();
        var caminho = teste.Caminho("config.json");
        File.WriteAllText(caminho, """{ "pastas": [ { "id": "x", "nome": "Minha", "caminho": "/tmp/minha" } ], "regras": [] }""");
        var config = new ConfiguracaoService(caminho).Carregar();
        Assert.Equal(7, config.Pastas.Count);
        Assert.Contains(config.Pastas, p => p.Padrao == PastaPadrao.Documentos);
    }

    [Fact]
    public void JsonSemRegrasUsaAsRegrasPadraoMasListaVaziaERespeitada()
    {
        using var teste = new PastaDeTeste();
        var caminho = teste.Caminho("config.json");
        File.WriteAllText(caminho, """{ "automacao": { "ligada": true } }""");
        var config = new ConfiguracaoService(caminho).Carregar();
        Assert.True(config.Automacao.Ligada);
        Assert.NotNull(config.RegraDaExtensao(".jpg"));

        var outro = teste.Caminho("config2.json");
        File.WriteAllText(outro, """{ "regras": [] }""");
        Assert.Null(new ConfiguracaoService(outro).Carregar().RegraDaExtensao(".jpg"));
    }

    [Fact]
    public void JsonQuebradoVoltaAoPadraoGuardaCopiaEAvisa()
    {
        using var teste = new PastaDeTeste();
        var caminho = teste.Caminho("config.json");
        File.WriteAllText(caminho, "{ isso não é json");
        var servico = new ConfiguracaoService(caminho);
        var config = servico.Carregar();
        Assert.NotNull(servico.AvisoDeCarga);
        Assert.NotNull(config.RegraDaExtensao(".png"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(caminho)!, "config.json.quebrado-*"));
    }

    [Fact]
    public void SalvaAcentosLegiveisNoJson()
    {
        using var teste = new PastaDeTeste();
        var caminho = teste.Caminho("config.json");
        var servico = new ConfiguracaoService(caminho);
        servico.Carregar();
        Assert.Contains("Área de Trabalho", File.ReadAllText(caminho));
    }

    [Fact]
    public void ArrastarExtensaoParaOutraPastaTiraDaAnterior()
    {
        var config = RegrasPadrao.CriarConfiguracaoInicial();
        ConfiguracaoService.AtribuirExtensao(config, ".pdf", "downloads");
        Assert.Equal("downloads", config.RegraDaExtensao(".pdf")!.PastaId);
        Assert.Single(config.Regras.SelectMany(r => r.Extensoes), e => e == ".pdf");
    }

    [Fact]
    public void DeixarOndeEstaTiraDaRegraENaoVoltaParaSemPasta()
    {
        var config = RegrasPadrao.CriarConfiguracaoInicial();
        ConfiguracaoService.IgnorarExtensao(config, "PNG");
        Assert.Null(config.RegraDaExtensao(".png"));
        Assert.Contains(".png", config.ExtensoesIgnoradas);
        Assert.False(ConfiguracaoService.AnotarSemPasta(config, new[] { ".png" }));
        Assert.True(ConfiguracaoService.AnotarSemPasta(config, new[] { ".7z" }));
        Assert.Contains(".7z", config.ExtensoesSemPasta);

        // Arrastar de volta para uma pasta tira da lista "deixar onde está".
        ConfiguracaoService.AtribuirExtensao(config, ".png", "imagens");
        Assert.DoesNotContain(".png", config.ExtensoesIgnoradas);
    }

    [Fact]
    public void ExtensaoIgnoradaFicaOndeEstaNaPrevia()
    {
        using var c = new Cenario();
        ConfiguracaoService.IgnorarExtensao(c.Config, ".png");
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "x.png");
        var plano = c.Planejar();
        Assert.Empty(plano.Movimentos);
        Assert.Empty(plano.SemPasta);
    }

    [Fact]
    public void PreviewDoCuboUsaSingularEPlural()
    {
        var cubo = new Cubo { PorCategoria = new() { ["Imagens"] = 4, ["Vídeos"] = 1 } };
        Assert.Equal("4 imagens · 1 vídeo", cubo.ResumoPorTipo);
    }

    [Fact]
    public void PastaPadraoNuncaERemovidaSoPersonalizada()
    {
        using var teste = new PastaDeTeste();
        var config = RegrasPadrao.CriarConfiguracaoInicial();
        Assert.False(ConfiguracaoService.RemoverPastaPersonalizada(config, "documentos"));

        var nova = ConfiguracaoService.CriarPastaPersonalizada(config, "Projetos Escola", teste.Caminho("Escola"), monitorar: false);
        ConfiguracaoService.AtribuirExtensao(config, ".zip", nova.Id);
        Assert.True(ConfiguracaoService.RemoverPastaPersonalizada(config, nova.Id));
        Assert.Contains(".zip", config.ExtensoesSemPasta);
        Assert.True(Directory.Exists(teste.Caminho("Escola"))); // o disco não é tocado
    }

    [Fact]
    public void RecusaPastasDoSistemaComoPersonalizadas()
    {
        var config = RegrasPadrao.CriarConfiguracaoInicial();
        Assert.NotNull(ConfiguracaoService.ValidarPastaPersonalizada(config, Path.GetPathRoot(Path.GetTempPath())!));
        if (OperatingSystem.IsLinux())
            Assert.NotNull(ConfiguracaoService.ValidarPastaPersonalizada(config, "/usr/share"));
    }
}

public class PlanejadorTests
{
    [Fact]
    public void ImagemDeDownloadsVaiParaImagensEDocumentoJaNoLugarFica()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "foto.jpg");
        c.Pasta.Arquivo(c.Em(PastaPadrao.Documentos), "contrato.pdf");

        var plano = c.Planejar();
        var mov = Assert.Single(plano.Movimentos);
        Assert.Equal(Path.Combine(c.Em(PastaPadrao.Imagens), "foto.jpg"), mov.Destino);
        Assert.Equal("Imagens", mov.PastaDestinoNome);
    }

    [Fact]
    public void ExtensaoSemPastaEhListadaEFicaOndeEsta()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "pacote.zip");
        var plano = c.Planejar();
        Assert.Empty(plano.Movimentos);
        Assert.Equal(new[] { ".zip" }, plano.ExtensoesSemPasta);
    }

    [Theory]
    [InlineData("atalho.lnk", MotivoPulado.Atalho)]
    [InlineData("app.desktop", MotivoPulado.Atalho)]
    [InlineData("jogo.exe", MotivoPulado.Executavel)]
    [InlineData("filme.mp4.part", MotivoPulado.DownloadIncompleto)]
    [InlineData("video.crdownload", MotivoPulado.DownloadIncompleto)]
    public void ProtecoesDeArquivo(string nome, MotivoPulado motivo)
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.AreaDeTrabalho), nome);
        var plano = c.Planejar();
        Assert.Empty(plano.Movimentos);
        Assert.Equal(motivo, Assert.Single(plano.Pulados).Motivo);
    }

    [Fact]
    public void ArquivoAlteradoHaPoucoMinutosFicaParaDepois()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "novo.png", idade: TimeSpan.FromMinutes(1));
        var plano = c.Planejar();
        Assert.Empty(plano.Movimentos);
        Assert.Equal(MotivoPulado.AlteradoRecentemente, Assert.Single(plano.Pulados).Motivo);
    }

    [Fact]
    public void ArquivoEmUsoFicaOndeEsta()
    {
        using var c = new Cenario();
        var arquivo = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "aberto.docx");
        c.EmUso.Abertos.Add(arquivo);
        Assert.Equal(MotivoPulado.EmUso, Assert.Single(c.Planejar().Pulados).Motivo);
    }

    [Fact]
    public void OcultosNaoAparecemNemSaoMovidos()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), ".escondido.png");
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "desktop.ini");
        var plano = c.Planejar();
        Assert.Empty(plano.Movimentos);
        Assert.Empty(plano.Pulados);
    }

    [Theory]
    [InlineData("jogo.c3p")]
    [InlineData("App.sln")]
    [InlineData("App.csproj")]
    [InlineData("package.json")]
    public void PastaMonitoradaQueEhProjetoEhPuladaInteira(string marcador)
    {
        using var c = new Cenario();
        var pasta = c.Pasta.Caminho("MeuProjeto");
        c.Pasta.Arquivo(pasta, marcador);
        c.Pasta.Arquivo(pasta, "sprite.png");
        ConfiguracaoService.CriarPastaPersonalizada(c.Config, "Projeto", pasta, monitorar: true);

        var plano = c.Planejar();
        Assert.Empty(plano.Movimentos);
        Assert.Contains(pasta, plano.PastasDeProjeto);
    }

    [Fact]
    public void SubpastasDeProjetoGitEUnityNuncaSaoTocadas()
    {
        using var c = new Cenario();
        var downloads = c.Em(PastaPadrao.Downloads);
        c.Config.ObterPasta("downloads")!.OrganizarSubpastas = true;

        var git = Path.Combine(downloads, "repo");
        Directory.CreateDirectory(Path.Combine(git, ".git"));
        c.Pasta.Arquivo(git, "logo.png");

        var unity = Path.Combine(downloads, "Jogo");
        Directory.CreateDirectory(Path.Combine(unity, "Assets"));
        Directory.CreateDirectory(Path.Combine(unity, "ProjectSettings"));
        c.Pasta.Arquivo(unity, "capa.png");

        // Só "Assets" (sem ProjectSettings) não é projeto Unity.
        var soAssets = Path.Combine(downloads, "Fotos");
        Directory.CreateDirectory(Path.Combine(soAssets, "Assets"));
        c.Pasta.Arquivo(soAssets, "praia.png");

        var plano = c.Planejar();
        var mov = Assert.Single(plano.Movimentos);
        Assert.EndsWith("praia.png", mov.Origem);
        Assert.Contains(git, plano.PastasDeProjeto);
        Assert.Contains(unity, plano.PastasDeProjeto);
    }

    [Fact]
    public void SemSubpastasLigadasNaoDesceNasPastas()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(Path.Combine(c.Em(PastaPadrao.Downloads), "viagem"), "praia.png");
        Assert.Empty(c.Planejar().Movimentos);
    }

    [Fact]
    public void ConflitoDeNomeGanhaPrefixoDaPastaDeOrigem()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Documentos), "Texto.pdf", "o que já estava");
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "Texto.pdf", "de downloads");
        c.Pasta.Arquivo(c.Em(PastaPadrao.AreaDeTrabalho), "Texto.pdf", "da área de trabalho");

        var destinos = c.Planejar().Movimentos.Select(m => Path.GetFileName(m.Destino)).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Desk-Texto.pdf", "Down-Texto.pdf" }, destinos);
    }

    [Fact]
    public void ConflitoRepetidoGanhaNumero()
    {
        using var teste = new PastaDeTeste();
        var pasta = teste.Caminho("destino");
        teste.Arquivo(pasta, "a.txt");
        teste.Arquivo(pasta, "Doc-a.txt");
        Assert.Equal(Path.Combine(pasta, "Doc-a (2).txt"), NomeSemConflito.Resolver(pasta, "a.txt", "Doc"));
    }

    [Fact]
    public void OneDriveComMuitosArquivosGeraAvisoEFicaForaDoAutomatico()
    {
        using var c = new Cenario();
        var pasta = c.Pasta.Caminho("OneDrive - Escola", "Docs");
        ConfiguracaoService.CriarPastaPersonalizada(c.Config, "Docs OneDrive", pasta, monitorar: true);
        c.Config.Protecoes.LimiteAvisoOneDriveArquivos = 3;
        for (var i = 0; i < 3; i++) c.Pasta.Arquivo(pasta, $"foto{i}.png");
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "outra.png");

        var plano = c.Planejar();
        Assert.Contains("Docs OneDrive", plano.AvisosOneDrive);

        var auto = OrganizacaoAutomatica.FiltrarParaAutomatico(plano);
        Assert.Single(auto.Movimentos);
        Assert.Equal(3, auto.Pulados.Count(p => p.Motivo == MotivoPulado.OneDriveAguardandoConfirmacao));
    }
}

public class OrganizadorTests
{
    [Fact]
    public void MoveDiretoGravaRegistroRelatorioECubo()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "foto, com vírgula \"e aspas\".jpg");
        c.Pasta.Arquivo(c.Em(PastaPadrao.AreaDeTrabalho), "música.mp3");

        var r = c.Organizar();

        Assert.Equal(2, r.Movidos.Count);
        Assert.True(File.Exists(Path.Combine(c.Em(PastaPadrao.Imagens), "foto, com vírgula \"e aspas\".jpg")));
        Assert.True(File.Exists(Path.Combine(c.Em(PastaPadrao.Musicas), "música.mp3")));
        Assert.Empty(Directory.GetFiles(c.Em(PastaPadrao.Downloads)));

        // Registro: sem pasta temporária, com origem de cada arquivo e acentos intactos.
        var itens = LeitorRegistro.LerItens(r.CaminhoRegistro!);
        Assert.Equal(2, itens.Count);
        Assert.All(itens, i => Assert.True(i.Ok));
        Assert.Contains(itens, i => i.Origem.EndsWith("música.mp3"));

        // Relatório com o nome do PRD.
        Assert.Equal($"Relatório {DateTime.Now:yyyy-MM-dd}.html", Path.GetFileName(r.CaminhoRelatorio));

        // Cubo com o preview por tipo.
        var cubo = Assert.Single(c.Cubos.Listar());
        Assert.Equal(2, cubo.Arquivos);
        Assert.Equal(1, cubo.PorCategoria["Imagens"]);
        Assert.Equal(1, cubo.PorCategoria["Músicas"]);
    }

    [Fact]
    public void PastaDeDestinoQueNaoExisteECriadaERegistrada()
    {
        using var c = new Cenario();
        Directory.Delete(c.Em(PastaPadrao.Videos));
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "aula.mp4");

        var plano = c.Planejar();
        Assert.Contains(c.Em(PastaPadrao.Videos), plano.DestinosACriar);
        var r = new Organizador(c.Ambiente, c.Cubos, c.Protecao).Executar(plano, "manual");
        Assert.Contains(c.Em(PastaPadrao.Videos), r.PastasCriadas);
        Assert.Contains("foi criada", File.ReadAllText(r.CaminhoRelatorio!));
    }

    [Fact]
    public void ArquivoAbertoDepoisDaPreviaNaoEMovido()
    {
        using var c = new Cenario();
        var arquivo = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "planilha.xlsx");
        var plano = c.Planejar();
        c.EmUso.Abertos.Add(arquivo);

        var r = new Organizador(c.Ambiente, c.Cubos, c.Protecao).Executar(plano, "manual");
        Assert.Empty(r.Movidos);
        Assert.True(File.Exists(arquivo));
    }

    [Fact]
    public void NomeOcupadoEntreAPreviaEAExecucaoNaoSobrescreve()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "nota.txt", "novo");
        var plano = c.Planejar();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Documentos), "nota.txt", "antigo");

        new Organizador(c.Ambiente, c.Cubos, c.Protecao).Executar(plano, "manual");
        Assert.Equal("antigo", File.ReadAllText(Path.Combine(c.Em(PastaPadrao.Documentos), "nota.txt")));
        Assert.Equal("novo", File.ReadAllText(Path.Combine(c.Em(PastaPadrao.Documentos), "Down-nota.txt")));
    }

    [Fact]
    public void CancelarNoMeioParaENaoPerdeNada()
    {
        using var c = new Cenario();
        for (var i = 0; i < 5; i++) c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), $"f{i}.png");
        var plano = c.Planejar();
        using var cancelar = new CancellationTokenSource();
        var progresso = new ProgressoSincrono(p => { if (p.Feitos == 2) cancelar.Cancel(); });

        var r = new Organizador(c.Ambiente, c.Cubos, c.Protecao).Executar(plano, "manual", progresso, cancelar.Token);
        Assert.True(r.Cancelado);
        Assert.Equal(2, r.Movidos.Count);
        Assert.Equal(3, Directory.GetFiles(c.Em(PastaPadrao.Downloads)).Length);
    }

    [Fact]
    public void NadaParaOrganizarNaoCriaCubo()
    {
        using var c = new Cenario();
        var r = c.Organizar();
        Assert.True(r.NadaAFazer);
        Assert.Empty(c.Cubos.Listar());
    }
}

public class DesfazerTests
{
    [Fact]
    public void DesfazerDevolveTudoAoLugar()
    {
        using var c = new Cenario();
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "a.png");
        var b = c.Pasta.Arquivo(c.Em(PastaPadrao.AreaDeTrabalho), "b.pdf");
        var r = c.Organizar();

        var restauracao = new Restaurador(c.Ambiente, c.Cubos).Restaurar(r.Cubo!);
        Assert.Equal(2, restauracao.Restaurados);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.True(c.Cubos.Obter(r.Cubo!.Id)!.Restaurado);

        // O registro também anota a devolução.
        Assert.All(LeitorRegistro.LerItens(r.CaminhoRegistro!), i => Assert.True(i.Restaurado));
    }

    [Fact]
    public void ArquivoNovoNaOrigemNaoESobrescritoNaRestauracao()
    {
        using var c = new Cenario();
        var original = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "foto.png", "original");
        var r = c.Organizar();
        File.WriteAllText(original, "um arquivo novo com o mesmo nome");

        var restauracao = new Restaurador(c.Ambiente, c.Cubos).Restaurar(r.Cubo!);
        Assert.Equal("um arquivo novo com o mesmo nome", File.ReadAllText(original));
        var devolvido = Path.Combine(c.Em(PastaPadrao.Downloads), "Img-foto.png");
        Assert.Equal("original", File.ReadAllText(devolvido));
        Assert.Contains(devolvido, restauracao.Renomeados);
    }

    [Fact]
    public void RegistroCortadoNoMeioContinuaServindoParaDesfazer()
    {
        // Simula o app travando: a ação foi anotada e o arquivo movido, mas o resultado não.
        using var c = new Cenario();
        var origem = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "x.png");
        var destino = Path.Combine(c.Em(PastaPadrao.Imagens), "x.png");
        var cubo = new Cubo { Tipo = TipoCubo.Organizado };
        string caminho;
        using (var registro = RegistroExecucao.Criar(c.Ambiente.PastaRegistros, "organizar", "manual", cubo.Id))
        {
            registro.AnotarAcao(TipoAcao.Mover, origem, destino, "Downloads", "Imagens", 8);
            caminho = registro.Caminho;
        }
        File.Move(origem, destino);
        File.AppendAllText(caminho, "{\"tipo\":\"resultado\",\"n\":1,\"o"); // linha cortada
        cubo.CaminhoRegistro = caminho;
        c.Cubos.Salvar(cubo);

        var restauracao = new Restaurador(c.Ambiente, c.Cubos).Restaurar(cubo);
        Assert.Equal(1, restauracao.Restaurados);
        Assert.True(File.Exists(origem));
    }

    [Fact]
    public void ArquivoQueAPessoaApagouDepoisEhInformadoSemErro()
    {
        using var c = new Cenario();
        c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "sumiu.png");
        var r = c.Organizar();
        File.Delete(Path.Combine(c.Em(PastaPadrao.Imagens), "sumiu.png"));

        var restauracao = new Restaurador(c.Ambiente, c.Cubos).Restaurar(r.Cubo!);
        Assert.Empty(restauracao.Falhas);
        Assert.Single(restauracao.NaoEncontrados);
    }
}

public class RelatorioTests
{
    [Fact]
    public void RelatorioTemResumoTabelaFalhaEmVermelhoEBuscaOffline()
    {
        var html = GeradorRelatorio.Montar(new DadosRelatorio
        {
            Linhas =
            {
                new LinhaRelatorio { Arquivo = "<script>.png", PastaOrigem = "Downloads", Destino = "Imagens", Tamanho = 2048 },
                new LinhaRelatorio { Arquivo = "travado.docx", PastaOrigem = "Documentos", Falhou = true, Motivo = "em uso" },
            },
        }, "Relatório 2026-11-17");

        Assert.Contains("<title>Relatório 2026-11-17</title>", html);
        Assert.Contains("&lt;script&gt;.png", html);           // escapado
        Assert.Contains("class=\"falha\"", html);              // falha destacada
        Assert.Contains("id=\"busca\"", html);                 // caixa de busca
        Assert.DoesNotContain("http://", html.Replace("http://www.w3.org", ""));
        Assert.DoesNotContain("https://", html);               // nada da internet
        Assert.Contains("2,0 KB", html);
    }

    [Fact]
    public void SegundoRelatorioDoDiaGanhaNumero()
    {
        using var teste = new PastaDeTeste();
        var pasta = teste.Caminho("rel");
        var dados = new DadosRelatorio();
        var a = GeradorRelatorio.Gerar(dados, pasta);
        var b = GeradorRelatorio.Gerar(dados, pasta);
        Assert.NotEqual(a, b);
        Assert.EndsWith(" (2).html", b);
    }
}

public class AutomaticoTests
{
    private static Automacao Ligada() => new() { Ligada = true, MinutosDeAusencia = 10, HorasEntreExecucoes = 4 };

    [Fact]
    public void SoTentaComAPessoaAusenteHaTMinutos()
    {
        var agora = DateTime.Now;
        Assert.False(OrganizacaoAutomatica.DeveTentar(Ligada(), TimeSpan.FromMinutes(5), agora, false));
        Assert.True(OrganizacaoAutomatica.DeveTentar(Ligada(), TimeSpan.FromMinutes(11), agora, false));
    }

    [Fact]
    public void DesligadoNuncaTenta()
    {
        Assert.False(OrganizacaoAutomatica.DeveTentar(new Automacao(), TimeSpan.FromHours(3), DateTime.Now, false));
    }

    [Fact]
    public void UmaVezPorAusenciaERespeitandoOIntervalo()
    {
        var a = Ligada();
        var agora = DateTime.Now;
        Assert.False(OrganizacaoAutomatica.DeveTentar(a, TimeSpan.FromHours(1), agora, jaTentouNestaAusencia: true));
        a.UltimaExecucaoAutomatica = agora.AddHours(-1);
        Assert.False(OrganizacaoAutomatica.DeveTentar(a, TimeSpan.FromHours(1), agora, false));
        a.UltimaExecucaoAutomatica = agora.AddHours(-5);
        Assert.True(OrganizacaoAutomatica.DeveTentar(a, TimeSpan.FromHours(1), agora, false));
    }

    [Fact]
    public void NotificacaoDeFimListaOQueNaoPodeSerMovido()
    {
        var r = new ResultadoOrganizacao();
        r.Falhas.Add(new FalhaArquivo { Caminho = "/x/relatorio.docx", Motivo = "em uso" });
        var (titulo, msg) = OrganizacaoAutomatica.TextoDeFim(r);
        Assert.Contains("não puderam ser movidos", titulo);
        Assert.Contains("relatorio.docx: em uso", msg);
    }
}

/// <summary>IProgress que chama na hora (o Progress&lt;T&gt; do .NET chama depois, em outra thread).</summary>
internal sealed class ProgressoSincrono : IProgress<Progresso>
{
    private readonly Action<Progresso> _acao;
    public ProgressoSincrono(Action<Progresso> acao) => _acao = acao;
    public void Report(Progresso value) => _acao(value);
}
