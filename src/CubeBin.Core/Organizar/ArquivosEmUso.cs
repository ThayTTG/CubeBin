namespace CubeBin.Core.Organizar;

/// <summary>Diz se um arquivo está aberto em algum programa (PRD 6.3: pular arquivos abertos).</summary>
public interface IArquivosEmUso
{
    bool EstaEmUso(string caminho);
}

public static class ArquivosEmUso
{
    public static IArquivosEmUso DoSistema() =>
        OperatingSystem.IsWindows() ? new ArquivosEmUsoWindows()
        : OperatingSystem.IsLinux() ? new ArquivosEmUsoLinux()
        : new NenhumEmUso();

    /// <summary>Usado nos testes e onde não há como saber.</summary>
    public sealed class NenhumEmUso : IArquivosEmUso
    {
        public bool EstaEmUso(string caminho) => false;
    }
}

/// <summary>
/// Windows: tenta abrir o arquivo sem dividir com ninguém. Se outro programa estiver com ele aberto
/// (Word, um player de vídeo, um download), o Windows recusa — e o arquivo fica onde está.
/// </summary>
public sealed class ArquivosEmUsoWindows : IArquivosEmUso
{
    public bool EstaEmUso(string caminho)
    {
        try
        {
            using var _ = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>
/// Linux: não existe trava obrigatória como no Windows, então olha quem tem o arquivo aberto em
/// /proc/&lt;processo&gt;/fd (só os processos do próprio usuário são legíveis). A lista é lida uma vez
/// e reaproveitada por alguns segundos, para não varrer o /proc a cada arquivo.
/// </summary>
public sealed class ArquivosEmUsoLinux : IArquivosEmUso
{
    private HashSet<string>? _abertos;
    private DateTime _lidoEm;

    public bool EstaEmUso(string caminho)
    {
        if (_abertos is null || DateTime.UtcNow - _lidoEm > TimeSpan.FromSeconds(10))
        {
            _abertos = LerAbertos();
            _lidoEm = DateTime.UtcNow;
        }
        return _abertos.Contains(caminho);
    }

    private static HashSet<string> LerAbertos()
    {
        var abertos = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var meuProcesso = Environment.ProcessId.ToString();
            foreach (var processo in Directory.EnumerateDirectories("/proc"))
            {
                var nome = Path.GetFileName(processo);
                if (!nome.All(char.IsAsciiDigit) || nome == meuProcesso) continue;
                try
                {
                    foreach (var fd in Directory.EnumerateFileSystemEntries(Path.Combine(processo, "fd")))
                    {
                        try
                        {
                            var alvo = new FileInfo(fd).LinkTarget;
                            if (alvo is not null && alvo.StartsWith('/')) abertos.Add(alvo);
                        }
                        catch { /* o descritor fechou no meio da leitura */ }
                    }
                }
                catch { /* processo de outro usuário ou que já terminou */ }
            }
        }
        catch { /* sem /proc: ninguém em uso */ }
        return abertos;
    }
}
