using System.Windows.Threading;
using CubeBin.Core.Models;
using CubeBin.Core.Services;

namespace CubeBin.App;

/// <summary>
/// Dispara o AgenteSugestaoService N vezes por dia (config.AgenteExecucoesPorDia), só quando o
/// agente está ativo (config.AgenteAtivo — desativado por padrão) e o usuário está ausente pelo
/// mesmo tempo já configurado para o resto do app (config.TempoAusenciaMinutos). Mesma filosofia
/// de agendamento do BackgroundScheduler, com sua própria cadência independente.
/// </summary>
public class AgenteScheduler
{
    private readonly ConfigService _configService;
    private readonly AgenteSugestaoService _agenteSugestaoService;
    private readonly SugestaoService _sugestaoService;
    private readonly IdleDetectionService _idleDetectionService;
    private readonly INotificationService _notificationService;
    private DispatcherTimer? _timerVerificacao;

    private DateOnly _diaDeReferencia = DateOnly.FromDateTime(DateTime.Now);
    private readonly HashSet<TimeOnly> _horariosJaExecutadosHoje = new();

    /// <summary>Disparado (numa thread de background) quando uma análise termina — a UI, se aberta, atualiza a grid.</summary>
    public event Action? AnaliseFinalizada;

    public AgenteScheduler(
        ConfigService configService,
        AgenteSugestaoService agenteSugestaoService,
        SugestaoService sugestaoService,
        IdleDetectionService idleDetectionService,
        INotificationService notificationService)
    {
        _configService = configService;
        _agenteSugestaoService = agenteSugestaoService;
        _sugestaoService = sugestaoService;
        _idleDetectionService = idleDetectionService;
        _notificationService = notificationService;
    }

    public void Iniciar()
    {
        _timerVerificacao = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timerVerificacao.Tick += (_, _) => VerificarEExecutar();
        _timerVerificacao.Start();
    }

    public void Parar() => _timerVerificacao?.Stop();

    private void VerificarEExecutar()
    {
        var config = _configService.CarregarOuCriarPadrao();
        if (!config.AgenteAtivo) return; // Desligado por padrão — só roda se o usuário ligar explicitamente.

        var agora = DateTime.Now;
        var hoje = DateOnly.FromDateTime(agora);

        if (hoje != _diaDeReferencia)
        {
            _diaDeReferencia = hoje;
            _horariosJaExecutadosHoje.Clear();
        }

        foreach (var horario in ObterHorariosDoDia(config.AgenteExecucoesPorDia, config.AgenteHorarioInicio))
        {
            if (_horariosJaExecutadosHoje.Contains(horario)) continue;
            if (agora.TimeOfDay < horario.ToTimeSpan()) continue;

            _horariosJaExecutadosHoje.Add(horario);

            // Mesma janela de "usuário ausente" já usada pelo resto do app.
            if (!_idleDetectionService.UsuarioEstaAusente(config.TempoAusenciaMinutos)) continue;

            ExecutarAnalise(config);
        }
    }

    private void ExecutarAnalise(AppConfig config)
    {
        // Roda em background thread — hash de arquivos duplicados pode ser um trabalho pesado de I/O.
        Task.Run(() =>
        {
            var novas = _agenteSugestaoService.Analisar(config);
            _sugestaoService.MesclarNovasSugestoes(config.ArquivoSugestoes, novas);

            if (novas.Count > 0)
            {
                var totalBytes = novas.Sum(s => s.TamanhoBytes);
                _notificationService.NotificarMensagemSimples(
                    "CubeBin — Novas sugestões de limpeza",
                    $"{novas.Count} sugestão(ões) encontradas — até {FormatarBytes.Formatar(totalBytes)} podem ser liberados.");
            }

            AnaliseFinalizada?.Invoke();
        });
    }

    /// <summary>Calcula os N horários do dia, espaçados em 24h/N a partir do horário de início.</summary>
    public static List<TimeOnly> ObterHorariosDoDia(int execucoesPorDia, TimeOnly horarioInicio)
    {
        var n = Math.Max(1, execucoesPorDia);
        var intervaloMinutos = (24.0 * 60.0) / n;

        var horarios = new List<TimeOnly>();
        var minutosInicio = horarioInicio.ToTimeSpan().TotalMinutes;

        for (var i = 0; i < n; i++)
        {
            var minutos = (minutosInicio + i * intervaloMinutos) % (24 * 60);
            horarios.Add(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutos)));
        }

        return horarios;
    }
}
