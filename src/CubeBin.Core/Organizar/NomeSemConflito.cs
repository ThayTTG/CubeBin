namespace CubeBin.Core.Organizar;

/// <summary>
/// A regra de conflito de nome do PRD (5.1 e 6.3): se já existe um arquivo com o mesmo nome no
/// destino, o novo ganha o prefixo da pasta de onde veio ("Doc-Texto.pdf", "Desk-Texto.pdf"). Se
/// ainda assim existir, ganha um número ("Doc-Texto (2).pdf"). Nunca sobrescreve.
/// </summary>
public static class NomeSemConflito
{
    /// <param name="pastaDestino">Onde o arquivo vai ficar.</param>
    /// <param name="nome">Nome original do arquivo.</param>
    /// <param name="prefixo">Prefixo da pasta de origem (ex.: "Doc").</param>
    /// <param name="jaReservados">Nomes já prometidos a outros arquivos nesta mesma execução.</param>
    public static string Resolver(string pastaDestino, string nome, string prefixo, ISet<string>? jaReservados = null)
    {
        bool Livre(string candidato)
        {
            var caminho = Path.Combine(pastaDestino, candidato);
            return !File.Exists(caminho) && !Directory.Exists(caminho) &&
                   (jaReservados is null || !jaReservados.Contains(caminho));
        }

        if (Livre(nome)) return Reservar(Path.Combine(pastaDestino, nome));

        var comPrefixo = $"{prefixo}-{nome}";
        if (Livre(comPrefixo)) return Reservar(Path.Combine(pastaDestino, comPrefixo));

        var semExtensao = Path.GetFileNameWithoutExtension(comPrefixo);
        var extensao = Path.GetExtension(comPrefixo);
        for (var i = 2; ; i++)
        {
            var candidato = $"{semExtensao} ({i}){extensao}";
            if (Livre(candidato)) return Reservar(Path.Combine(pastaDestino, candidato));
        }

        string Reservar(string caminho)
        {
            jaReservados?.Add(caminho);
            return caminho;
        }
    }

    public static ISet<string> NovoConjunto() =>
        new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
}
