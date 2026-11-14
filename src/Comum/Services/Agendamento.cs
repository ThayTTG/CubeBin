namespace Comum;

/// <summary>
/// A conta dos horários do dia, usada pelos agendadores dos dois apps (a organização automática e
/// o agente de sugestões). Antes ela existia duas vezes, uma em cada agendador.
/// </summary>
public static class Agendamento
{
    /// <summary>
    /// Calcula os N horários do dia, espaçados em 24h/N a partir do horário de início.
    /// Ex.: 2 vezes a partir das 12:00 -> [12:00, 00:00]. Zero ou menos vira 1.
    /// </summary>
    public static List<TimeOnly> ObterHorariosDoDia(int vezesPorDia, TimeOnly horarioInicio)
    {
        var n = Math.Max(1, vezesPorDia);
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
