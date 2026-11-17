using System.Globalization;

namespace CubeBin.Core.Plataforma;

/// <summary>Tamanho legível no formato brasileiro ("1,5 GB"), igual em qualquer idioma do sistema.</summary>
public static class FormatarBytes
{
    public static string Formatar(long bytes) => bytes switch
    {
        >= 1_073_741_824 => Numero(bytes / 1_073_741_824.0) + " GB",
        >= 1_048_576 => Numero(bytes / 1_048_576.0) + " MB",
        >= 1_024 => Numero(bytes / 1_024.0) + " KB",
        _ => $"{bytes} B",
    };

    private static string Numero(double valor) =>
        valor.ToString("F1", CultureInfo.InvariantCulture).Replace('.', ',');
}
