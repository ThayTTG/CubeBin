using Microsoft.Win32;

namespace CubeBin.Core.Services;

/// <summary>
/// Registra (ou remove) o app para abrir automaticamente ao iniciar o Windows, via a chave
/// clássica HKCU\...\Run — não exige instalador nem empacotamento MSIX.
/// </summary>
public class InicializacaoService
{
    private const string CaminhoChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NomeValor = "CubeBin";

    public bool EstaHabilitado()
    {
        using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: false);
        return chave?.GetValue(NomeValor) is string valor && !string.IsNullOrWhiteSpace(valor);
    }

    public void Habilitar(string caminhoExecutavel)
    {
        using var chave = Registry.CurrentUser.CreateSubKey(CaminhoChaveRun, writable: true);
        chave.SetValue(NomeValor, $"\"{caminhoExecutavel}\"");
    }

    public void Desabilitar()
    {
        using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: true);
        chave?.DeleteValue(NomeValor, throwOnMissingValue: false);
    }
}
