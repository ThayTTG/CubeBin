namespace CubeBin.Core;

/// <summary>
/// Onde o CubeBin guarda as coisas dele: %LOCALAPPDATA%\CubeBin no Windows e ~/.local/share/CubeBin
/// no Linux. Os testes passam uma pasta temporária.
/// </summary>
public class Ambiente
{
    public string PastaDados { get; }

    public Ambiente(string pastaDados)
    {
        PastaDados = pastaDados;
        Directory.CreateDirectory(PastaDados);
    }

    public static Ambiente Padrao() => new(PastaDadosPadrao());

    /// <summary>
    /// A pasta de dados do usuário. Com a opção Create, o .NET cria a pasta se faltar (num Linux novo
    /// ~/.local/share pode nem existir, e sem isso o .NET devolveria um caminho vazio — os dados iriam
    /// parar na pasta atual).
    /// </summary>
    public static string PastaDadosPadrao()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(local) || !Path.IsPathRooted(local))
            local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(local, "CubeBin");
    }

    public string CaminhoConfiguracoes => Path.Combine(PastaDados, "configuracoes.json");
    public string CaminhoCubos => Path.Combine(PastaDados, "cubos.json");
    public string PastaRegistros => Path.Combine(PastaDados, "Registros");
    public string PastaRelatorios => Path.Combine(PastaDados, "Relatórios");
    public string PastaLog => Path.Combine(PastaDados, "Log");
    public string PastaCredenciais => Path.Combine(PastaDados, "Google");
    public string PastaEnviosPendentes => Path.Combine(PastaDados, "Envios pendentes");

    /// <summary>Log simples de cada execução (PRD 7), um arquivo por mês.</summary>
    public void Log(string mensagem)
    {
        try
        {
            Directory.CreateDirectory(PastaLog);
            File.AppendAllText(Path.Combine(PastaLog, $"cubebin-{DateTime.Now:yyyy-MM}.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensagem}{Environment.NewLine}");
        }
        catch
        {
            // O log nunca pode derrubar uma organização.
        }
    }
}
