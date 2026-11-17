using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CubeBin.Core.Plataforma;

namespace CubeBin.Core.Configuracao;

/// <summary>
/// Lê e grava o <c>configuracoes.json</c>. Grava em um arquivo temporário e troca no fim (nunca
/// deixa o arquivo pela metade), guarda a versão anterior em <c>.bak</c> e, se o JSON estiver
/// quebrado, guarda a cópia estragada com outro nome e volta às regras padrão, avisando.
/// </summary>
public class ConfiguracaoService
{
    public static readonly JsonSerializerOptions OpcoesJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Acentos e caminhos aparecem como são, para a pessoa conseguir ler e editar.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _trava = new();
    private Configuracoes? _atual;

    public string Caminho { get; }

    /// <summary>Preenchido quando o arquivo estava quebrado e foi trocado pelo padrão.</summary>
    public string? AvisoDeCarga { get; private set; }

    public event Action<Configuracoes>? Alterada;

    public ConfiguracaoService(string caminho) => Caminho = caminho;

    public Configuracoes Carregar()
    {
        lock (_trava)
        {
            if (_atual is not null) return _atual;

            Configuracoes? lida = null;
            if (File.Exists(Caminho))
            {
                try
                {
                    lida = JsonSerializer.Deserialize<Configuracoes>(File.ReadAllText(Caminho), OpcoesJson);
                }
                catch (Exception ex)
                {
                    var copia = $"{Caminho}.quebrado-{DateTime.Now:yyyyMMdd-HHmmss}";
                    try { File.Copy(Caminho, copia, overwrite: false); } catch { /* melhor esforço */ }
                    AvisoDeCarga = $"O arquivo de configurações estava com um erro ({ex.Message}). " +
                                   $"Uma cópia dele foi guardada em \"{copia}\" e as configurações voltaram ao padrão.";
                }
            }

            _atual = Completar(lida ?? RegrasPadrao.CriarConfiguracaoInicial());
            if (!File.Exists(Caminho) || lida is null) GravarNoDisco(_atual);
            return _atual;
        }
    }

    /// <summary>Salva a configuração (cada mudança na tela salva sozinha: PRD 5.6).</summary>
    public void Salvar(Configuracoes config)
    {
        lock (_trava)
        {
            _atual = config;
            GravarNoDisco(config);
        }
        Alterada?.Invoke(config);
    }

    /// <summary>Altera e salva numa chamada só.</summary>
    public void Alterar(Action<Configuracoes> alteracao)
    {
        var config = Carregar();
        lock (_trava) alteracao(config);
        Salvar(config);
    }

    /// <summary>Esquece a cópia em memória (para reler o arquivo editado à mão).</summary>
    public void Recarregar()
    {
        lock (_trava) _atual = null;
        Alterada?.Invoke(Carregar());
    }

    private void GravarNoDisco(Configuracoes config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        var temporario = Caminho + ".gravando";
        File.WriteAllText(temporario, JsonSerializer.Serialize(config, OpcoesJson));
        if (File.Exists(Caminho))
            File.Copy(Caminho, Caminho + ".bak", overwrite: true);
        File.Move(temporario, Caminho, overwrite: true);
    }

    /// <summary>
    /// Garante as regras de segurança mesmo num JSON editado à mão: as seis pastas padrão sempre
    /// existem (nunca são removidas), ids são únicos e as extensões ficam no formato ".ext".
    /// </summary>
    public static Configuracoes Completar(Configuracoes config)
    {
        config.Pastas ??= new();
        config.Regras ??= RegrasPadrao.RegrasIniciais();
        config.ExtensoesSemPasta ??= new();
        config.ExtensoesIgnoradas ??= new();
        config.Automacao ??= new();
        config.Protecoes ??= new();
        config.Nuvem ??= new();
        config.Agente ??= new();

        config.Pastas.RemoveAll(p => p is null);
        foreach (var padrao in PastasDoSistema.Todas)
        {
            if (config.Pastas.All(p => p.Padrao != padrao))
                config.Pastas.Insert(Math.Min((int)padrao, config.Pastas.Count), RegrasPadrao.CriarPastaPadrao(padrao));
        }

        var ids = new HashSet<string>();
        foreach (var pasta in config.Pastas)
        {
            if (pasta.Padrao is { } padrao) pasta.Id = RegrasPadrao.IdDaPasta(padrao);
            if (string.IsNullOrWhiteSpace(pasta.Id) || !ids.Add(pasta.Id))
            {
                pasta.Id = "pasta-" + Guid.NewGuid().ToString("N")[..8];
                ids.Add(pasta.Id);
            }
            if (string.IsNullOrWhiteSpace(pasta.Nome))
                pasta.Nome = pasta.Padrao is { } p ? PastasDoSistema.NomeAmigavel(p) : Path.GetFileName(pasta.Caminho ?? "") ?? "Pasta";
        }

        // Regras: só para pastas que existem; cada extensão em uma regra só (a primeira vence).
        var vistas = new HashSet<string>();
        config.Regras.RemoveAll(r => r is null || config.ObterPasta(r.PastaId) is null);
        foreach (var regra in config.Regras)
        {
            regra.Extensoes = (regra.Extensoes ?? new())
                .Select(Extensoes.Normalizar)
                .Where(e => e.Length > 0 && vistas.Add(e))
                .ToList();
        }

        config.ExtensoesIgnoradas = config.ExtensoesIgnoradas.Select(Extensoes.Normalizar)
            .Where(e => e.Length > 0).Distinct().ToList();
        config.ExtensoesSemPasta = config.ExtensoesSemPasta.Select(Extensoes.Normalizar)
            .Where(e => e.Length > 0 && !vistas.Contains(e) && !config.ExtensoesIgnoradas.Contains(e))
            .Distinct().ToList();

        config.Automacao.MinutosDeAusencia = Math.Clamp(config.Automacao.MinutosDeAusencia, 1, 24 * 60);
        config.Automacao.SegundosParaCancelar = Math.Clamp(config.Automacao.SegundosParaCancelar, 10, 600);
        config.Automacao.HorasEntreExecucoes = Math.Clamp(config.Automacao.HorasEntreExecucoes, 1, 24 * 7);
        config.Protecoes.MinutosSemAlteracao = Math.Clamp(config.Protecoes.MinutosSemAlteracao, 0, 24 * 60);
        config.Protecoes.MarcadoresDeProjeto ??= new();
        config.Protecoes.ExtensoesDeAtalho ??= new();
        config.Protecoes.ExtensoesExecutaveis ??= new();
        config.Protecoes.ExtensoesDeDownloadIncompleto ??= new();
        return config;
    }

    // ---------- Operações usadas pelas telas ----------

    /// <summary>Move uma extensão para a regra de outra pasta (o "arrastar o bloquinho").</summary>
    public static void AtribuirExtensao(Configuracoes config, string extensao, string pastaId)
    {
        var ext = Extensoes.Normalizar(extensao);
        if (ext.Length == 0 || config.ObterPasta(pastaId) is null) return;

        foreach (var r in config.Regras) r.Extensoes.RemoveAll(e => Extensoes.Normalizar(e) == ext);
        config.ExtensoesSemPasta.Remove(ext);
        config.ExtensoesIgnoradas.Remove(ext);

        var regra = config.Regras.FirstOrDefault(r => r.PastaId == pastaId);
        if (regra is null)
        {
            regra = new Regra { PastaId = pastaId };
            config.Regras.Add(regra);
        }
        regra.Extensoes.Add(ext);
        config.Regras.RemoveAll(r => r.Extensoes.Count == 0 && r.PastaId != pastaId);
    }

    /// <summary>Tira a extensão de qualquer pasta: os arquivos dela passam a ficar onde estão.</summary>
    public static void IgnorarExtensao(Configuracoes config, string extensao)
    {
        var ext = Extensoes.Normalizar(extensao);
        foreach (var r in config.Regras) r.Extensoes.RemoveAll(e => Extensoes.Normalizar(e) == ext);
        config.Regras.RemoveAll(r => r.Extensoes.Count == 0);
        config.ExtensoesSemPasta.Remove(ext);
        if (!config.ExtensoesIgnoradas.Contains(ext)) config.ExtensoesIgnoradas.Add(ext);
    }

    /// <summary>Anota extensões novas encontradas sem pasta, para a pessoa decidir depois.</summary>
    public static bool AnotarSemPasta(Configuracoes config, IEnumerable<string> extensoes)
    {
        var mudou = false;
        foreach (var ext in extensoes.Select(Extensoes.Normalizar).Where(e => e.Length > 0))
        {
            if (config.RegraDaExtensao(ext) is not null || config.ExtensoesIgnoradas.Contains(ext)) continue;
            if (config.ExtensoesSemPasta.Contains(ext)) continue;
            config.ExtensoesSemPasta.Add(ext);
            mudou = true;
        }
        return mudou;
    }

    /// <summary>Cria uma pasta personalizada (no disco e na configuração). Nunca apaga nada.</summary>
    public static PastaConfig CriarPastaPersonalizada(Configuracoes config, string nome, string caminho, bool monitorar)
    {
        var problema = ValidarPastaPersonalizada(config, caminho);
        if (problema is not null) throw new InvalidOperationException(problema);

        Directory.CreateDirectory(caminho);
        var pasta = new PastaConfig
        {
            Id = "pasta-" + Guid.NewGuid().ToString("N")[..8],
            Nome = string.IsNullOrWhiteSpace(nome) ? Path.GetFileName(caminho.TrimEnd(Path.DirectorySeparatorChar)) : nome.Trim(),
            Caminho = Path.GetFullPath(caminho),
            Monitorar = monitorar,
        };
        config.Pastas.Add(pasta);
        return pasta;
    }

    /// <summary>
    /// Remove uma pasta personalizada da lista do CubeBin (o disco não é tocado). As extensões que iam
    /// para ela voltam para "sem pasta". Pastas padrão não podem ser removidas.
    /// </summary>
    public static bool RemoverPastaPersonalizada(Configuracoes config, string pastaId)
    {
        var pasta = config.ObterPasta(pastaId);
        if (pasta is null || !pasta.PodeRemover) return false;

        foreach (var regra in config.Regras.Where(r => r.PastaId == pastaId))
            foreach (var ext in regra.Extensoes)
                if (!config.ExtensoesSemPasta.Contains(ext)) config.ExtensoesSemPasta.Add(ext);
        config.Regras.RemoveAll(r => r.PastaId == pastaId);
        config.Pastas.Remove(pasta);
        return true;
    }

    /// <summary>
    /// "Agir só nas pastas do usuário" (PRD 6.3): recusa a raiz do disco, pastas do sistema e
    /// programas, a pasta de dados do próprio CubeBin e caminhos repetidos.
    /// </summary>
    public static string? ValidarPastaPersonalizada(Configuracoes config, string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return "Escolha uma pasta.";
        string completo;
        try { completo = Path.GetFullPath(caminho).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception) { return "Esse caminho não é válido."; }

        if (completo.Length == 0 || Path.GetPathRoot(completo + Path.DirectorySeparatorChar)?.TrimEnd('\\', '/') == completo)
            return "O CubeBin não mexe na raiz do disco. Escolha uma pasta dentro dela.";

        var home = PastasDoSistema.ObterHome().TrimEnd(Path.DirectorySeparatorChar);
        if (MesmoCaminho(completo, home))
            return "A pasta pessoal inteira é grande demais para organizar. Escolha uma pasta dentro dela.";

        foreach (var proibida in PastasDoSistemaOperacional())
            if (MesmoCaminho(completo, proibida) || EstaDentro(completo, proibida))
                return "Essa é uma pasta do sistema ou de programas. O CubeBin só mexe nas pastas do usuário.";

        foreach (var pasta in config.Pastas)
            if (MesmoCaminho(pasta.CaminhoEfetivo(), completo))
                return $"Essa pasta já está na lista ({pasta.Nome}).";

        return null;
    }

    private static IEnumerable<string> PastasDoSistemaOperacional()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var p in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     })
                if (!string.IsNullOrWhiteSpace(p)) yield return p.TrimEnd('\\');
        }
        else
        {
            foreach (var p in new[] { "/bin", "/boot", "/dev", "/etc", "/lib", "/lib64", "/opt", "/proc", "/root",
                         "/run", "/sbin", "/snap", "/srv", "/sys", "/usr", "/var" })
                yield return p;
            var home = PastasDoSistema.ObterHome();
            yield return Path.Combine(home, ".local");
            yield return Path.Combine(home, ".config");
            yield return Path.Combine(home, ".cache");
        }
    }

    public static bool MesmoCaminho(string a, string b) =>
        string.Equals(Limpar(a), Limpar(b), ComparacaoDeCaminhos);

    /// <summary>True se <paramref name="caminho"/> fica dentro de <paramref name="pasta"/> (ou é ela).</summary>
    public static bool EstaDentro(string caminho, string pasta)
    {
        var c = Limpar(caminho) + Path.DirectorySeparatorChar;
        var p = Limpar(pasta) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, ComparacaoDeCaminhos);
    }

    public static StringComparison ComparacaoDeCaminhos =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Limpar(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return "";
        try { caminho = Path.GetFullPath(caminho); } catch { /* fica como veio */ }
        return caminho.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
