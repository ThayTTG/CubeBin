using System.Text.Json.Nodes;

namespace Comum;

/// <summary>
/// Até a versão 2.x, o CubeBin guardava TUDO num config.json só (organização, faxina e nuvem), em
/// %LOCALAPPDATA%\CubeBin. A partir da separação em dois apps, cada um tem o seu config, e os dois
/// precisam ler a parte deles daquele arquivo antigo.
///
/// O cuidado principal: o primeiro app a abrir não pode estragar o arquivo antes do outro ler. Por
/// isso, antes de qualquer um mexer, o config antigo é copiado intacto para "config-2.x.json", e
/// os dois apps migram a partir dessa cópia. O arquivo original nunca é apagado por aqui.
/// </summary>
public static class MigracaoDaVersao2
{
    public const string NomeDaCopia = "config-2.x.json";

    /// <summary>%LOCALAPPDATA%\CubeBin — onde a versão 2.x guardava tudo.</summary>
    public static string PastaPadraoDaVersao2 => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CubeBin");

    /// <summary>
    /// Devolve o caminho da cópia intacta do config da 2.x, criando-a se ainda não existir.
    /// Devolve null se não houver config da 2.x nessa pasta (instalação nova, ou o config já é
    /// de uma versão separada e a cópia nunca foi feita).
    /// </summary>
    public static string? GarantirCopiaDoConfigAntigo(string pastaDaVersao2)
    {
        var copia = Path.Combine(pastaDaVersao2, NomeDaCopia);
        if (File.Exists(copia)) return copia;

        var original = Path.Combine(pastaDaVersao2, "config.json");
        if (!EhConfigDaVersao2(original))
        {
            // O config.json pode ter sido corrompido; o .bak que o próprio app guardava salva o dia.
            original += ".bak";
            if (!EhConfigDaVersao2(original)) return null;
        }

        File.Copy(original, copia, overwrite: false);
        return copia;
    }

    /// <summary>O config da 2.x é o único que tem regras de extensão E o agente de sugestões juntos.</summary>
    public static bool EhConfigDaVersao2(string caminho)
    {
        var raiz = LerJson(caminho);
        return raiz is not null && raiz.ContainsKey("regrasExtensao") && raiz.ContainsKey("agenteAtivo");
    }

    /// <summary>Lê um JSON como objeto. Devolve null se o arquivo não existir ou estiver quebrado.</summary>
    public static JsonObject? LerJson(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return null;
            return JsonNode.Parse(File.ReadAllText(caminho)) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
