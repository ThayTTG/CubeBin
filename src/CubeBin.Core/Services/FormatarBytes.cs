namespace CubeBin.Core.Services;

/// <summary>Formata um tamanho em bytes de forma legível (KB/MB/GB), reaproveitado entre o
/// agendador do agente e a UI, para não duplicar a mesma lógica em dois lugares.</summary>
public static class FormatarBytes
{
    public static string Formatar(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }
}
