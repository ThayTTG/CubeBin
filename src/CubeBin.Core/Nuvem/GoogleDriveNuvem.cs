using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using DriveArquivo = Google.Apis.Drive.v3.Data.File;

namespace CubeBin.Core.Nuvem;

/// <summary>
/// O Google Drive (PRD 6.2). O login é feito uma vez (abre o navegador) e o token fica guardado com
/// segurança: DPAPI no Windows, chaveiro do sistema no Linux. O escopo é o mais restrito que existe
/// (drive.file): o CubeBin só enxerga os arquivos que ele mesmo criou — na prática, a pasta CubeBin.
/// As credenciais do app vêm de GoogleDriveCredentials (fora do Git; ver README).
/// </summary>
public sealed class GoogleDriveNuvem : IArmazenamentoNuvem
{
    private static readonly string[] Escopos = { DriveService.Scope.DriveFile };
    private const string Usuario = "usuario";
    private const string Pasta = "application/vnd.google-apps.folder";
    private const string Campos = "id, name, size, md5Checksum, webViewLink";

    private readonly string _pastaCredenciais;
    private DriveService? _servico;

    public GoogleDriveNuvem(string pastaCredenciais) => _pastaCredenciais = pastaCredenciais;

    public static bool CredenciaisDoAppConfiguradas => GoogleDriveCredentials.EstaConfigurado;

    public bool Conectado => _servico is not null;

    /// <summary>Login com o navegador (o clique em "Conectar conta").</summary>
    public async Task ConectarAsync(CancellationToken cancelar = default)
    {
        ExigirCredenciais();
        var credencial = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            Segredos(), Escopos, Usuario, cancelar, CriarArmazenamento());
        Iniciar(credencial);
    }

    /// <summary>
    /// Ao abrir o app: usa o token guardado, se houver, sem abrir o navegador. Se o token estiver
    /// vencido, a primeira chamada à API tenta renovar; se não der, o envio é adiado com aviso.
    /// </summary>
    public async Task<bool> ConectarSilenciosamenteAsync(CancellationToken cancelar = default)
    {
        if (!CredenciaisDoAppConfiguradas) return false;
        var fluxo = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = Segredos(),
            Scopes = Escopos,
            DataStore = CriarArmazenamento(),
        });
        var token = await fluxo.LoadTokenAsync(Usuario, cancelar);
        if (token is null) return false;
        Iniciar(new UserCredential(fluxo, Usuario, token));
        return true;
    }

    public async Task DesconectarAsync()
    {
        _servico = null;
        try { await CriarArmazenamento().ClearAsync(); } catch { /* melhor esforço */ }
        if (Directory.Exists(_pastaCredenciais)) Directory.Delete(_pastaCredenciais, recursive: true);
    }

    private void Iniciar(UserCredential credencial) =>
        _servico = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credencial,
            ApplicationName = "CubeBin",
        });

    private static ClientSecrets Segredos() => new()
    {
        ClientId = GoogleDriveCredentials.ClientId,
        ClientSecret = GoogleDriveCredentials.ClientSecret,
    };

    private IDataStore CriarArmazenamento() =>
        OperatingSystem.IsWindows() ? new DpapiDataStore(_pastaCredenciais) : new LinuxSecretDataStore(_pastaCredenciais);

    private static void ExigirCredenciais()
    {
        if (!CredenciaisDoAppConfiguradas)
            throw new InvalidOperationException(
                "Este CubeBin foi compilado sem as credenciais do Google Drive (ver README, \"Configurar a integração com o Google Drive\").");
    }

    private DriveService Servico => _servico ?? throw new InvalidOperationException("Nenhuma conta do Google Drive conectada.");

    // ---------- IArmazenamentoNuvem ----------

    public async Task<string> GarantirPastaAsync(string caminho, CancellationToken cancelar = default)
    {
        string? paiId = null;
        foreach (var nome in caminho.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var existente = await ProcurarAsync(paiId ?? "root", nome, cancelar, Pasta);
            if (existente is not null)
            {
                paiId = existente.Id;
                continue;
            }
            var criar = Servico.Files.Create(new DriveArquivo
            {
                Name = nome,
                MimeType = Pasta,
                Parents = paiId is null ? null : new List<string> { paiId },
            });
            criar.Fields = "id";
            paiId = (await criar.ExecuteAsync(cancelar)).Id;
        }
        return paiId ?? "root";
    }

    public async Task<ArquivoNuvem> EnviarAsync(string arquivoLocal, string pastaId, string nome, string tipoMime,
        IProgress<long>? progresso = null, CancellationToken cancelar = default)
    {
        await using var leitura = File.OpenRead(arquivoLocal);
        var envio = Servico.Files.Create(new DriveArquivo { Name = nome, Parents = new List<string> { pastaId } }, leitura, tipoMime);
        envio.Fields = Campos;
        envio.ChunkSize = 4 * 1024 * 1024;
        if (progresso is not null) envio.ProgressChanged += p => progresso.Report(p.BytesSent);
        var resultado = await envio.UploadAsync(cancelar);
        if (resultado.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw resultado.Exception ?? new IOException("O envio para o Google Drive não terminou.");
        return Descrever(envio.ResponseBody);
    }

    public async Task<ArquivoNuvem> AtualizarAsync(string id, string arquivoLocal, string tipoMime, CancellationToken cancelar = default)
    {
        await using var leitura = File.OpenRead(arquivoLocal);
        var envio = Servico.Files.Update(new DriveArquivo(), id, leitura, tipoMime);
        envio.Fields = Campos;
        var resultado = await envio.UploadAsync(cancelar);
        if (resultado.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw resultado.Exception ?? new IOException("A atualização no Google Drive não terminou.");
        return Descrever(envio.ResponseBody);
    }

    public async Task<ArquivoNuvem?> ObterAsync(string id, CancellationToken cancelar = default)
    {
        try
        {
            var pedido = Servico.Files.Get(id);
            pedido.Fields = Campos + ", trashed";
            var arquivo = await pedido.ExecuteAsync(cancelar);
            return arquivo.Trashed == true ? null : Descrever(arquivo);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<ArquivoNuvem?> ProcurarAsync(string pastaId, string nome, CancellationToken cancelar = default) =>
        ProcurarAsync(pastaId, nome, cancelar, null);

    private async Task<ArquivoNuvem?> ProcurarAsync(string pastaId, string nome, CancellationToken cancelar, string? tipo)
    {
        var lista = Servico.Files.List();
        lista.Q = $"name = '{nome.Replace("\\", "\\\\").Replace("'", "\\'")}' and '{pastaId}' in parents and trashed = false" +
                  (tipo is null ? "" : $" and mimeType = '{tipo}'");
        lista.Fields = $"files({Campos})";
        lista.Spaces = "drive";
        var resposta = await lista.ExecuteAsync(cancelar);
        var primeiro = resposta.Files?.FirstOrDefault();
        return primeiro is null ? null : Descrever(primeiro);
    }

    public async Task BaixarAsync(string id, string destinoLocal, IProgress<long>? progresso = null, CancellationToken cancelar = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinoLocal)!);
        var pedido = Servico.Files.Get(id);
        if (progresso is not null) pedido.MediaDownloader.ProgressChanged += p => progresso.Report(p.BytesDownloaded);
        await using var saida = new FileStream(destinoLocal, FileMode.Create, FileAccess.Write);
        var resultado = await pedido.DownloadAsync(saida, cancelar);
        if (resultado.Status != Google.Apis.Download.DownloadStatus.Completed)
            throw resultado.Exception ?? new IOException("O download do Google Drive não terminou.");
    }

    public async Task<InfoContaNuvem> ContaAsync(CancellationToken cancelar = default)
    {
        var pedido = Servico.About.Get();
        pedido.Fields = "user, storageQuota";
        var sobre = await pedido.ExecuteAsync(cancelar);
        return new InfoContaNuvem(sobre.User?.EmailAddress ?? "conta do Google", sobre.StorageQuota?.Usage ?? 0, sobre.StorageQuota?.Limit);
    }

    private static ArquivoNuvem Descrever(DriveArquivo a) => new(a.Id, a.Name ?? "", a.Size ?? 0, a.Md5Checksum, a.WebViewLink);
}
