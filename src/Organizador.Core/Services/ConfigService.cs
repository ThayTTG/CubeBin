using System.Text.Json;
using System.Text.Json.Nodes;
using Comum.Interop;
using Organizador.Core.Models;

namespace Organizador.Core.Services;

/// <summary>
/// Lê, valida, grava e mantém o config.json do organizador. Garante a regra da spec (seção 2):
/// pastas padrão podem ser limpas/restauradas mas nunca removidas do arquivo;
/// pastas personalizadas podem ser criadas/removidas livremente.
///
/// Na primeira vez que abre, se a pessoa já usava o CubeBin 2.x (que fazia tudo num app só), traz
/// de lá as pastas, as regras, o agendamento e o histórico, para nada se perder na separação.
/// </summary>
public class ConfigService
{
    private readonly string _caminhoConfig;
    private readonly string _caminhoBackupConfig;
    private readonly string _pastaDados;
    private readonly string? _pastaDaVersao2;

    /// <summary>
    /// Preenchido quando a migração da versão 2.x precisou mudar alguma coisa que a pessoa deve
    /// saber (ex.: regras que mandavam arquivos para o Google Drive). Null se não houve nada a avisar.
    /// O app mostra uma vez e chama <see cref="LimparAvisoDeMigracao"/>.
    /// </summary>
    public string? AvisoDeMigracao { get; private set; }

    /// <summary>True quando, nesta abertura, as configurações vieram do CubeBin 2.x.</summary>
    public bool MigrouDaVersao2 { get; private set; }

    public void LimparAvisoDeMigracao()
    {
        AvisoDeMigracao = null;
        MigrouDaVersao2 = false;
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <param name="caminhoConfig">Onde fica o config.json do organizador. Os outros arquivos dele
    /// (pendências, histórico, registros em CSV, pasta temporária) ficam na mesma pasta.</param>
    /// <param name="pastaDaVersao2">Onde procurar o config do CubeBin 2.x para migrar
    /// (normalmente %LOCALAPPDATA%\CubeBin). Null desliga a migração (usado nos testes).</param>
    public ConfigService(string caminhoConfig, string? pastaDaVersao2 = null)
    {
        _caminhoConfig = caminhoConfig;
        _caminhoBackupConfig = caminhoConfig + ".bak";
        _pastaDados = Path.GetDirectoryName(Path.GetFullPath(caminhoConfig))!;
        _pastaDaVersao2 = pastaDaVersao2;
    }

    public AppConfig CarregarOuCriarPadrao()
    {
        if (!File.Exists(_caminhoConfig))
        {
            var novo = TentarMigrarDaVersao2() ?? CriarConfigPadrao();
            Salvar(novo);
            return novo;
        }

        try
        {
            var json = File.ReadAllText(_caminhoConfig);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null) throw new InvalidDataException("config.json vazio ou inválido.");

            // Migração: config.json criados por versões anteriores a um campo de caminho ser
            // adicionado (ex.: ArquivoRecall na 1.12) ficam com esse campo vazio, porque a chave
            // simplesmente não existe no JSON salvo. Preenche com o padrão e persiste a correção.
            if (PreencherCaminhosAusentes(config))
                Salvar(config);

            return config;
        }
        catch (Exception)
        {
            // Config corrompido: tenta recuperar do .bak antes de recriar do zero.
            if (File.Exists(_caminhoBackupConfig))
            {
                var jsonBackup = File.ReadAllText(_caminhoBackupConfig);
                var configBackup = JsonSerializer.Deserialize<AppConfig>(jsonBackup, JsonOptions);
                if (configBackup is not null)
                {
                    PreencherCaminhosAusentes(configBackup);
                    Salvar(configBackup);
                    return configBackup;
                }
            }

            var padrao = CriarConfigPadrao();
            Salvar(padrao);
            return padrao;
        }
    }

    /// <summary>
    /// Traz a parte do organizador do config do CubeBin 2.x. Os caminhos de pendências, histórico,
    /// registros em CSV e pasta temporária continuam apontando para onde já estavam (dentro da
    /// pasta do CubeBin): assim os arquivos pendentes e o histórico de "devolver" seguem
    /// funcionando sem precisar mover nada.
    /// </summary>
    private AppConfig? TentarMigrarDaVersao2()
    {
        if (_pastaDaVersao2 is null) return null;

        try
        {
            var copia = MigracaoDaVersao2.GarantirCopiaDoConfigAntigo(_pastaDaVersao2);
            if (copia is null) return null;

            var raiz = MigracaoDaVersao2.LerJson(copia);
            if (raiz is null) return null;

            var convertidas = ConverterRegrasDoDrive(raiz);
            var config = raiz.Deserialize<AppConfig>(JsonOptions);
            if (config is null) return null;

            PreencherCaminhosAusentes(config);
            MigrouDaVersao2 = true;

            if (convertidas.Count > 0)
            {
                AvisoDeMigracao =
                    "Estas regras, que antes mandavam arquivos para o Google Drive, passaram a " +
                    "guardá-los numa pasta do computador:\n\n" + string.Join("\n", convertidas) +
                    "\n\nOs arquivos que já estavam no Google Drive continuam lá.";
            }

            return config;
        }
        catch (Exception)
        {
            // Se a migração der errado, começa do zero em vez de travar: o config.json antigo e a
            // cópia "config-2.x.json" continuam intactos na pasta do CubeBin.
            return null;
        }
    }

    /// <summary>
    /// Regras da versão 2.x com destino no Google Drive viram regras para uma pasta local com o
    /// mesmo nome (o que estiver em "destino"; se vazio, a última pasta do caminho no Drive; se
    /// nada disso, o nome da regra). Mexe no JSON antes de ler, porque o organizador nem conhece
    /// mais esses campos. Devolve uma linha legível por regra convertida.
    /// </summary>
    public static List<string> ConverterRegrasDoDrive(JsonObject raiz)
    {
        var convertidas = new List<string>();

        void ConverterDicionario(JsonNode? regras, string? nomeDaPasta)
        {
            if (regras is not JsonObject dicionario) return;

            foreach (var (chave, no) in dicionario)
            {
                if (no is not JsonObject regra) continue;
                if (!EhDestinoNoDrive(regra["tipoDestino"])) continue;

                var destino = regra["destino"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(destino))
                {
                    var caminhoDrive = regra["caminhoDrive"]?.GetValue<string>() ?? "";
                    destino = caminhoDrive.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                }
                if (string.IsNullOrWhiteSpace(destino)) destino = chave;

                regra["destino"] = destino;
                regra.Remove("tipoDestino");
                regra.Remove("caminhoDrive");

                convertidas.Add(nomeDaPasta is null
                    ? $"• {chave} → pasta \"{destino}\""
                    : $"• {chave} (só em {nomeDaPasta}) → pasta \"{destino}\"");
            }
        }

        ConverterDicionario(raiz["regrasExtensao"], null);

        foreach (var lista in new[] { raiz["pastasPadrao"], raiz["pastasPersonalizadas"] })
        {
            if (lista is not JsonArray pastas) continue;
            foreach (var pasta in pastas.OfType<JsonObject>())
                ConverterDicionario(pasta["regrasPersonalizadas"], pasta["nome"]?.GetValue<string>() ?? "uma pasta");
        }

        return convertidas;
    }

    /// <summary>A 2.x gravava o tipo como número (1 = Google Drive); aceita o texto também, por garantia.</summary>
    private static bool EhDestinoNoDrive(JsonNode? tipo)
    {
        if (tipo is not JsonValue valor) return false;
        if (valor.TryGetValue<int>(out var numero)) return numero == 1;
        if (valor.TryGetValue<string>(out var texto))
            return texto.Replace(" ", "").Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>Retorna true se algum campo foi preenchido (e portanto precisa ser salvo).</summary>
    private bool PreencherCaminhosAusentes(AppConfig config)
    {
        var padrao = CriarConfigPadrao();
        var alterou = false;

        if (string.IsNullOrWhiteSpace(config.DiretorioTemporario))
        {
            config.DiretorioTemporario = padrao.DiretorioTemporario;
            alterou = true;
        }
        if (string.IsNullOrWhiteSpace(config.ArquivoFilaPendencias))
        {
            config.ArquivoFilaPendencias = padrao.ArquivoFilaPendencias;
            alterou = true;
        }
        if (string.IsNullOrWhiteSpace(config.ArquivoRecall))
        {
            config.ArquivoRecall = padrao.ArquivoRecall;
            alterou = true;
        }
        if (string.IsNullOrWhiteSpace(config.Backup?.DiretorioCsv))
        {
            config.Backup ??= new BackupConfig();
            config.Backup.DiretorioCsv = padrao.Backup.DiretorioCsv;
            alterou = true;
        }

        // Corrige pastas padrão cujo caminho salvo não existe mais — típico quando o OneDrive
        // redireciona Documentos/Área de Trabalho para dentro de si mesmo ("Backup do PC" / Known
        // Folder Move) depois que o config.json já tinha sido criado com o caminho antigo, não
        // redirecionado. Só troca se o caminho novo calculado realmente existir no disco.
        foreach (var pastaAtual in config.PastasPadrao)
        {
            if (Directory.Exists(pastaAtual.Caminho)) continue;

            var correspondente = padrao.PastasPadrao.FirstOrDefault(p => p.Id == pastaAtual.Id);
            if (correspondente is null || !Directory.Exists(correspondente.Caminho)) continue;

            pastaAtual.Caminho = correspondente.Caminho;
            alterou = true;
        }

        // Migração desta versão: Documentos e Área de Trabalho deixaram de ser pastas padrão.
        // Configs antigos ainda têm essas duas com Removivel=false, o que travava o botão
        // "Remover" na UI — removidas aqui de vez, para quem já tinha essas pastas configuradas.
        var idsPadraoObsoletos = new[] { "documentos", "area_trabalho" };
        if (config.PastasPadrao.RemoveAll(p => idsPadraoObsoletos.Contains(p.Id)) > 0)
            alterou = true;

        return alterou;
    }

    public void Salvar(AppConfig config)
    {
        var diretorio = Path.GetDirectoryName(_caminhoConfig);
        if (!string.IsNullOrEmpty(diretorio))
            Directory.CreateDirectory(diretorio);

        // Mantém uma cópia .bak antes de sobrescrever, para recuperação em caso de corrupção futura.
        if (File.Exists(_caminhoConfig))
            File.Copy(_caminhoConfig, _caminhoBackupConfig, overwrite: true);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_caminhoConfig, json);
    }

    /// <summary>Reseta os campos de uma pasta padrão (extensões, subpastas, regras) sem remover o registro.</summary>
    public void LimparPastaPadrao(AppConfig config, string pastaId)
    {
        var pasta = config.PastasPadrao.FirstOrDefault(p => p.Id == pastaId)
            ?? throw new InvalidOperationException($"Pasta padrão '{pastaId}' não encontrada.");

        pasta.ExtensoesMonitoradas = new List<string> { "*" };
        pasta.OrganizarSubpastas = false;
        pasta.Prefixo = null;
    }

    /// <summary>Restaura uma pasta padrão ao estado original de fábrica.</summary>
    public void RestaurarPastaPadrao(AppConfig config, string pastaId)
    {
        var original = CriarConfigPadrao().PastasPadrao.FirstOrDefault(p => p.Id == pastaId);
        var atual = config.PastasPadrao.FirstOrDefault(p => p.Id == pastaId);
        if (original is null || atual is null)
            throw new InvalidOperationException($"Pasta padrão '{pastaId}' não encontrada.");

        atual.ExtensoesMonitoradas = original.ExtensoesMonitoradas;
        atual.OrganizarSubpastas = original.OrganizarSubpastas;
        atual.Prefixo = original.Prefixo;
    }

    /// <summary>Remove uma pasta personalizada. Lança exceção se tentarem remover uma pasta padrão.</summary>
    public void RemoverPastaPersonalizada(AppConfig config, string pastaId)
    {
        var pasta = config.PastasPersonalizadas.FirstOrDefault(p => p.Id == pastaId);
        if (pasta is null)
            throw new InvalidOperationException(
                $"Pasta '{pastaId}' não é uma pasta personalizada ou não existe. Pastas padrão não podem ser removidas.");

        config.PastasPersonalizadas.Remove(pasta);
    }

    public void AdicionarPastaPersonalizada(AppConfig config, PastaMonitorada pasta)
    {
        pasta.Removivel = true;
        config.PastasPersonalizadas.Add(pasta);
    }

    private AppConfig CriarConfigPadrao()
    {
        var baseApp = _pastaDados;

        return new AppConfig
        {
            VersaoConfig = 1,
            DiretorioTemporario = Path.Combine(baseApp, "temp"),
            TempoAusenciaMinutos = 10,
            JanelaCancelamentoSegundos = 60,
            ArquivoFilaPendencias = Path.Combine(baseApp, "pendencias.json"),
            ArquivoRecall = Path.Combine(baseApp, "recall.json"),
            PastasPadrao = new List<PastaMonitorada>
            {
                new() { Id = "downloads", Nome = "Downloads", Caminho = KnownFolders.ObterDownloads(), Removivel = false },
            },
            PastasPersonalizadas = new List<PastaMonitorada>(),
            RegrasExtensao = new Dictionary<string, RegraExtensao>
            {
                ["compactados"] = new() { Chave = "compactados", Extensoes = new() { ".zip", ".rar", ".7z" }, Destino = "Compactados" },
                ["executaveis"] = new() { Chave = "executaveis", Extensoes = new() { ".exe", ".msi", ".bat" }, Destino = "Instaladores" },
                ["imagens"] = new() { Chave = "imagens", Extensoes = new() { ".jpg", ".jpeg", ".png", ".gif", ".webp" }, Destino = "Imagens" },
                ["documentos_office"] = new() { Chave = "documentos_office", Extensoes = new() { ".pdf", ".docx", ".xlsx", ".pptx" }, Destino = "Documentos_Office" },
                ["videos"] = new() { Chave = "videos", Extensoes = new() { ".mp4", ".mkv", ".avi" }, Destino = "Vídeos" },
                ["audio"] = new() { Chave = "audio", Extensoes = new() { ".mp3", ".wav", ".flac" }, Destino = "Áudio" },
            },
            Backup = new BackupConfig
            {
                DiretorioCsv = Path.Combine(baseApp, "backups"),
                ManterUltimosN = 30
            }
        };
    }
}
