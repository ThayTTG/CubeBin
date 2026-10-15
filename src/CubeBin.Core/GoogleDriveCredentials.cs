namespace CubeBin.Core;

/// <summary>
/// Credenciais OAuth do app "CubeBin", registradas uma única vez no Google Cloud Console (ver
/// README, seção "Configurar a integração com o Google Drive"). Embutidas no código para o
/// usuário final só precisar clicar em "Conectar" — sem criar o próprio projeto no Google Cloud
/// nem lidar com Client ID/Secret manualmente.
///
/// Os valores reais ficam em GoogleDriveCredentials.Secrets.cs, que NÃO é versionado no Git (veja
/// .gitignore) — evita vazar a credencial ao publicar o repositório. Para configurar localmente:
/// copie GoogleDriveCredentials.Secrets.cs.example para GoogleDriveCredentials.Secrets.cs e
/// preencha com os valores reais gerados no Google Cloud Console.
///
/// O Client Secret de um app "Desktop" não é secreto no sentido tradicional — o próprio Google
/// reconhece isso publicamente. A segurança desse tipo de credencial vem do redirecionamento do
/// OAuth ser sempre para localhost, não de esconder este valor. Ainda assim, mantê-lo fora do
/// controle de versão é uma prática padrão saudável (evita, por exemplo, ferramentas automáticas
/// de scan de segredos no GitHub sinalizando o repositório).
/// </summary>
public static partial class GoogleDriveCredentials
{
    /// <summary>False enquanto os placeholders do arquivo .Secrets.cs não forem substituídos pelos valores reais.</summary>
    public static bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(ClientId) && !ClientId.StartsWith("COLE_AQUI", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(ClientSecret) && !ClientSecret.StartsWith("COLE_AQUI", StringComparison.Ordinal);
}
