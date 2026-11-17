using CubeBin.App.Interface;
using CubeBin.Core;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.LiberarEspaco;
using CubeBin.Core.Nuvem;
using CubeBin.Core.Agente;
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
        Drive = new GoogleDriveNuvem(ambiente.PastaCredenciais);

        // Modo de teste: a "nuvem" vira uma pasta local, para testar o fluxo sem conta do Google.
        var pastaDeTeste = Environment.GetEnvironmentVariable("CUBEBIN_NUVEM_DE_TESTE");
        if (!string.IsNullOrWhiteSpace(pastaDeTeste) && Path.IsPathRooted(pastaDeTeste))
            NuvemDeTeste = new NuvemLocal(pastaDeTeste);
    }

    /// <summary>Preenchido só no modo de teste (variável CUBEBIN_NUVEM_DE_TESTE).</summary>
    public NuvemLocal? NuvemDeTeste { get; }

    /// <summary>A nuvem em uso: o Google Drive, ou a pasta de teste.</summary>
    public IArmazenamentoNuvem Nuvem => (IArmazenamentoNuvem?)NuvemDeTeste ?? Drive;

    /// <summary>O Google Drive (versão 2.0).</summary>
    public GoogleDriveNuvem Drive { get; }

    /// <summary>Há uma conta do Google Drive conectada e pronta para receber os Cubos?</summary>
    public bool NuvemConectada => NuvemDeTeste is not null || Drive.Conectado;

    public event Action? NuvemAlterada;

    public EnvioNuvem NovoEnvioNuvem() => new(Ambiente, Cubos, Nuvem);

    /// <summary>Ao abrir o app: reconecta com o token guardado (sem abrir navegador) e lê o índice.</summary>
    public async Task ReconectarNuvemAsync()
    {
        try
        {
            if (NuvemDeTeste is null && !await Drive.ConectarSilenciosamenteAsync()) return;
            NuvemAlterada?.Invoke();
            var novos = await NovoEnvioNuvem().SincronizarIndiceAsync();
            if (novos > 0) Ambiente.Log($"{novos} Cubo(s) da nuvem acrescentados pelo índice.");
        }
        catch (Exception ex)
        {
            Ambiente.Log($"Não foi possível falar com o Google Drive agora: {EnvioNuvem.ExplicarErro(ex)}");
        }
    }

    /// <summary>"Conectar conta": abre o navegador para o login do Google.</summary>
    public async Task<string?> ConectarNuvemAsync()
    {
        await Drive.ConectarAsync();
        var conta = await Drive.ContaAsync();
        Config.Alterar(c => c.Nuvem.ContaConectada = conta.Email);
        NuvemAlterada?.Invoke();
        _ = NovoEnvioNuvem().SincronizarIndiceAsync();
        return conta.Email;
    }

    public async Task DesconectarNuvemAsync()
    {
        await Drive.DesconectarAsync();
        Config.Alterar(c => c.Nuvem.ContaConectada = null);
        NuvemAlterada?.Invoke();
    }

    /// <summary>As sugestões do agente guardadas da última análise.</summary>
    public ResultadoAgente? Sugestoes() => AgenteSugestoes.Carregar(Ambiente.CaminhoSugestoes);

    /// <summary>Roda o agente agora (fora da thread da tela) e guarda o resultado.</summary>
    public async Task<ResultadoAgente> AnalisarAgoraAsync()
    {
        var config = Config.Carregar();
        var resultado = await Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; // baixo uso de CPU (PRD 7)
            try { return new AgenteSugestoes(config, NovaProtecao()).Analisar(); }
            finally { Thread.CurrentThread.Priority = ThreadPriority.Normal; }
        });
        AgenteSugestoes.Salvar(Ambiente.CaminhoSugestoes, resultado);
        Config.Alterar(c => c.Agente.UltimaAnalise = resultado.AnalisadoEm);
        Ambiente.Log($"Agente: {resultado.Sugestoes.Count} sugestão(ões), {resultado.Bytes} bytes.");
        return resultado;
    }

    public Protecao NovaProtecao() => new(Config.Carregar().Protecoes);
    public Planejador NovoPlanejador() => new(Config.Carregar(), NovaProtecao());
    public Organizador NovoOrganizador() => new(Ambiente, Cubos, NovaProtecao());
    public Restaurador NovoRestaurador() => new(Ambiente, Cubos, Lixeira);
    public Liberador NovoLiberador() => new(Ambiente, Cubos, Lixeira);

    /// <summary>Tenta "pegar a vez" de mexer nos arquivos. Devolve false se já há outra execução.</summary>
    public bool TentarOcupar() => Interlocked.CompareExchange(ref _ocupado, 1, 0) == 0;

    public void Liberar() => Interlocked.Exchange(ref _ocupado, 0);

    public bool Ocupado => Volatile.Read(ref _ocupado) == 1;

    /// <summary>Envia a seleção para a nuvem (compacta, envia, confere e só então apaga).</summary>
    public Task<ResultadoLiberar> EnviarParaNuvemAsync(SelecaoLiberar selecao, IProgress<Progresso> progresso, CancellationToken cancelar) =>
        NovoEnvioNuvem().EnviarAsync(selecao, progresso, cancelar);

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
    protected virtual Task<ResultadoRestauracao> RestaurarSemMensagemAsync(Cubo cubo)
    {
        if (cubo.Tipo != TipoCubo.Nuvem) return Task.Run(() => NovoRestaurador().Restaurar(cubo));
        if (!NuvemConectada) throw new InvalidOperationException("Conecte a conta do Google Drive (Configurações › Nuvem) para trazer este Cubo de volta");
        return NovoEnvioNuvem().RestaurarAsync(cubo);
    }

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

    /// <summary>Um botão a mais nos detalhes de um Cubo (ex.: "Tentar enviar de novo", na 2.0).</summary>
    public virtual Avalonia.Controls.Control? AcaoExtraDoCubo(Cubo cubo)
    {
        if (cubo.EnvioAdiado)
        {
            var botao = Ui.Botao("Tentar enviar de novo", "primary", "IconeEnviarNuvem", async (_, _) =>
            {
                if (!NuvemConectada)
                {
                    Dialogo.Show("Conecte a conta do Google Drive em Configurações › Nuvem e tente de novo.", "Nuvem");
                    return;
                }
                if (!TentarOcupar()) return;
                try
                {
                    var r = await NovoEnvioNuvem().TentarDeNovoAsync(cubo, Config.Carregar());
                    Dialogo.Show(r.MotivoAdiamento is { } m
                            ? $"Ainda não deu: {m}. Os arquivos continuam no computador."
                            : $"Pronto! {r.Feitos.Count} item(ns) estão na nuvem e saíram do computador.",
                        "Nuvem", MessageBoxButton.OK, r.MotivoAdiamento is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                finally { Liberar(); }
            });
            botao.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            return botao;
        }
        if (cubo.Tipo == TipoCubo.Nuvem && !string.IsNullOrEmpty(cubo.LinkNuvem))
        {
            var abrir = Ui.Botao("Abrir no Google Drive", "secondary", "IconeLinkExterno", (_, _) => InfoApp.AbrirNoNavegador(cubo.LinkNuvem!));
            abrir.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            return abrir;
        }
        return null;
    }

    /// <summary>Anota as extensões novas sem pasta para a pessoa decidir na tela de regras.</summary>
    public void AnotarSemPasta(IEnumerable<string> extensoes)
    {
        var config = Config.Carregar();
        if (ConfiguracaoService.AnotarSemPasta(config, extensoes)) Config.Salvar(config);
    }
}
