using System.Globalization;
using System.Text;

namespace CubeBin.Core.Plataforma;

/// <summary>
/// A Lixeira do Linux no padrão FreeDesktop (a mesma que o Nautilus, o Dolphin e o "gio trash" usam):
/// o item vai para <c>~/.local/share/Trash/files/NOME</c> e ganha um <c>info/NOME.trashinfo</c> com o
/// caminho original e a data. Assim ele aparece na Lixeira do sistema e pode ser restaurado por lá
/// também. A "chave" devolvida é o NOME dentro da Lixeira.
/// </summary>
public sealed class LixeiraLinux : ILixeira
{
    private readonly string _raiz;

    /// <param name="raiz">A pasta Trash (nos testes, uma pasta temporária).</param>
    public LixeiraLinux(string? raiz = null)
    {
        _raiz = raiz ?? Path.Combine(PastaDeDadosXdg(), "Trash");
    }

    private string PastaArquivos => Path.Combine(_raiz, "files");
    private string PastaInfo => Path.Combine(_raiz, "info");

    public string Enviar(string caminho)
    {
        var completo = Path.GetFullPath(caminho).TrimEnd('/');
        var ehPasta = Directory.Exists(completo);
        if (!ehPasta && !File.Exists(completo)) throw new FileNotFoundException("O arquivo não existe mais.", completo);

        Directory.CreateDirectory(PastaArquivos);
        Directory.CreateDirectory(PastaInfo);

        // O padrão manda reservar o nome criando o .trashinfo primeiro (com criação exclusiva).
        var nome = ReservarNome(Path.GetFileName(completo), completo);
        var destino = Path.Combine(PastaArquivos, nome);
        try
        {
            if (ehPasta) MoverPasta(completo, destino);
            else File.Move(completo, destino, overwrite: false); // entre discos, o .NET copia e apaga
        }
        catch
        {
            File.Delete(Path.Combine(PastaInfo, nome + ".trashinfo"));
            throw;
        }
        return nome;
    }

    public bool Existe(string chave) =>
        File.Exists(Path.Combine(PastaArquivos, chave)) || Directory.Exists(Path.Combine(PastaArquivos, chave));

    public void Restaurar(string chave, string destino)
    {
        var origem = Path.Combine(PastaArquivos, chave);
        if (File.Exists(destino) || Directory.Exists(destino))
            throw new IOException("Já existe um arquivo com esse nome no lugar de origem.");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        if (Directory.Exists(origem)) MoverPasta(origem, destino);
        else File.Move(origem, destino, overwrite: false);

        var info = Path.Combine(PastaInfo, chave + ".trashinfo");
        if (File.Exists(info)) File.Delete(info);
    }

    private string ReservarNome(string nome, string caminhoOriginal)
    {
        var semExtensao = Path.GetFileNameWithoutExtension(nome);
        var extensao = Path.GetExtension(nome);
        var conteudo = MontarTrashInfo(caminhoOriginal, DateTime.Now);

        for (var i = 1; ; i++)
        {
            var candidato = i == 1 ? nome : $"{semExtensao}.{i}{extensao}";
            if (File.Exists(Path.Combine(PastaArquivos, candidato)) || Directory.Exists(Path.Combine(PastaArquivos, candidato)))
                continue;
            try
            {
                using var arquivo = new FileStream(Path.Combine(PastaInfo, candidato + ".trashinfo"), FileMode.CreateNew, FileAccess.Write);
                arquivo.Write(Encoding.UTF8.GetBytes(conteudo));
                return candidato;
            }
            catch (IOException) when (i < 10_000)
            {
                // Nome já reservado por outro programa: tenta o próximo.
            }
        }
    }

    /// <summary>O conteúdo do .trashinfo (público para os testes).</summary>
    public static string MontarTrashInfo(string caminhoOriginal, DateTime data) =>
        "[Trash Info]\n" +
        $"Path={CodificarCaminho(caminhoOriginal)}\n" +
        $"DeletionDate={data.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n";

    /// <summary>O padrão pede o caminho em formato de URL (espaço vira %20, acentos em UTF-8), mantendo as barras.</summary>
    public static string CodificarCaminho(string caminho)
    {
        var texto = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(caminho))
        {
            var c = (char)b;
            if (b < 128 && (char.IsAsciiLetterOrDigit(c) || "/-_.~".Contains(c))) texto.Append(c);
            else texto.Append('%').Append(b.ToString("X2"));
        }
        return texto.ToString();
    }

    /// <summary>Move uma pasta; entre discos diferentes, copia tudo e só então apaga a original.</summary>
    internal static void MoverPasta(string origem, string destino)
    {
        try
        {
            Directory.Move(origem, destino);
        }
        catch (IOException) when (!Directory.Exists(destino))
        {
            CopiarPasta(origem, destino);
            Directory.Delete(origem, recursive: true);
        }
    }

    private static void CopiarPasta(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var arquivo in Directory.GetFiles(origem))
            File.Copy(arquivo, Path.Combine(destino, Path.GetFileName(arquivo)));
        foreach (var pasta in Directory.GetDirectories(origem))
            CopiarPasta(pasta, Path.Combine(destino, Path.GetFileName(pasta)));
    }

    private static string PastaDeDadosXdg()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }
}
