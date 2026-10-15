namespace CubeBin.Core.Models;

public class BackupConfig
{
    public string DiretorioCsv { get; set; } = string.Empty;

    /// <summary>Quantidade de arquivos de backup CSV a manter (rotação automática).</summary>
    public int ManterUltimosN { get; set; } = 30;
}
