using System.Diagnostics;

namespace CubeBin.App;

/// <summary>
/// Dados de identificação do app para a tela Sobre e o rodapé do menu lateral. A versão é lida do
/// próprio assembly (o &lt;Version&gt; do .csproj), nunca escrita à mão, como pede o guia da NokyLabs.
/// </summary>
public static class InfoApp
{
    public const string UrlSite = "https://thayttg.github.io/nokylabs/#cubebin";
    public const string UrlCodigo = "https://github.com/ThayTTG/CubeBin";
    public const string UrlProblema = "https://github.com/ThayTTG/CubeBin/issues/new";

    /// <summary>Versão no formato curto (ex.: "1.1.0").</summary>
    public static string Versao
    {
        get
        {
            var versao = typeof(InfoApp).Assembly.GetName().Version;
            return versao is null ? "?" : $"{versao.Major}.{versao.Minor}.{Math.Max(versao.Build, 0)}";
        }
    }

    /// <summary>Abre um endereço no navegador padrão. Devolve false se não deu para abrir.</summary>
    public static bool AbrirNoNavegador(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
