using System.Diagnostics;

namespace Organizador.App;

/// <summary>
/// Dados de identificação do app para a tela Sobre, o rodapé do menu lateral, a bandeja e os
/// avisos. A versão é lida do próprio assembly (o &lt;Version&gt; do .csproj), nunca escrita à mão.
///
/// O NOME AINDA É PROVISÓRIO. Quando ele for escolhido, troque aqui (Nome, PastaDados,
/// NomeNaInicializacao e os endereços) e no .csproj (AssemblyName, Product, AssemblyTitle).
/// </summary>
public static class InfoApp
{
    public const string Nome = "Organizador";

    /// <summary>Onde ficam os arquivos do app: %LOCALAPPDATA%\NokyLabs\&lt;PastaDados&gt;.</summary>
    public const string PastaDados = "Organizador";

    /// <summary>O nome na aba Inicializar do Gerenciador de Tarefas (diferente do "CubeBin").</summary>
    public const string NomeNaInicializacao = "NokyLabs Organizador";

    // Enquanto o organizador não tem repositório nem página próprios, aponta para os do CubeBin.
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
