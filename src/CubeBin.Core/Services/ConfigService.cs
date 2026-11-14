using System.Text.Json;
using System.Text.Json.Nodes;
using Comum;
using Comum.Interop;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// Lê, grava e mantém o config.json do CubeBin (a parte da faxina e da nuvem). Guarda uma cópia
/// .bak antes de cada gravação, para recuperar se o arquivo corromper.
///
/// Quem atualiza do CubeBin 2.x tem, nesse mesmo caminho, o config antigo com tudo junto
/// (organização + faxina). Antes de reescrevê-lo, a <see cref="MigracaoDaVersao2"/> guarda uma
/// cópia intacta dele, para o organizador (agora um app separado) ainda conseguir ler a parte dele.
/// </summary>
public class ConfigService
{
    private readonly string _caminhoConfig;
    private readonly string _caminhoBackupConfig;
    private readonly string _pastaDados;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="caminhoConfig">Normalmente %LOCALAPPDATA%\CubeBin\config.json. Os outros
    /// arquivos do CubeBin (sugestões, cubos, login do Google, registros) ficam na mesma pasta.</param>
    public ConfigService(string caminhoConfig)
    {
        _caminhoConfig = caminhoConfig;
        _caminhoBackupConfig = caminhoConfig + ".bak";
        _pastaDados = Path.GetDirectoryName(Path.GetFullPath(caminhoConfig))!;
    }

    public AppConfig CarregarOuCriarPadrao()
    {
        // Veio do CubeBin 2.x? Guarda a cópia intacta para o organizador e migra a parte da faxina.
        if (MigracaoDaVersao2.EhConfigDaVersao2(_caminhoConfig))
        {
            var migrado = TentarMigrarDaVersao2();
            if (migrado is not null)
            {
                Salvar(migrado);
                return migrado;
            }
        }

        if (!File.Exists(_caminhoConfig))
        {
            var novo = CriarConfigPadrao();
            Salvar(novo);
            return novo;
        }

        try
        {
            var json = File.ReadAllText(_caminhoConfig);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
                ?? throw new InvalidDataException("config.json vazio ou inválido.");

            if (PreencherCaminhosAusentes(config))
                Salvar(config);

            return config;
        }
        catch (Exception)
        {
            // Config corrompido: tenta recuperar do .bak antes de recriar do zero.
            var configBackup = LerBackup();
            if (configBackup is not null)
            {
                PreencherCaminhosAusentes(configBackup);
                Salvar(configBackup);
                return configBackup;
            }

            var padrao = CriarConfigPadrao();
            Salvar(padrao);
            return padrao;
        }
    }

    private AppConfig? LerBackup()
    {
        try
        {
            if (!File.Exists(_caminhoBackupConfig)) return null;
            if (MigracaoDaVersao2.EhConfigDaVersao2(_caminhoBackupConfig)) return null; // .bak ainda da 2.x
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_caminhoBackupConfig), JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Traz da 2.x: o agente (ligado ou não, limites, horários), o login do Google, os cubos, as
    /// sugestões, o tempo de ausência e o "abrir com o Windows". As pastas que o organizador
    /// acompanhava viram as pastas que o agente analisa (era delas que ele já tirava as sugestões).
    /// Os registros do que for apagado daqui para frente vão para uma pasta própria do CubeBin.
    /// </summary>
    private AppConfig? TentarMigrarDaVersao2()
    {
        try
        {
            var copia = MigracaoDaVersao2.GarantirCopiaDoConfigAntigo(_pastaDados);
            if (copia is null) return null;

            var raiz = MigracaoDaVersao2.LerJson(copia);
            if (raiz is null) return null;

            // O "backup" da 2.x era o histórico da organização: fica com o organizador.
            raiz.Remove("backup");
            var config = raiz.Deserialize<AppConfig>(JsonOptions);
            if (config is null) return null;

            config.VersaoConfig = 3;
            config.PastasAnalisadas = LerPastasDaVersao2(raiz);
            PreencherCaminhosAusentes(config);
            return config;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>As pastas padrão e personalizadas da 2.x, sem repetir o mesmo caminho.</summary>
    public static List<PastaAnalisada> LerPastasDaVersao2(JsonObject raiz)
    {
        var pastas = new List<PastaAnalisada>();

        foreach (var lista in new[] { raiz["pastasPadrao"], raiz["pastasPersonalizadas"] })
        {
            if (lista is not JsonArray itens) continue;

            foreach (var item in itens.OfType<JsonObject>())
            {
                var caminho = item["caminho"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(caminho)) continue;
                if (pastas.Any(p => p.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase))) continue;

                var nome = item["nome"]?.GetValue<string>();
                pastas.Add(new PastaAnalisada
                {
                    Id = item["id"]?.GetValue<string>() is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N")[..8],
                    Nome = string.IsNullOrWhiteSpace(nome) ? Path.GetFileName(caminho.TrimEnd('\\', '/')) : nome,
                    Caminho = caminho
                });
            }
        }

        return pastas;
    }

    public void Salvar(AppConfig config)
    {
        Directory.CreateDirectory(_pastaDados);

        // Mantém uma cópia .bak antes de sobrescrever, para recuperação em caso de corrupção futura.
        if (File.Exists(_caminhoConfig))
            File.Copy(_caminhoConfig, _caminhoBackupConfig, overwrite: true);

        File.WriteAllText(_caminhoConfig, JsonSerializer.Serialize(config, JsonOptions));
    }

    public void AdicionarPasta(AppConfig config, string caminho)
    {
        if (config.PastasAnalisadas.Any(p => p.Caminho.Equals(caminho, StringComparison.OrdinalIgnoreCase)))
            return;

        var nome = Path.GetFileName(caminho.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        config.PastasAnalisadas.Add(new PastaAnalisada
        {
            Nome = string.IsNullOrWhiteSpace(nome) ? caminho : nome,
            Caminho = caminho
        });
    }

    public void RemoverPasta(AppConfig config, string id) =>
        config.PastasAnalisadas.RemoveAll(p => p.Id == id);

    /// <summary>Retorna true se algum campo foi preenchido (e portanto precisa ser salvo).</summary>
    private bool PreencherCaminhosAusentes(AppConfig config)
    {
        var padrao = CriarConfigPadrao();
        var alterou = false;

        if (string.IsNullOrWhiteSpace(config.ArquivoSugestoes))
        {
            config.ArquivoSugestoes = padrao.ArquivoSugestoes;
            alterou = true;
        }
        if (string.IsNullOrWhiteSpace(config.ArquivoPacotesBackup))
        {
            config.ArquivoPacotesBackup = padrao.ArquivoPacotesBackup;
            alterou = true;
        }
        if (string.IsNullOrWhiteSpace(config.PastaCredenciaisGoogleDrive))
        {
            config.PastaCredenciaisGoogleDrive = padrao.PastaCredenciaisGoogleDrive;
            alterou = true;
        }
        if (string.IsNullOrWhiteSpace(config.Backup?.DiretorioCsv))
        {
            config.Backup ??= new BackupConfig();
            config.Backup.DiretorioCsv = padrao.Backup.DiretorioCsv;
            if (config.Backup.ManterUltimosN <= 0) config.Backup.ManterUltimosN = padrao.Backup.ManterUltimosN;
            alterou = true;
        }

        return alterou;
    }

    private AppConfig CriarConfigPadrao() => new()
    {
        TempoAusenciaMinutos = 10,
        ArquivoSugestoes = Path.Combine(_pastaDados, "sugestoes.json"),
        ArquivoPacotesBackup = Path.Combine(_pastaDados, "pacotes.json"),
        PastaCredenciaisGoogleDrive = Path.Combine(_pastaDados, "google-credentials"),
        PastasAnalisadas = new List<PastaAnalisada>
        {
            new() { Id = "downloads", Nome = "Downloads", Caminho = KnownFolders.ObterDownloads() },
        },
        Backup = new BackupConfig
        {
            // "registros", e não "backups": o histórico da organização (da 2.x) continua em "backups".
            DiretorioCsv = Path.Combine(_pastaDados, "registros"),
            ManterUltimosN = 30
        }
    };
}
