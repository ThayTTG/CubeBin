using CubeBin.Core.Plataforma;

namespace CubeBin.Tests;

/// <summary>
/// Uma pasta temporária só deste teste, com as seis "pastas do sistema" falsas dentro dela.
/// Enquanto existir, <see cref="PastasDoSistema"/> aponta para cá: nenhum teste chega perto das
/// pastas reais do usuário. Ao final, tudo é apagado (só dentro da pasta temporária).
/// </summary>
public sealed class PastaDeTeste : IDisposable
{
    public string Raiz { get; }

    public PastaDeTeste()
    {
        Raiz = Path.Combine(Path.GetTempPath(), "cubebin-teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Raiz);
        PastasDoSistema.Substituto = p => Path.Combine(Raiz, "home", PastasDoSistema.NomeAmigavel(p));
    }

    public string Sistema(PastaPadrao pasta) => PastasDoSistema.Obter(pasta);

    /// <summary>Caminho dentro da pasta de teste (cria as pastas no caminho).</summary>
    public string Caminho(params string[] partes)
    {
        var caminho = Path.Combine(new[] { Raiz }.Concat(partes).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        return caminho;
    }

    /// <summary>Cria um arquivo com conteúdo e data de modificação antiga (para não cair na
    /// proteção de "modificado nos últimos minutos").</summary>
    public string Arquivo(string pasta, string nome, string conteudo = "conteúdo", TimeSpan? idade = null)
    {
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, nome);
        File.WriteAllText(caminho, conteudo);
        var data = DateTime.Now - (idade ?? TimeSpan.FromHours(2));
        File.SetLastWriteTime(caminho, data);
        try { File.SetCreationTime(caminho, data); } catch { /* nem todo sistema guarda */ }
        return caminho;
    }

    public void Dispose()
    {
        PastasDoSistema.Substituto = null;
        try
        {
            if (Directory.Exists(Raiz) && Raiz.StartsWith(Path.GetTempPath(), StringComparison.Ordinal))
                Directory.Delete(Raiz, recursive: true);
        }
        catch
        {
            // Sobra de teste em pasta temporária: o sistema limpa depois.
        }
    }
}
