namespace CubeBin.Core.Models;

/// <summary>
/// Representa uma pasta monitorada pelo organizador — padrão do Windows ou personalizada.
/// Pastas padrão nunca são removidas do config, apenas limpas/restauradas (ver ConfigService).
/// </summary>
public class PastaMonitorada
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Caminho { get; set; } = string.Empty;

    /// <summary>Se false, a pasta é um dos diretórios padrão do Windows (não pode ser removida).</summary>
    public bool Removivel { get; set; } = true;

    public bool Editavel { get; set; } = true;

    /// <summary>Se true, subpastas também são varridas e organizadas (com risco de desorganização).</summary>
    public bool OrganizarSubpastas { get; set; } = false;

    /// <summary>Prefixo usado em conflitos de nome (seção 9 da spec). Se vazio, é gerado a partir de Nome.</summary>
    public string? Prefixo { get; set; }

    /// <summary>"*" monitora todas as extensões; caso contrário, lista explícita (ex: ".pdf", ".zip").</summary>
    public List<string> ExtensoesMonitoradas { get; set; } = new() { "*" };

    /// <summary>Se preenchido, essas regras substituem inteiramente as regras padrão só para esta
    /// pasta. Null/vazio = usa as regras padrão (config.RegrasExtensao).</summary>
    public Dictionary<string, RegraExtensao>? RegrasPersonalizadas { get; set; }

    public string ObterPrefixoEfetivo()
    {
        if (!string.IsNullOrWhiteSpace(Prefixo))
            return Prefixo!;

        // Gera um prefixo curto a partir do nome, ex: "Documentos" -> "Doc", "Área de Trabalho" -> "Área"
        var somenteLetras = new string(Nome.Where(char.IsLetter).ToArray());
        return somenteLetras.Length >= 3 ? somenteLetras[..3] : somenteLetras;
    }
}
