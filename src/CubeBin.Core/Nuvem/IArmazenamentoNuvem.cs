using System.Security.Cryptography;

namespace CubeBin.Core.Nuvem;

/// <summary>Um arquivo guardado na nuvem, com o que é preciso para conferir a integridade.</summary>
public record ArquivoNuvem(string Id, string Nome, long Tamanho, string? Md5, string? Link);

public record InfoContaNuvem(string Email, long UsadoBytes, long? TotalBytes);

/// <summary>
/// O que o CubeBin precisa de uma nuvem. O Google Drive é a implementação real
/// (<see cref="GoogleDriveNuvem"/>); os testes usam uma pasta no disco (<see cref="NuvemLocal"/>).
/// Os caminhos são sempre dentro da pasta "CubeBin" na raiz (PRD 6.2: o app só mexe dentro dela).
/// </summary>
public interface IArmazenamentoNuvem
{
    /// <summary>Id da pasta (ex.: "CubeBin/Cubos"), criando o que faltar.</summary>
    Task<string> GarantirPastaAsync(string caminho, CancellationToken cancelar = default);

    Task<ArquivoNuvem> EnviarAsync(string arquivoLocal, string pastaId, string nome, string tipoMime,
        IProgress<long>? progresso = null, CancellationToken cancelar = default);

    /// <summary>Troca o conteúdo de um arquivo que já existe (usado no índice).</summary>
    Task<ArquivoNuvem> AtualizarAsync(string id, string arquivoLocal, string tipoMime, CancellationToken cancelar = default);

    /// <summary>Os dados do arquivo como a nuvem os vê (tamanho e hash calculados por ela).</summary>
    Task<ArquivoNuvem?> ObterAsync(string id, CancellationToken cancelar = default);

    Task<ArquivoNuvem?> ProcurarAsync(string pastaId, string nome, CancellationToken cancelar = default);

    Task BaixarAsync(string id, string destinoLocal, IProgress<long>? progresso = null, CancellationToken cancelar = default);

    Task<InfoContaNuvem> ContaAsync(CancellationToken cancelar = default);
}

public static class Hash
{
    /// <summary>MD5 em hexadecimal minúsculo (o mesmo formato do "md5Checksum" do Google Drive).</summary>
    public static string Md5DoArquivo(string caminho)
    {
        using var md5 = MD5.Create();
        using var arquivo = File.OpenRead(caminho);
        return Convert.ToHexString(md5.ComputeHash(arquivo)).ToLowerInvariant();
    }
}

/// <summary>
/// Uma "nuvem" que é só uma pasta no disco. Serve para os testes (nunca toca na internet) e para
/// conferir o fluxo inteiro sem conta. Pode simular falhas: sem internet, Drive cheio, pacote corrompido.
/// </summary>
public sealed class NuvemLocal : IArmazenamentoNuvem
{
    private readonly string _raiz;

    public NuvemLocal(string raiz)
    {
        _raiz = raiz;
        Directory.CreateDirectory(_raiz);
    }

    /// <summary>Se preenchido, o próximo envio falha com esta exceção (simula rede, cota...).</summary>
    public Exception? FalharNoProximoEnvio { get; set; }

    /// <summary>Simula um pacote que chega diferente do enviado (a conferência tem de pegar).</summary>
    public bool CorromperNoProximoEnvio { get; set; }

    public long? Cota { get; set; }

    public Task<string> GarantirPastaAsync(string caminho, CancellationToken cancelar = default)
    {
        var pasta = Path.Combine(_raiz, caminho.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(pasta);
        return Task.FromResult(Path.GetRelativePath(_raiz, pasta));
    }

    public async Task<ArquivoNuvem> EnviarAsync(string arquivoLocal, string pastaId, string nome, string tipoMime,
        IProgress<long>? progresso = null, CancellationToken cancelar = default)
    {
        if (FalharNoProximoEnvio is { } falha)
        {
            FalharNoProximoEnvio = null;
            throw falha;
        }
        var destino = Path.Combine(_raiz, pastaId, nome);
        File.Copy(arquivoLocal, destino, overwrite: true);
        if (CorromperNoProximoEnvio)
        {
            CorromperNoProximoEnvio = false;
            await File.AppendAllTextAsync(destino, "x", cancelar);
        }
        progresso?.Report(new FileInfo(arquivoLocal).Length);
        return Descrever(Path.GetRelativePath(_raiz, destino));
    }

    public Task<ArquivoNuvem> AtualizarAsync(string id, string arquivoLocal, string tipoMime, CancellationToken cancelar = default)
    {
        File.Copy(arquivoLocal, Path.Combine(_raiz, id), overwrite: true);
        return Task.FromResult(Descrever(id));
    }

    public Task<ArquivoNuvem?> ObterAsync(string id, CancellationToken cancelar = default) =>
        Task.FromResult(File.Exists(Path.Combine(_raiz, id)) ? Descrever(id) : null);

    public Task<ArquivoNuvem?> ProcurarAsync(string pastaId, string nome, CancellationToken cancelar = default)
    {
        var id = Path.Combine(pastaId, nome);
        return ObterAsync(id, cancelar);
    }

    public Task BaixarAsync(string id, string destinoLocal, IProgress<long>? progresso = null, CancellationToken cancelar = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinoLocal)!);
        File.Copy(Path.Combine(_raiz, id), destinoLocal, overwrite: true);
        return Task.CompletedTask;
    }

    public Task<InfoContaNuvem> ContaAsync(CancellationToken cancelar = default)
    {
        var usado = Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        return Task.FromResult(new InfoContaNuvem("teste@local", usado, Cota));
    }

    private ArquivoNuvem Descrever(string id)
    {
        var caminho = Path.Combine(_raiz, id);
        return new ArquivoNuvem(id, Path.GetFileName(caminho), new FileInfo(caminho).Length, Hash.Md5DoArquivo(caminho), caminho);
    }
}
