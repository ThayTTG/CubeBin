using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.LiberarEspaco;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;
using CubeBin.Core.Registros;
using Xunit;

namespace CubeBin.Tests;

public class LixeiraLinuxTests
{
    [Fact]
    public void EnviaComTrashInfoERestauraNoLugar()
    {
        using var teste = new PastaDeTeste();
        var lixeira = new LixeiraLinux(teste.Caminho("Trash"));
        var arquivo = teste.Arquivo(teste.Caminho("docs"), "relatório final.pdf", "abc");

        var chave = lixeira.Enviar(arquivo);
        Assert.False(File.Exists(arquivo));
        Assert.True(lixeira.Existe(chave));
        var info = File.ReadAllText(Path.Combine(teste.Caminho("Trash"), "info", chave + ".trashinfo"));
        Assert.Contains("[Trash Info]", info);
        Assert.Contains("relat%C3%B3rio%20final.pdf", info);
        Assert.Contains("DeletionDate=", info);

        lixeira.Restaurar(chave, arquivo);
        Assert.Equal("abc", File.ReadAllText(arquivo));
        Assert.False(lixeira.Existe(chave));
    }

    [Fact]
    public void NomesRepetidosNaLixeiraNaoSeSobrescrevem()
    {
        using var teste = new PastaDeTeste();
        var lixeira = new LixeiraLinux(teste.Caminho("Trash"));
        var a = lixeira.Enviar(teste.Arquivo(teste.Caminho("a"), "x.txt", "1"));
        var b = lixeira.Enviar(teste.Arquivo(teste.Caminho("b"), "x.txt", "2"));
        Assert.NotEqual(a, b);
        Assert.True(lixeira.Existe(a) && lixeira.Existe(b));
    }

    [Fact]
    public void EnviaPastaInteira()
    {
        using var teste = new PastaDeTeste();
        var lixeira = new LixeiraLinux(teste.Caminho("Trash"));
        var pasta = teste.Caminho("fotos");
        teste.Arquivo(pasta, "a.png");
        var chave = lixeira.Enviar(pasta);
        Assert.False(Directory.Exists(pasta));
        lixeira.Restaurar(chave, pasta);
        Assert.True(File.Exists(Path.Combine(pasta, "a.png")));
    }
}

public class LixeiraWindowsTests
{
    [Fact]
    public void LeOArquivoDeControleDaLixeira()
    {
        using var teste = new PastaDeTeste();
        var data = new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc);
        var arquivo = teste.Caminho("$IABC123.pdf");
        File.WriteAllBytes(arquivo, LixeiraWindows.MontarArquivoI(@"C:\Users\Ana\Downloads\Aula ção.pdf", 1234, data));

        var lido = LixeiraWindows.LerArquivoI(arquivo);
        Assert.NotNull(lido);
        Assert.Equal(@"C:\Users\Ana\Downloads\Aula ção.pdf", lido!.Value.Caminho);
        Assert.Equal(data, lido.Value.ExcluidoEmUtc);
    }

    [Fact]
    public void ProcuraOItemMaisNovoDaquelecaminho()
    {
        using var teste = new PastaDeTeste();
        var recycle = teste.Caminho("$Recycle.Bin", "S-1-5-21");
        Directory.CreateDirectory(recycle);
        var original = @"C:\Users\Ana\Downloads\nota.txt";
        var velho = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var novo = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllBytes(Path.Combine(recycle, "$IAAA.txt"), LixeiraWindows.MontarArquivoI(original, 1, velho));
        File.WriteAllText(Path.Combine(recycle, "$RAAA.txt"), "velho");
        File.WriteAllBytes(Path.Combine(recycle, "$IBBB.txt"), LixeiraWindows.MontarArquivoI(original, 1, novo));
        File.WriteAllText(Path.Combine(recycle, "$RBBB.txt"), "novo");

        var item = LixeiraWindows.Procurar(Path.GetDirectoryName(recycle)!, original, velho.AddDays(1));
        Assert.NotNull(item);
        Assert.EndsWith("$RBBB.txt", item!.CaminhoR);
    }
}

public class LiberadorTests
{
    private static (Cenario c, Liberador liberador, LixeiraLinux lixeira) Montar()
    {
        var c = new Cenario();
        var lixeira = new LixeiraLinux(c.Pasta.Caminho("Trash"));
        return (c, new Liberador(c.Ambiente, c.Cubos, lixeira), lixeira);
    }

    [Fact]
    public void EsvaziarPastaPadraoTiraOConteudoMasNuncaAPasta()
    {
        var (c, liberador, _) = Montar();
        using var _c = c;
        var downloads = c.Em(PastaPadrao.Downloads);
        c.Pasta.Arquivo(downloads, "a.zip");
        c.Pasta.Arquivo(Path.Combine(downloads, "viagem"), "b.png");
        c.Pasta.Arquivo(downloads, ".oculto");
        var projeto = Path.Combine(downloads, "App");
        c.Pasta.Arquivo(projeto, "App.sln");

        var selecao = Liberador.SelecionarConteudoDaPasta(c.Config.ObterPasta("downloads")!, c.Config, c.EmUso);
        Assert.Equal(2, selecao.Itens.Count);           // a.zip e a pasta "viagem"
        Assert.Contains(projeto, selecao.PastasDeProjeto); // projeto fica de fora

        var r = liberador.Executar(selecao, DestinoLiberar.Lixeira);
        Assert.Equal(2, r.Feitos.Count);
        Assert.True(Directory.Exists(downloads));         // a pasta padrão continua
        Assert.True(File.Exists(Path.Combine(downloads, ".oculto")));
        Assert.True(Directory.Exists(projeto));
        Assert.Equal(TipoCubo.Lixeira, r.Cubo!.Tipo);
        Assert.Equal(2, r.Cubo.Arquivos);                 // a.zip + b.png dentro da pasta
    }

    [Fact]
    public void RestaurarDaLixeiraDevolveTudo()
    {
        var (c, liberador, lixeira) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Em(PastaPadrao.Downloads), "a.zip", "conteúdo a");
        var selecao = Liberador.SelecionarConteudoDaPasta(c.Config.ObterPasta("downloads")!, c.Config, c.EmUso);
        var r = liberador.Executar(selecao, DestinoLiberar.Lixeira);
        Assert.False(File.Exists(a));

        var restauracao = new Restaurador(c.Ambiente, c.Cubos, lixeira).Restaurar(r.Cubo!);
        Assert.Equal(1, restauracao.Restaurados);
        Assert.Equal("conteúdo a", File.ReadAllText(a));
    }

    [Fact]
    public void ApagarDeVezApagaENaoTemRestauracao()
    {
        var (c, liberador, lixeira) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Pasta.Caminho("soltos"), "lixo.tmp");
        var selecao = Liberador.SelecionarArquivos(new[] { a }, c.Config);
        var r = liberador.Executar(selecao, DestinoLiberar.ApagarDeVez);
        Assert.False(File.Exists(a));
        Assert.False(r.Cubo!.PodeRestaurar);
        var restauracao = new Restaurador(c.Ambiente, c.Cubos, lixeira).Restaurar(r.Cubo);
        Assert.NotEmpty(restauracao.Falhas);
        Assert.Contains("Apagado de vez", File.ReadAllText(r.CaminhoRelatorio!));
    }

    [Fact]
    public void EscolherArquivosRecusaPastasPadraoEAPastaDoCubeBin()
    {
        var (c, _, _) = Montar();
        using var _c = c;
        var arquivo = c.Pasta.Arquivo(c.Em(PastaPadrao.Documentos), "ok.txt");
        var selecao = Liberador.SelecionarArquivos(
            new[] { c.Em(PastaPadrao.Documentos), c.Pasta.Caminho("home"), c.Ambiente.PastaDados, arquivo },
            c.Config, c.Ambiente.PastaDados);

        Assert.Equal(arquivo, Assert.Single(selecao.Itens).Caminho);
        Assert.Equal(3, selecao.Recusados.Count);
        Assert.Contains(selecao.Recusados, r => r.Motivo.Contains("nunca apagadas"));
    }

    [Fact]
    public void ArquivoDentroDePastaEscolhidaSaiJuntoComEla()
    {
        var (c, _, _) = Montar();
        using var _c = c;
        var pasta = c.Pasta.Caminho("projeto velho");
        var dentro = c.Pasta.Arquivo(pasta, "x.txt");
        var selecao = Liberador.SelecionarArquivos(new[] { pasta, dentro }, c.Config);
        Assert.True(Assert.Single(selecao.Itens).EhPasta);
    }

    [Fact]
    public void RegistroAnotaAChaveDaLixeira()
    {
        var (c, liberador, _) = Montar();
        using var _c = c;
        var a = c.Pasta.Arquivo(c.Pasta.Caminho("x"), "a.txt");
        var r = liberador.Executar(Liberador.SelecionarArquivos(new[] { a }, c.Config), DestinoLiberar.Lixeira);
        var item = Assert.Single(LeitorRegistro.LerItens(r.Cubo!.CaminhoRegistro!));
        Assert.Equal(TipoAcao.Lixeira, item.Acao);
        Assert.False(string.IsNullOrEmpty(item.Extra));
    }
}
