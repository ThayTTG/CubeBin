using CubeBin.Core;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Servicos;

/// <summary>
/// Os serviços do Core que as telas usam, criados uma vez quando o app abre. Também garante que só
/// uma execução (manual ou automática) mexa nos arquivos por vez.
/// </summary>
public sealed class ServicosApp
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
    }

    public Protecao NovaProtecao() => new(Config.Carregar().Protecoes);
    public Planejador NovoPlanejador() => new(Config.Carregar(), NovaProtecao());
    public Organizador NovoOrganizador() => new(Ambiente, Cubos, NovaProtecao());
    public Restaurador NovoRestaurador() => new(Ambiente, Cubos, Lixeira);

    /// <summary>Tenta "pegar a vez" de mexer nos arquivos. Devolve false se já há outra execução.</summary>
    public bool TentarOcupar() => Interlocked.CompareExchange(ref _ocupado, 1, 0) == 0;

    public void Liberar() => Interlocked.Exchange(ref _ocupado, 0);

    public bool Ocupado => Volatile.Read(ref _ocupado) == 1;

    /// <summary>Anota as extensões novas sem pasta para a pessoa decidir na tela de regras.</summary>
    public void AnotarSemPasta(IEnumerable<string> extensoes)
    {
        var config = Config.Carregar();
        if (ConfiguracaoService.AnotarSemPasta(config, extensoes)) Config.Salvar(config);
    }
}
