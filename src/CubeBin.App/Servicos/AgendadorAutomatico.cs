using Avalonia.Threading;
using CubeBin.Core.Organizar;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Servicos;

/// <summary>
/// O modo automático (PRD 5.4): a cada 30 s olha há quanto tempo a pessoa não mexe no computador.
/// Ausente há T minutos → monta o plano (sem prévia), avisa com 60 s para cancelar, organiza e avisa
/// o fim com o que não pôde ser movido. Uma tentativa por ausência.
/// </summary>
public sealed class AgendadorAutomatico
{
    private readonly ServicosApp _servicos;
    private readonly Ociosidade _ociosidade = new();
    private readonly Action<string?> _abrirCubo;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _jaTentouNestaAusencia;
    private bool _verificando;

    public AgendadorAutomatico(ServicosApp servicos, Action<string?> abrirCubo)
    {
        _servicos = servicos;
        _abrirCubo = abrirCubo;
        _timer.Tick += async (_, _) => await VerificarAsync();
    }

    public void Iniciar() => _timer.Start();

    /// <summary>Com CUBEBIN_DEPURAR=1, anota no log cada passo do automático.</summary>
    private void Depurar(string texto)
    {
        if (Environment.GetEnvironmentVariable("CUBEBIN_DEPURAR") == "1") _servicos.Ambiente.Log("[depuração] " + texto);
    }
    public void Parar() => _timer.Stop();

    private async Task VerificarAsync()
    {
        if (_verificando) return;
        _verificando = true;
        try
        {
            var config = _servicos.Config.Carregar();
            var ocioso = await Task.Run(_ociosidade.ObterTempoOcioso);
            Depurar($"ausência: {ocioso?.ToString() ?? "desconhecida"}, ligada: {config.Automacao.Ligada}, já tentou: {_jaTentouNestaAusencia}");
            if (OrganizacaoAutomatica.PessoaVoltou(ocioso)) _jaTentouNestaAusencia = false;
            if (_servicos.Ocupado || !OrganizacaoAutomatica.DeveTentar(config.Automacao, ocioso, DateTime.Now, _jaTentouNestaAusencia))
                return;

            _jaTentouNestaAusencia = true;
            await OrganizarAsync();
        }
        catch (Exception ex)
        {
            App.RegistrarCrash(ex);
        }
        finally
        {
            _verificando = false;
        }
    }

    /// <summary>Uma organização automática completa (também usada pelo "Organizar agora" da bandeja
    /// quando a janela está fechada).</summary>
    public async Task OrganizarAsync()
    {
        var plano = OrganizacaoAutomatica.FiltrarParaAutomatico(await Task.Run(() => _servicos.NovoPlanejador().Planejar()));
        _servicos.AnotarSemPasta(plano.ExtensoesSemPasta);
        Depurar($"plano automático: {plano.Movimentos.Count} para mover, {plano.Pulados.Count} pulados");
        if (plano.Movimentos.Count == 0) return; // nada a fazer: não incomoda ninguém

        var segundos = _servicos.Config.Carregar().Automacao.SegundosParaCancelar;
        var cancelado = await _servicos.Avisos.AvisarInicioAsync(segundos, plano.Movimentos.Count);
        Depurar($"aviso de início respondido: cancelado = {cancelado}");
        if (cancelado)
        {
            _servicos.Config.Alterar(c => c.Automacao.ExecucoesCanceladas++);
            _servicos.Ambiente.Log("Organização automática cancelada pela pessoa no aviso de início.");
            return;
        }

        if (!_servicos.TentarOcupar()) return;
        try
        {
            // Monta de novo: em 60 s arquivos podem ter sido abertos, alterados ou apagados.
            var resultado = await Task.Run(() =>
            {
                var novo = OrganizacaoAutomatica.FiltrarParaAutomatico(_servicos.NovoPlanejador().Planejar());
                return _servicos.NovoOrganizador().Executar(novo, "automatico");
            });
            _servicos.Config.Alterar(c =>
            {
                c.Automacao.UltimaExecucaoAutomatica = DateTime.Now;
                c.Automacao.ExecucoesAutomaticas++;
            });
            _servicos.AnotarSemPasta(resultado.ExtensoesSemPasta);

            if (!resultado.NadaAFazer)
            {
                var (titulo, mensagem) = OrganizacaoAutomatica.TextoDeFim(resultado);
                _servicos.Avisos.AvisarFim(titulo, mensagem, () => _abrirCubo(resultado.Cubo?.Id));
            }
        }
        finally
        {
            _servicos.Liberar();
        }
    }
}
