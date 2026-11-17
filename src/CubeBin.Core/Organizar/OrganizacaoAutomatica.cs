using CubeBin.Core.Configuracao;

namespace CubeBin.Core.Organizar;

/// <summary>
/// As decisões do modo automático (PRD 5.4), separadas da tela para poderem ser testadas:
/// quando tentar e o que fica de fora (sem prévia, então nada que precise de confirmação).
/// </summary>
public static class OrganizacaoAutomatica
{
    /// <summary>
    /// Deve organizar agora? Só com o automático ligado, com a pessoa ausente há T minutos, uma vez por
    /// ausência e respeitando o intervalo mínimo entre execuções.
    /// </summary>
    /// <param name="ocioso">Tempo sem mexer no computador, ou null se o sistema não informa
    /// (ex.: Linux com Sway/Hyprland). Nesse caso vale só o intervalo — sempre com o aviso de 60 s.</param>
    /// <param name="jaTentouNestaAusencia">Já houve uma tentativa (feita ou cancelada) desde que a
    /// pessoa saiu: espera ela voltar antes de tentar de novo.</param>
    public static bool DeveTentar(Automacao automacao, TimeSpan? ocioso, DateTime agora, bool jaTentouNestaAusencia)
    {
        if (!automacao.Ligada || jaTentouNestaAusencia) return false;

        if (automacao.UltimaExecucaoAutomatica is { } ultima &&
            agora - ultima < TimeSpan.FromHours(automacao.HorasEntreExecucoes))
            return false;

        return ocioso is null || ocioso.Value >= TimeSpan.FromMinutes(automacao.MinutosDeAusencia);
    }

    /// <summary>A pessoa voltou (mexeu no computador há pouco): a próxima ausência pode ter tentativa.</summary>
    public static bool PessoaVoltou(TimeSpan? ocioso) => ocioso is { } o && o < TimeSpan.FromMinutes(1);

    /// <summary>
    /// No automático não há prévia, então o que precisaria de confirmação fica de fora: pastas do
    /// OneDrive com muitos arquivos (PRD 6.3) ficam para quando a pessoa abrir o CubeBin.
    /// </summary>
    public static PlanoOrganizacao FiltrarParaAutomatico(PlanoOrganizacao plano)
    {
        if (plano.AvisosOneDrive.Count == 0) return plano;

        var filtrado = new PlanoOrganizacao();
        foreach (var mov in plano.Movimentos)
        {
            if (mov.NoOneDrive && plano.AvisosOneDrive.Contains(mov.PastaOrigemNome))
                filtrado.Pulados.Add(new ArquivoPulado { Caminho = mov.Origem, Motivo = MotivoPulado.OneDriveAguardandoConfirmacao });
            else
                filtrado.Movimentos.Add(mov);
        }
        filtrado.Pulados.AddRange(plano.Pulados);
        filtrado.SemPasta.AddRange(plano.SemPasta);
        filtrado.PastasDeProjeto.AddRange(plano.PastasDeProjeto);
        filtrado.PastasInexistentes.AddRange(plano.PastasInexistentes);
        filtrado.AvisosOneDrive.AddRange(plano.AvisosOneDrive);
        filtrado.DestinosACriar.AddRange(plano.DestinosACriar);
        return filtrado;
    }

    /// <summary>O texto da notificação de fim (PRD 5.4: sucesso, ou a lista do que não pôde ser movido).</summary>
    public static (string Titulo, string Mensagem) TextoDeFim(ResultadoOrganizacao r)
    {
        if (r.Cancelado)
            return ("Organização interrompida", $"{r.Movidos.Count} arquivo(s) organizado(s) antes de parar. Dá para desfazer pelo CubeBin.");

        if (r.Falhas.Count == 0)
        {
            var extra = r.ExtensoesSemPasta.Count > 0
                ? $" Há {r.ExtensoesSemPasta.Count} tipo(s) de arquivo esperando você escolher a pasta."
                : "";
            return ("Tudo organizado", $"{r.Movidos.Count} arquivo(s) foram para as pastas certas.{extra}");
        }

        var lista = string.Join("\n", r.Falhas.Take(5).Select(f => $"• {f.Nome}: {f.Motivo}"));
        if (r.Falhas.Count > 5) lista += $"\n• e mais {r.Falhas.Count - 5}…";
        return ($"{r.Movidos.Count} organizado(s), {r.Falhas.Count} não puderam ser movidos", lista);
    }
}
