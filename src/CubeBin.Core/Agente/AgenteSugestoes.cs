using System.Security.Cryptography;
using System.Text.Json;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Organizar;

namespace CubeBin.Core.Agente;

public enum TipoSugestao
{
    Duplicado,
    TemporarioEsquecido,
    GrandeParado,
}

/// <summary>Um arquivo que o agente acha que talvez não precise estar no computador.</summary>
public class Sugestao
{
    public string Caminho { get; set; } = "";
    public long Tamanho { get; set; }
    public TipoSugestao Tipo { get; set; }
    public string Motivo { get; set; } = "";
    /// <summary>Nos duplicados: o arquivo igual que fica.</summary>
    public string? DuplicadoDe { get; set; }
    public string Nome => Path.GetFileName(Caminho);
}

public class ResultadoAgente
{
    public DateTime AnalisadoEm { get; set; } = DateTime.Now;
    public List<Sugestao> Sugestoes { get; set; } = new();
    public long Bytes => Sugestoes.Sum(s => s.Tamanho);
}

/// <summary>
/// O agente de limpeza (PRD 5.5): roda em segundo plano, estilo antivírus, e só SUGERE. Quem tira os
/// arquivos do computador é o Liberar Espaço, sempre com a confirmação da pessoa.
/// É feito de regras locais, sem internet e sem enviar nada para lugar nenhum:
/// - duplicados (mesmo tamanho e mesmo conteúdo — SHA-256; fica o mais antigo);
/// - temporários e caches esquecidos (.tmp, .bak, .old, .log... parados há N dias);
/// - arquivos grandes parados (acima de X MB, sem uso há N dias).
/// Respeita as proteções: nunca olha dentro de pastas de projeto, nem sugere atalhos, programas,
/// ocultos ou arquivos abertos.
/// </summary>
public class AgenteSugestoes
{
    private static readonly string[] Temporarios = { ".tmp", ".temp", ".cache", ".bak", ".old", ".log", ".dmp" };

    private readonly Configuracoes _config;
    private readonly Protecao _protecao;
    private readonly Func<DateTime> _agora;

    public AgenteSugestoes(Configuracoes config, Protecao? protecao = null, Func<DateTime>? agora = null)
    {
        _config = config;
        _protecao = protecao ?? new Protecao(config.Protecoes);
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>As pastas que o agente olha: as que o CubeBin organiza e as de destino.</summary>
    public IEnumerable<string> PastasAnalisadas() =>
        _config.Pastas.Where(p => p.Monitorar || _config.Regras.Any(r => r.PastaId == p.Id))
            .Select(p => p.CaminhoEfetivo()).Where(Directory.Exists).Distinct();

    public ResultadoAgente Analisar(CancellationToken cancelar = default)
    {
        var arquivos = new List<FileInfo>();
        var vistos = NomeSemConflito.NovoConjunto();
        foreach (var pasta in PastasAnalisadas())
            Coletar(new DirectoryInfo(pasta), arquivos, vistos, cancelar);

        var resultado = new ResultadoAgente { AnalisadoEm = _agora() };
        resultado.Sugestoes.AddRange(Duplicados(arquivos, cancelar));
        var jaSugeridos = resultado.Sugestoes.Select(s => s.Caminho).ToHashSet();
        resultado.Sugestoes.AddRange(TemporariosEsquecidos(arquivos).Where(s => jaSugeridos.Add(s.Caminho)));
        resultado.Sugestoes.AddRange(GrandesParados(arquivos).Where(s => jaSugeridos.Add(s.Caminho)));
        return resultado;
    }

    private void Coletar(DirectoryInfo raiz, List<FileInfo> arquivos, ISet<string> vistos, CancellationToken cancelar)
    {
        var pendentes = new Stack<DirectoryInfo>();
        pendentes.Push(raiz);
        while (pendentes.Count > 0)
        {
            cancelar.ThrowIfCancellationRequested();
            var pasta = pendentes.Pop();
            if (pasta != raiz && (Protecao.EhOcultoOuDoSistema(pasta) || pasta.LinkTarget is not null)) continue;
            if (Protecao.EhPastaDeProjeto(pasta.FullName, _config.Protecoes.MarcadoresDeProjeto)) continue;
            try
            {
                foreach (var arquivo in pasta.GetFiles())
                    if (arquivo.LinkTarget is null && vistos.Add(arquivo.FullName) && PodeSugerir(arquivo))
                        arquivos.Add(arquivo);
                foreach (var sub in pasta.GetDirectories()) pendentes.Push(sub);
            }
            catch (Exception) { /* sem permissão: segue */ }
        }
    }

    private bool PodeSugerir(FileInfo arquivo)
    {
        var motivo = _protecao.MotivoParaNaoMover(arquivo);
        // Temporários entram mesmo sendo "download incompleto" quando estão parados há muito tempo.
        return motivo is null || (motivo == MotivoPulado.DownloadIncompleto && Temporarios.Contains(Extensoes.DoArquivo(arquivo.Name)));
    }

    /// <summary>Agrupa por tamanho (sem ler nada) e só calcula hash quando há mais de um do mesmo tamanho.</summary>
    private IEnumerable<Sugestao> Duplicados(List<FileInfo> arquivos, CancellationToken cancelar)
    {
        foreach (var mesmoTamanho in arquivos.Where(a => a.Length > 0).GroupBy(a => a.Length).Where(g => g.Count() > 1))
        {
            var porHash = new Dictionary<string, List<FileInfo>>();
            foreach (var arquivo in mesmoTamanho)
            {
                cancelar.ThrowIfCancellationRequested();
                string hash;
                try { hash = Sha256(arquivo.FullName); }
                catch (Exception) { continue; }
                if (!porHash.TryGetValue(hash, out var lista)) porHash[hash] = lista = new();
                lista.Add(arquivo);
            }
            foreach (var iguais in porHash.Values.Where(l => l.Count > 1))
            {
                var fica = iguais.OrderBy(a => a.LastWriteTime).ThenBy(a => a.FullName.Length).First();
                foreach (var copia in iguais.Where(a => a.FullName != fica.FullName))
                    yield return new Sugestao
                    {
                        Caminho = copia.FullName, Tamanho = copia.Length, Tipo = TipoSugestao.Duplicado, DuplicadoDe = fica.FullName,
                        Motivo = $"Cópia igual de \"{fica.Name}\" ({Path.GetFileName(fica.DirectoryName)})",
                    };
            }
        }
    }

    private IEnumerable<Sugestao> TemporariosEsquecidos(List<FileInfo> arquivos)
    {
        var limite = _agora().AddDays(-_config.Agente.DiasTemporarioParado);
        foreach (var a in arquivos.Where(a => Temporarios.Contains(Extensoes.DoArquivo(a.Name))))
        {
            var toque = UltimoToque(a);
            if (toque >= limite) continue;
            yield return new Sugestao
            {
                Caminho = a.FullName, Tamanho = a.Length, Tipo = TipoSugestao.TemporarioEsquecido,
                Motivo = $"Arquivo temporário parado há {(int)(_agora() - toque).TotalDays} dias",
            };
        }
    }

    private IEnumerable<Sugestao> GrandesParados(List<FileInfo> arquivos)
    {
        var limite = _agora().AddDays(-_config.Agente.DiasArquivoGrandeParado);
        var minimo = (long)_config.Agente.TamanhoMinimoGrandeMb * 1024 * 1024;
        foreach (var a in arquivos.Where(a => a.Length >= minimo))
        {
            var toque = UltimoToque(a);
            if (toque >= limite) continue;
            yield return new Sugestao
            {
                Caminho = a.FullName, Tamanho = a.Length, Tipo = TipoSugestao.GrandeParado,
                Motivo = $"Arquivo grande sem uso há {(int)(_agora() - toque).TotalDays} dias",
            };
        }
    }

    /// <summary>O Windows costuma não atualizar a data de acesso: usa a mais recente entre acesso e modificação.</summary>
    private static DateTime UltimoToque(FileInfo a) => a.LastAccessTime > a.LastWriteTime ? a.LastAccessTime : a.LastWriteTime;

    private static string Sha256(string caminho)
    {
        using var sha = SHA256.Create();
        using var leitura = File.OpenRead(caminho);
        return Convert.ToHexString(sha.ComputeHash(leitura));
    }

    // ---------- Guardar as sugestões (para a tela do Liberar Espaço) ----------

    public static void Salvar(string caminho, ResultadoAgente resultado)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, JsonSerializer.Serialize(resultado, ConfiguracaoService.OpcoesJson));
    }

    /// <summary>Lê as sugestões guardadas, tirando as que já não existem.</summary>
    public static ResultadoAgente? Carregar(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return null;
            var r = JsonSerializer.Deserialize<ResultadoAgente>(File.ReadAllText(caminho), ConfiguracaoService.OpcoesJson);
            r?.Sugestoes.RemoveAll(s => !File.Exists(s.Caminho));
            return r;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Hora de analisar de novo? No máximo uma vez por dia, e só com o agente ligado.</summary>
    public static bool DeveAnalisar(AgenteConfig agente, DateTime agora) =>
        agente.Ligado && (agente.UltimaAnalise is not { } ultima || agora - ultima >= TimeSpan.FromHours(24));
}
