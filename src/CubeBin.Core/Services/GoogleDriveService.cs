using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using CubeBin.Core;

namespace CubeBin.Core.Services;

/// <summary>
/// Envia, baixa e apaga arquivos no Google Drive, e cuida do login OAuth (uma vez só — o token
/// fica salvo em disco e se renova sozinho depois disso, sem precisar abrir o navegador de novo).
/// Escopo usado é o mais restrito possível (drive.file): só dá acesso a arquivos que o próprio
/// app criar/enviar, nunca ao Google Drive inteiro do usuário.
/// </summary>
public class GoogleDriveService
{
    private static readonly string[] Escopos = { DriveService.Scope.DriveFile };
    private const string NomeAplicativo = "CubeBin";

    private readonly string _pastaCredenciais;
    private DriveService? _servico;

    public GoogleDriveService(string pastaCredenciais)
    {
        _pastaCredenciais = pastaCredenciais;
    }

    public bool EstaConectado() => _servico is not null;

    /// <summary>
    /// Faz login no Google usando as credenciais embutidas do app (abre o navegador só se não
    /// houver um token salvo/renovável). Chamado tanto pelo clique manual em "Conectar" quanto
    /// silenciosamente na inicialização do app, para restaurar uma sessão já autorizada
    /// anteriormente sem exigir login de novo.
    /// </summary>
    public async Task ConectarAsync()
    {
        if (!GoogleDriveCredentials.EstaConfigurado)
            throw new InvalidOperationException(
                "As credenciais do Google Drive ainda não foram configuradas em GoogleDriveCredentials.cs. " +
                "Veja o README, seção \"Configurar a integração com o Google Drive\".");

        var segredo = new ClientSecrets
        {
            ClientId = GoogleDriveCredentials.ClientId,
            ClientSecret = GoogleDriveCredentials.ClientSecret
        };

        var credencial = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            segredo,
            Escopos,
            "usuario",
            CancellationToken.None,
            new DpapiDataStore(_pastaCredenciais));

        _servico = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credencial,
            ApplicationName = NomeAplicativo
        });
    }

    public void Desconectar()
    {
        _servico = null;
        if (Directory.Exists(_pastaCredenciais))
            Directory.Delete(_pastaCredenciais, recursive: true);
    }

    public record InfoConta(string Email, long UsadoBytes, long? TotalBytes);

    public async Task<InfoConta> ObterInfoContaAsync()
    {
        var servico = ObterServicoOuFalhar();
        var pedido = servico.About.Get();
        pedido.Fields = "user,storageQuota";
        var about = await pedido.ExecuteAsync();

        return new InfoConta(
            about.User?.EmailAddress ?? "desconhecido",
            about.StorageQuota?.Usage ?? 0,
            about.StorageQuota?.Limit);
    }

    private DriveService ObterServicoOuFalhar()
    {
        if (_servico is null)
            throw new InvalidOperationException(
                "Não conectado ao Google Drive. Clique em \"Conectar ao Google Drive\" nas Configurações gerais primeiro.");
        return _servico;
    }

    /// <summary>Resolve (criando se preciso) uma estrutura de pastas no Drive, ex.: "Organizador/Compactados".
    /// Retorna o ID da última pasta da cadeia.</summary>
    public async Task<string> ObterOuCriarPastaAsync(string caminhoDrive)
    {
        var servico = ObterServicoOuFalhar();
        var segmentos = caminhoDrive.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? paiId = null;

        foreach (var nome in segmentos)
        {
            var nomeEscapado = nome.Replace("'", "\\'");
            var consulta = $"name = '{nomeEscapado}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false and "
                + (paiId is null ? "'root' in parents" : $"'{paiId}' in parents");

            var listagem = servico.Files.List();
            listagem.Q = consulta;
            listagem.Fields = "files(id, name)";
            listagem.Spaces = "drive";
            var resultado = await listagem.ExecuteAsync();

            if (resultado.Files is { Count: > 0 })
            {
                paiId = resultado.Files[0].Id;
                continue;
            }

            var novaPasta = new Google.Apis.Drive.v3.Data.File
            {
                Name = nome,
                MimeType = "application/vnd.google-apps.folder",
                Parents = paiId is null ? null : new List<string> { paiId }
            };

            var criacao = servico.Files.Create(novaPasta);
            criacao.Fields = "id";
            var pastaCriada = await criacao.ExecuteAsync();
            paiId = pastaCriada.Id;
        }

        return paiId ?? "root";
    }

    /// <summary>Disparado durante upload/download: nome do arquivo e velocidade estimada em Mbps.</summary>
    public event Action<string, double>? ProgressoAlterado;

    /// <summary>
    /// Implementação compartilhada de upload — EnviarArquivoAsync e EnviarArquivoComLinkAsync só
    /// diferem no tipo MIME e nos campos pedidos à API, então toda a lógica de fato (resolver
    /// pasta, abrir stream, acompanhar progresso, checar conclusão) vive aqui uma vez só.
    /// </summary>
    private async Task<Google.Apis.Drive.v3.Data.File> EnviarArquivoInternoAsync(
        string caminhoLocal, string caminhoDrive, string tipoMime, string campos)
    {
        var servico = ObterServicoOuFalhar();
        var pastaId = await ObterOuCriarPastaAsync(caminhoDrive);
        var nomeArquivo = Path.GetFileName(caminhoLocal);

        var metadados = new Google.Apis.Drive.v3.Data.File
        {
            Name = nomeArquivo,
            Parents = new List<string> { pastaId }
        };

        await using var stream = new FileStream(caminhoLocal, FileMode.Open, FileAccess.Read);
        var upload = servico.Files.Create(metadados, stream, tipoMime);
        upload.Fields = campos;

        var inicio = DateTime.Now;
        upload.ProgressChanged += p =>
        {
            var segundos = Math.Max(0.1, (DateTime.Now - inicio).TotalSeconds);
            var mbps = p.BytesSent * 8.0 / segundos / 1_000_000.0;
            ProgressoAlterado?.Invoke(nomeArquivo, mbps);
        };

        var progresso = await upload.UploadAsync();
        if (progresso.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw new IOException($"Upload para o Google Drive não completou: {progresso.Exception?.Message ?? "motivo desconhecido"}");

        return upload.ResponseBody;
    }

    /// <summary>Envia um arquivo local para uma pasta do Drive (criando a pasta se preciso). Retorna o ID do arquivo criado.</summary>
    public async Task<string> EnviarArquivoAsync(string caminhoLocal, string caminhoDrive)
    {
        var resposta = await EnviarArquivoInternoAsync(caminhoLocal, caminhoDrive, "application/octet-stream", "id");
        return resposta.Id;
    }

    /// <summary>Baixa um arquivo do Drive de volta para um caminho local (usado ao restaurar via Recall).</summary>
    public async Task BaixarArquivoAsync(string idArquivo, string caminhoDestinoLocal)
    {
        var servico = ObterServicoOuFalhar();

        var diretorio = Path.GetDirectoryName(caminhoDestinoLocal);
        if (!string.IsNullOrEmpty(diretorio))
            Directory.CreateDirectory(diretorio);

        var nomeArquivo = Path.GetFileName(caminhoDestinoLocal);
        var pedido = servico.Files.Get(idArquivo);

        var inicio = DateTime.Now;
        pedido.MediaDownloader.ProgressChanged += p =>
        {
            var segundos = Math.Max(0.1, (DateTime.Now - inicio).TotalSeconds);
            var mbps = p.BytesDownloaded * 8.0 / segundos / 1_000_000.0;
            ProgressoAlterado?.Invoke(nomeArquivo, mbps);
        };

        await using var saida = new FileStream(caminhoDestinoLocal, FileMode.Create, FileAccess.Write);
        await pedido.DownloadAsync(saida);
    }

    /// <summary>Apaga um arquivo do Drive (usado depois de um download bem-sucedido, ao restaurar via Recall).</summary>
    public async Task ExcluirArquivoAsync(string idArquivo)
    {
        var servico = ObterServicoOuFalhar();
        await servico.Files.Delete(idArquivo).ExecuteAsync();
    }

    /// <summary>
    /// Igual a EnviarArquivoAsync, mas também devolve o link de visualização no Drive (usado pelos
    /// pacotes de backup da Camada 3/4, para o botão "Abrir no Google Drive" nos detalhes de um
    /// Cubo). Mantido como método próprio — em vez de mudar o retorno de EnviarArquivoAsync — para
    /// não alterar o contrato já em uso pelo pipeline principal de organização.
    /// </summary>
    public async Task<(string Id, string? LinkVisualizacao)> EnviarArquivoComLinkAsync(string caminhoLocal, string caminhoDrive)
    {
        var resposta = await EnviarArquivoInternoAsync(caminhoLocal, caminhoDrive, "application/zip", "id, webViewLink");
        return (resposta.Id, resposta.WebViewLink);
    }
}
