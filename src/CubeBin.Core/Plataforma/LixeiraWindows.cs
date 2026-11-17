using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CubeBin.Core.Plataforma;

/// <summary>
/// A Lixeira do Windows. Enviar usa a mesma API do Explorer (SHFileOperation com "permitir desfazer"),
/// então o item aparece na Lixeira normalmente. Para restaurar sem depender de menus do Explorer, o
/// CubeBin lê os arquivos de controle da Lixeira: para cada item há um "$I..." (caminho original e
/// data) e um "$R..." (o conteúdo), em <c>X:\$Recycle.Bin\&lt;usuário&gt;\</c>. Restaurar = mover o $R
/// de volta e apagar o $I.
/// A chave guardada no registro é "caminho original|momento do envio".
/// </summary>
public sealed class LixeiraWindows : ILixeira
{
    public string Enviar(string caminho)
    {
        var completo = Path.GetFullPath(caminho).TrimEnd('\\');
        if (!File.Exists(completo) && !Directory.Exists(completo))
            throw new FileNotFoundException("O arquivo não existe mais.", completo);

        var antes = DateTime.UtcNow.AddSeconds(-2);
        var operacao = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = completo + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var codigo = SHFileOperation(ref operacao);
        if (codigo != 0 || operacao.fAnyOperationsAborted)
            throw new IOException($"O Windows não conseguiu mandar para a Lixeira (código {codigo}).");
        if (File.Exists(completo) || Directory.Exists(completo))
            throw new IOException("O Windows não mandou o arquivo para a Lixeira (talvez a Lixeira esteja desligada para este disco).");

        return $"{completo}|{antes.Ticks.ToString(CultureInfo.InvariantCulture)}";
    }

    public bool Existe(string chave) => Encontrar(chave) is not null;

    public void Restaurar(string chave, string destino)
    {
        var item = Encontrar(chave) ?? throw new FileNotFoundException("O item não está mais na Lixeira.");
        if (File.Exists(destino) || Directory.Exists(destino))
            throw new IOException("Já existe um arquivo com esse nome no lugar de origem.");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        if (Directory.Exists(item.CaminhoR)) LixeiraLinux.MoverPasta(item.CaminhoR, destino);
        else File.Move(item.CaminhoR, destino, overwrite: false);
        File.Delete(item.CaminhoI);
    }

    public record ItemLixeira(string CaminhoI, string CaminhoR, string CaminhoOriginal, DateTime ExcluidoEmUtc);

    private static ItemLixeira? Encontrar(string chave)
    {
        var separador = chave.LastIndexOf('|');
        if (separador < 0) return null;
        var original = chave[..separador];
        var depoisDe = new DateTime(long.Parse(chave[(separador + 1)..], CultureInfo.InvariantCulture), DateTimeKind.Utc);

        var raiz = Path.GetPathRoot(original);
        if (string.IsNullOrEmpty(raiz)) return null;
        return Procurar(Path.Combine(raiz, "$Recycle.Bin"), original, depoisDe);
    }

    /// <summary>Procura, nas pastas da Lixeira a que o usuário tem acesso, o item mais novo daquele caminho.</summary>
    public static ItemLixeira? Procurar(string pastaRecycleBin, string original, DateTime depoisDeUtc)
    {
        if (!Directory.Exists(pastaRecycleBin)) return null;
        ItemLixeira? melhor = null;
        foreach (var pastaUsuario in Directory.EnumerateDirectories(pastaRecycleBin))
        {
            IEnumerable<string> controles;
            try { controles = Directory.EnumerateFiles(pastaUsuario, "$I*").ToList(); }
            catch (Exception) { continue; } // pasta de outro usuário
            foreach (var controle in controles)
            {
                var lido = LerArquivoI(controle);
                if (lido is null) continue;
                var (caminho, data) = lido.Value;
                if (!string.Equals(caminho, original, StringComparison.OrdinalIgnoreCase) || data < depoisDeUtc) continue;
                var conteudo = Path.Combine(pastaUsuario, "$R" + Path.GetFileName(controle)[2..]);
                if (!File.Exists(conteudo) && !Directory.Exists(conteudo)) continue;
                if (melhor is null || data > melhor.ExcluidoEmUtc)
                    melhor = new ItemLixeira(controle, conteudo, caminho, data);
            }
        }
        return melhor;
    }

    /// <summary>
    /// Lê um "$I": versão (8 bytes), tamanho (8), data em FILETIME (8) e o caminho em UTF-16 —
    /// na versão 1 (Vista a 8.1) em 520 bytes fixos; na versão 2 (Windows 10+) com o tamanho antes.
    /// </summary>
    public static (string Caminho, DateTime ExcluidoEmUtc)? LerArquivoI(string arquivo)
    {
        try
        {
            var bytes = File.ReadAllBytes(arquivo);
            if (bytes.Length < 24) return null;
            var versao = BitConverter.ToInt64(bytes, 0);
            var data = DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 16));
            string caminho;
            if (versao == 2)
            {
                var caracteres = BitConverter.ToInt32(bytes, 24);
                caminho = Encoding.Unicode.GetString(bytes, 28, Math.Max(0, Math.Min(caracteres * 2, bytes.Length - 28)));
            }
            else
            {
                caminho = Encoding.Unicode.GetString(bytes, 24, Math.Min(520, bytes.Length - 24));
            }
            return (caminho.TrimEnd('\0'), data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Monta um "$I" da versão 2 (usado nos testes).</summary>
    public static byte[] MontarArquivoI(string caminho, long tamanho, DateTime excluidoEmUtc)
    {
        var texto = Encoding.Unicode.GetBytes(caminho + "\0");
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(2L));
        bytes.AddRange(BitConverter.GetBytes(tamanho));
        bytes.AddRange(BitConverter.GetBytes(excluidoEmUtc.ToFileTimeUtc()));
        bytes.AddRange(BitConverter.GetBytes(texto.Length / 2));
        bytes.AddRange(texto);
        return bytes.ToArray();
    }

    // ---------- SHFileOperation ----------

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}

public static class Lixeiras
{
    /// <summary>A Lixeira do sistema em que o CubeBin está rodando.</summary>
    public static ILixeira? DoSistema() =>
        OperatingSystem.IsWindows() ? new LixeiraWindows()
        : OperatingSystem.IsLinux() ? new LixeiraLinux()
        : null;
}
