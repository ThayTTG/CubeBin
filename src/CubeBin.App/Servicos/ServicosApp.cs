using CubeBin.App.Interface;
using CubeBin.Core;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.LiberarEspaco;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Servicos;

/// <summary>
/// Os serviços do Core que as telas usam, criados uma vez quando o app abre. Também garante que só
/// uma execução (manual ou automática) mexa nos arquivos por vez.
/// </summary>
public class ServicosApp
{
    private int _ocupado;

    public Ambiente Ambiente { get; }
    public ConfiguracaoService Config { get; }
    public CuboService Cubos { get; }
    public ILixeira? Lixeira { get; set; }
    public IAvisos Avisos { get; }

    public ServicosApp(Ambiente ambiente, IAvisos avisos)
    {
        Ambiente = ambiente;
        Avisos = avisos;
        Config = new ConfiguracaoService(ambiente.CaminhoConfiguracoes);
        Cubos = new CuboService(ambiente.CaminhoCubos);
        Lixeira = Lixeiras.DoSistema();
    }

    /// <summary>Há uma conta do Google Drive conectada e pronta para receber os Cubos?</summary>
    public virtual bool NuvemConectada => false;

    public Protecao NovaProtecao() => new(Config.Carregar().Protecoes);
    public Planejador NovoPlanejador() => new(Config.Carregar(), NovaProtecao());
    public Organizador NovoOrganizador() => new(Ambiente, Cubos, NovaProtecao());
    public Restaurador NovoRestaurador() => new(Ambiente, Cubos, Lixeira);
    public Liberador NovoLiberador() => new(Ambiente, Cubos, Lixeira);

    /// <summary>Tenta "pegar a vez" de mexer nos arquivos. Devolve false se já há outra execução.</summary>
    public bool TentarOcupar() => Interlocked.CompareExchange(ref _ocupado, 1, 0) == 0;

    public void Liberar() => Interlocked.Exchange(ref _ocupado, 0);

    public bool Ocupado => Volatile.Read(ref _ocupado) == 1;

    /// <summary>Envia a seleção para a nuvem (versão 2.0).</summary>
    public virtual Task<ResultadoLiberar> EnviarParaNuvemAsync(SelecaoLiberar selecao, IProgress<Progresso> progresso, CancellationToken cancelar) =>
        throw new InvalidOperationException("Nenhuma conta do Google Drive conectada.");

    /// <summary>Restaura um Cubo (Desfazer, tela de Cubos) e mostra o resultado numa mensagem.</summary>
    public virtual async Task<ResultadoRestauracao?> RestaurarAsync(Cubo cubo)
    {
        if (!TentarOcupar())
        {
            Dialogo.Show("O CubeBin está mexendo em arquivos agora. Tente de novo em instantes.", "Restaurar");
            return null;
        }
        try
        {
            var r = await RestaurarSemMensagemAsync(cubo);
            MostrarResultadoDaRestauracao(r);
            return r;
        }
        catch (Exception ex)
        {
            App.RegistrarCrash(ex);
            Dialogo.Show($"Não foi possível restaurar: {ex.Message}", "Restaurar", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            Liberar();
        }
    }

    /// <summary>A restauração em si (a nuvem, na 2.0, baixa o pacote antes).</summary>
    protected virtual Task<ResultadoRestauracao> RestaurarSemMensagemAsync(Cubo cubo) =>
        Task.Run(() => NovoRestaurador().Restaurar(cubo));

    public static void MostrarResultadoDaRestauracao(ResultadoRestauracao r)
    {
        var texto = r.Restaurados == 1 ? "1 item voltou para o lugar de origem." : $"{r.Restaurados} itens voltaram para o lugar de origem.";
        if (r.Renomeados.Count > 0)
            texto += $"\n\n{r.Renomeados.Count} já tinha(m) um arquivo novo com o mesmo nome no lugar, então voltaram com outro nome:\n" +
                     string.Join("\n", r.Renomeados.Take(8).Select(Path.GetFileName));
        if (r.NaoEncontrados.Count > 0)
            texto += $"\n\n{r.NaoEncontrados.Count} não estavam mais onde o CubeBin deixou (foram movidos, apagados ou a Lixeira foi esvaziada).";
        if (r.Falhas.Count > 0)
            texto += "\n\nNão foi possível devolver:\n" + string.Join("\n", r.Falhas.Take(8).Select(f => $"• {f.Nome}: {f.Motivo}"));
        Dialogo.Show(texto, "Restaurar", MessageBoxButton.OK, r.Falhas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>Anota as extensões novas sem pasta para a pessoa decidir na tela de regras.</summary>
    public void AnotarSemPasta(IEnumerable<string> extensoes)
    {
        var config = Config.Carregar();
        if (ConfiguracaoService.AnotarSemPasta(config, extensoes)) Config.Salvar(config);
    }
}
