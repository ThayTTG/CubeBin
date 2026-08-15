using System.Text.Json;
using OrganizadorArquivos.Core.Interop;
using OrganizadorArquivos.Core.Models;

namespace OrganizadorArquivos.Core.Services;

/// <summary>
/// Lê, valida, grava e mantém o config.json. Garante a regra da spec (seção 2):
/// pastas padrão podem ser limpas/restauradas mas nunca removidas do arquivo;
/// pastas personalizadas podem ser criadas/removidas livremente.
/// </summary>
public class ConfigService
{
    private readonly string _caminhoConfig;
    private readonly string _caminhoBackupConfig;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConfigService(string caminhoConfig)
    {
        _caminhoConfig = caminhoConfig;
        _caminhoBackupConfig = caminhoConfig + ".bak";
    }

    public AppConfig CarregarOuCriarPadrao()
    {
        if (!File.Exists(_caminhoConfig))
        {
            var novo = CriarConfigPadrao();
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

    /// <summary>Retorna true se algum campo foi preenchido (e portanto precisa ser salvo).</summary>
    private static bool PreencherCaminhosAusentes(AppConfig config)
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
        if (string.IsNullOrWhiteSpace(config.PastaCredenciaisGoogleDrive))
        {
            config.PastaCredenciaisGoogleDrive = padrao.PastaCredenciaisGoogleDrive;
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

    private static AppConfig CriarConfigPadrao()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseApp = Path.Combine(appData, "OrganizadorArquivos");

        return new AppConfig
        {
            VersaoConfig = 1,
            DiretorioTemporario = Path.Combine(baseApp, "temp"),
            TempoAusenciaMinutos = 10,
            JanelaCancelamentoSegundos = 60,
            ArquivoFilaPendencias = Path.Combine(baseApp, "pendencias.json"),
            ArquivoRecall = Path.Combine(baseApp, "recall.json"),
            PastaCredenciaisGoogleDrive = Path.Combine(baseApp, "google-credentials"),
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
