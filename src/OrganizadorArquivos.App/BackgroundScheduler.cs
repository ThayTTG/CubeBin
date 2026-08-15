using System.Windows.Threading;
using OrganizadorArquivos.Core.Models;
using OrganizadorArquivos.Core.Services;

namespace OrganizadorArquivos.App;

/// <summary>
/// Dispara o OrganizadorService N vezes por dia (config.ExecucoesPorDia), em intervalos iguais
/// de 24h/N a partir de config.HorarioInicio. Ex.: 2 execuções a partir de meio-dia -> 12:00 e 00:00.
/// A checagem de ausência real acontece dentro de OrganizadorService.ExecutarCicloAsync (seção 7 da spec).
/// </summary>
public class BackgroundScheduler
{
    private readonly ConfigService _configService;
    private readonly OrganizadorService _organizadorService;
    private DispatcherTimer? _timerVerificacao;

    private DateOnly _diaDeReferencia = DateOnly.FromDateTime(DateTime.Now);
    private readonly HashSet<TimeOnly> _horariosJaExecutadosHoje = new();

    public BackgroundScheduler(ConfigService configService, OrganizadorService organizadorService)
    {
        _configService = configService;
        _organizadorService = organizadorService;
    }

    public void Iniciar()
    {
        // Verifica a cada 30s se algum dos horários agendados do dia já chegou. Não precisa ser mais
        // preciso que isso — a checagem de ausência do usuário já adiciona sua própria folga.
        _timerVerificacao = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timerVerificacao.Tick += async (_, _) => await VerificarEExecutarAsync();
        _timerVerificacao.Start();
    }

    public void Parar()
    {
        _timerVerificacao?.Stop();
    }

    private async Task VerificarEExecutarAsync()
    {
        var agora = DateTime.Now;
        var hoje = DateOnly.FromDateTime(agora);

        if (hoje != _diaDeReferencia)
        {
            _diaDeReferencia = hoje;
            _horariosJaExecutadosHoje.Clear();
        }

        var config = _configService.CarregarOuCriarPadrao();

        foreach (var horario in ObterHorariosDoDia(config))
        {
            if (_horariosJaExecutadosHoje.Contains(horario)) continue;
            if (agora.TimeOfDay < horario.ToTimeSpan()) continue;

            // Marca como executado (ou tentado) mesmo se ExecutarCicloAsync retornar null por ausência
            // do usuário não satisfeita — senão o ciclo tentaria de novo a cada 30s até o usuário sair.
            _horariosJaExecutadosHoje.Add(horario);
            await _organizadorService.ExecutarCicloAsync(config);
        }
    }

    /// <summary>
    /// Calcula os N horários do dia, espaçados em 24h/N a partir de HorarioInicio.
    /// Ex.: ExecucoesPorDia=2, HorarioInicio=12:00 -> [12:00, 00:00].
    /// </summary>
    public static List<TimeOnly> ObterHorariosDoDia(AppConfig config)
    {
        var n = Math.Max(1, config.ExecucoesPorDia);
        var intervaloMinutos = (24.0 * 60.0) / n;

        var horarios = new List<TimeOnly>();
        var minutosInicio = config.HorarioInicio.ToTimeSpan().TotalMinutes;

        for (var i = 0; i < n; i++)
        {
            var minutos = (minutosInicio + i * intervaloMinutos) % (24 * 60);
            horarios.Add(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutos)));
        }

        return horarios;
    }
}
