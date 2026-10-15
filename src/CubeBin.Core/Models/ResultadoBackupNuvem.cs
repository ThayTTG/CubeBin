namespace CubeBin.Core.Models;

/// <summary>Resultado de uma tentativa de "enviar à nuvem e excluir localmente" (Camada 3).</summary>
public class ResultadoBackupNuvem
{
    public bool Sucesso { get; set; }
    public string? MensagemErro { get; set; }

    public PacoteBackup? Pacote { get; set; }

    public List<string> Deletados { get; set; } = new();
    public List<string> FalhasAoDeletar { get; set; } = new();
}
