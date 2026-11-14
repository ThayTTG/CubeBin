namespace Comum.Interop;

/// <summary>
/// No Linux, as pastas do usuário (Downloads, Documentos, Área de Trabalho) podem ter outro nome
/// conforme o idioma (ex.: ~/Transferências). O nome real fica em ~/.config/user-dirs.dirs, o mesmo
/// arquivo que o comando xdg-user-dir consulta. É o equivalente às Known Folders do Windows.
/// </summary>
public static class PastasXdg
{
    /// <summary>Caminho da pasta (ex.: "DOWNLOAD", "DOCUMENTS", "DESKTOP"), ou null se não estiver definida.</summary>
    public static string? Obter(string tipo)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
                configHome = Path.Combine(home, ".config");

            var arquivo = Path.Combine(configHome, "user-dirs.dirs");
            if (!File.Exists(arquivo)) return null;

            return LerDoConteudo(File.ReadAllText(arquivo), tipo, home);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Lê uma linha como XDG_DOWNLOAD_DIR="$HOME/Transferências" do conteúdo do user-dirs.dirs.
    /// Separado de <see cref="Obter"/> para poder ser testado sem mexer nos arquivos do sistema.
    /// </summary>
    public static string? LerDoConteudo(string conteudo, string tipo, string home)
    {
        var chave = $"XDG_{tipo}_DIR=";

        foreach (var linhaBruta in conteudo.Split('\n'))
        {
            var linha = linhaBruta.Trim();
            if (linha.StartsWith('#') || !linha.StartsWith(chave, StringComparison.Ordinal))
                continue;

            var valor = linha[chave.Length..].Trim().Trim('"');
            valor = valor.Replace("\\\"", "\"");

            if (valor.StartsWith("$HOME", StringComparison.Ordinal))
                valor = home + valor["$HOME".Length..];
            else if (!valor.StartsWith('/'))
                return null; // o formato só aceita "$HOME/..." ou um caminho absoluto

            valor = valor.TrimEnd('/');

            // "$HOME/" sozinho quer dizer "pasta desativada" (aponta para a própria home).
            if (valor.Length == 0 || valor == home.TrimEnd('/'))
                return null;

            return valor;
        }

        return null;
    }
}
