using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Util.Store;

namespace CubeBin.Core.Services;

/// <summary>
/// Substitui o FileDataStore padrão do Google (que grava o token OAuth em texto puro) por uma
/// versão que criptografa cada valor com DPAPI antes de gravar em disco — ligado à conta do
/// Windows do usuário atual, ilegível por outro usuário/processo sem passar pela mesma sessão.
/// Atende ao requisito "sem armazenar token bruto" da Camada 3.
/// </summary>
public class DpapiDataStore : IDataStore
{
    private readonly string _pasta;

    public DpapiDataStore(string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(_pasta);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var caminho = ObterCaminho(key);
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protegido = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(caminho, protegido);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var caminho = ObterCaminho(key);
        if (File.Exists(caminho))
            File.Delete(caminho);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var caminho = ObterCaminho(key);
        if (!File.Exists(caminho))
            return Task.FromResult(default(T)!);

        var protegido = File.ReadAllBytes(caminho);
        var bytes = ProtectedData.Unprotect(protegido, null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(bytes);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json)!);
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_pasta))
        {
            foreach (var arquivo in Directory.GetFiles(_pasta))
                File.Delete(arquivo);
        }
        return Task.CompletedTask;
    }

    private string ObterCaminho(string key) => Path.Combine(_pasta, $"{key}.dpapi");
}
