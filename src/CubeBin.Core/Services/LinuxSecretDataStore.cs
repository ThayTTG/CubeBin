using System.Text.Json;
using CubeBin.Core.Interop;
using Google.Apis.Util.Store;

namespace CubeBin.Core.Services;

/// <summary>
/// O par do DpapiDataStore no Linux: guarda o token OAuth do Google Drive no chaveiro do sistema
/// (GNOME Keyring / KWallet, via libsecret e o comando secret-tool), que é o que cumpre o papel do
/// DPAPI por lá — "sem token bruto" (Camada 3).
///
/// Se o chaveiro não estiver disponível (secret-tool não instalado ou sem serviço de segredos),
/// o token vai para um arquivo legível só pelo próprio usuário (permissão 600), como fazem as
/// ferramentas de linha de comando do Google. Na pasta ficam só marcadores (.libsecret) dizendo
/// quais chaves estão no chaveiro, para o ClearAsync saber o que apagar.
/// </summary>
public class LinuxSecretDataStore : IDataStore
{
    private const string Atributo = "aplicativo";
    private const string ValorAtributo = "cubebin-google-drive";
    private static readonly TimeSpan LimiteChaveiro = TimeSpan.FromSeconds(60);

    private readonly string _pasta;

    public LinuxSecretDataStore(string pasta)
    {
        _pasta = pasta;
        Directory.CreateDirectory(_pasta);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);

        if (GravarNoChaveiro(key, json))
        {
            File.WriteAllText(CaminhoMarcador(key), "");
            ApagarSeExistir(CaminhoArquivo(key));
        }
        else
        {
            GravarArquivoPrivado(CaminhoArquivo(key), json);
            ApagarSeExistir(CaminhoMarcador(key));
        }

        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        string? json = null;

        if (File.Exists(CaminhoMarcador(key)))
            json = LerDoChaveiro(key);

        if (json is null && File.Exists(CaminhoArquivo(key)))
            json = File.ReadAllText(CaminhoArquivo(key));

        if (string.IsNullOrWhiteSpace(json))
            return Task.FromResult(default(T)!);

        return Task.FromResult(JsonSerializer.Deserialize<T>(json)!);
    }

    public Task DeleteAsync<T>(string key)
    {
        ApagarDoChaveiro(key);
        ApagarSeExistir(CaminhoMarcador(key));
        ApagarSeExistir(CaminhoArquivo(key));
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (!Directory.Exists(_pasta))
            return Task.CompletedTask;

        foreach (var marcador in Directory.GetFiles(_pasta, "*.libsecret"))
            ApagarDoChaveiro(Path.GetFileNameWithoutExtension(marcador));

        foreach (var arquivo in Directory.GetFiles(_pasta))
            File.Delete(arquivo);

        return Task.CompletedTask;
    }

    private string CaminhoMarcador(string key) => Path.Combine(_pasta, $"{key}.libsecret");
    private string CaminhoArquivo(string key) => Path.Combine(_pasta, $"{key}.json");

    private static bool GravarNoChaveiro(string key, string json)
    {
        var resultado = ComandoExterno.Executar("secret-tool",
            new[] { "store", "--label=CubeBin (Google Drive)", Atributo, ValorAtributo, "chave", key },
            entrada: json, limite: LimiteChaveiro);
        return resultado is { Codigo: 0 };
    }

    private static string? LerDoChaveiro(string key)
    {
        var resultado = ComandoExterno.Executar("secret-tool",
            new[] { "lookup", Atributo, ValorAtributo, "chave", key }, limite: LimiteChaveiro);
        return resultado is { Codigo: 0 } && !string.IsNullOrWhiteSpace(resultado.Saida) ? resultado.Saida : null;
    }

    private static void ApagarDoChaveiro(string key) =>
        ComandoExterno.Executar("secret-tool",
            new[] { "clear", Atributo, ValorAtributo, "chave", key }, limite: LimiteChaveiro);

    private static void GravarArquivoPrivado(string caminho, string conteudo)
    {
        ApagarSeExistir(caminho);
        var opcoes = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
        };
        if (!OperatingSystem.IsWindows())
            opcoes.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var escritor = new StreamWriter(caminho, opcoes);
        escritor.Write(conteudo);
    }

    private static void ApagarSeExistir(string caminho)
    {
        if (File.Exists(caminho))
            File.Delete(caminho);
    }
}
