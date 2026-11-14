using Microsoft.Win32;

namespace Comum;

/// <summary>
/// Registra (ou remove) o app para abrir automaticamente ao iniciar o Windows, via a chave
/// clássica HKCU\...\Run — não exige instalador nem empacotamento MSIX.
/// </summary>
public class InicializacaoService
{
    private const string CaminhoChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _nomeValor;

    /// <param name="nomeDoApp">O nome que aparece na aba Inicializar do Gerenciador de Tarefas
    /// (cada app da família tem o seu, para um não desligar o outro).</param>
    public InicializacaoService(string nomeDoApp) => _nomeValor = nomeDoApp;

    public bool EstaHabilitado()
    {
        using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: false);
        return chave?.GetValue(_nomeValor) is string valor && !string.IsNullOrWhiteSpace(valor);
    }

    public void Habilitar(string caminhoExecutavel)
    {
        using var chave = Registry.CurrentUser.CreateSubKey(CaminhoChaveRun, writable: true);
        chave.SetValue(_nomeValor, $"\"{caminhoExecutavel}\"");
    }

    public void Desabilitar()
    {
        using var chave = Registry.CurrentUser.OpenSubKey(CaminhoChaveRun, writable: true);
        chave?.DeleteValue(_nomeValor, throwOnMissingValue: false);
    }
}
