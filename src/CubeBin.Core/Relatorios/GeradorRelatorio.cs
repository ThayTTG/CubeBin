using System.Text;
using CubeBin.Core.Plataforma;

namespace CubeBin.Core.Relatorios;

/// <summary>Uma linha da tabela do relatório.</summary>
public class LinhaRelatorio
{
    public string Arquivo { get; init; } = "";
    public string PastaOrigem { get; init; } = "";
    public string CaminhoOrigem { get; init; } = "";
    public string Destino { get; init; } = "";
    public string CaminhoDestino { get; init; } = "";
    public long Tamanho { get; init; }
    public bool Falhou { get; init; }
    public string? Motivo { get; init; }
}

/// <summary>O que vai no relatório de uma execução.</summary>
public class DadosRelatorio
{
    /// <summary>"Organização", "Liberar espaço"...</summary>
    public string Operacao { get; init; } = "Organização";
    public DateTime Data { get; init; } = DateTime.Now;
    public string ColunaDestino { get; init; } = "Pasta de destino";
    public string RotuloSucesso { get; init; } = "arquivos movidos";
    public List<LinhaRelatorio> Linhas { get; init; } = new();
    public List<string> Avisos { get; init; } = new();
}

/// <summary>
/// Preenche o modelo HTML embutido no programa e salva "Relatório 2026-11-17.html" (PRD 6.1).
/// Na segunda execução do mesmo dia: "Relatório 2026-11-17 (2).html". Tudo é escapado para HTML.
/// </summary>
public static class GeradorRelatorio
{
    public static string Gerar(DadosRelatorio dados, string pasta)
    {
        Directory.CreateDirectory(pasta);
        var nomeBase = $"Relatório {dados.Data:yyyy-MM-dd}";
        var caminho = Path.Combine(pasta, nomeBase + ".html");
        for (var i = 2; File.Exists(caminho); i++)
            caminho = Path.Combine(pasta, $"{nomeBase} ({i}).html");

        File.WriteAllText(caminho, Montar(dados, Path.GetFileNameWithoutExtension(caminho)), new UTF8Encoding(false));
        return caminho;
    }

    public static string Montar(DadosRelatorio dados, string titulo)
    {
        var ok = dados.Linhas.Count(l => !l.Falhou);
        var falhas = dados.Linhas.Count(l => l.Falhou);
        var bytes = dados.Linhas.Where(l => !l.Falhou).Sum(l => l.Tamanho);

        var resumo = new StringBuilder();
        resumo.Append(Cartao(dados.Data.ToString("dd/MM/yyyy"), "às " + dados.Data.ToString("HH:mm"), ""));
        resumo.Append(Cartao(ok.ToString(), dados.RotuloSucesso, ok > 0 ? "ok" : ""));
        resumo.Append(Cartao(falhas.ToString(), falhas == 1 ? "erro" : "erros", falhas > 0 ? "erro" : ""));
        resumo.Append(Cartao(FormatarBytes.Formatar(bytes), "no total", ""));

        var avisos = dados.Avisos.Count == 0
            ? ""
            : "<ul class=\"avisos\">" + string.Concat(dados.Avisos.Select(a => $"<li>{H(a)}</li>")) + "</ul>";

        var linhas = new StringBuilder();
        // Falhas primeiro: é o que a pessoa precisa ver.
        foreach (var l in dados.Linhas.OrderByDescending(l => l.Falhou).ThenBy(l => l.Arquivo, StringComparer.CurrentCultureIgnoreCase))
        {
            linhas.Append(l.Falhou ? "<tr class=\"falha\"" : "<tr").Append($" data-nome=\"{H(l.Arquivo)}\">");
            linhas.Append($"<td>{H(l.Arquivo)}");
            if (l.Falhou) linhas.Append($"<div class=\"motivo\">Não foi possível: {H(l.Motivo ?? "erro desconhecido")}</div>");
            linhas.Append("</td>");
            linhas.Append($"<td>{H(l.PastaOrigem)}<div class=\"caminho\">{H(Pasta(l.CaminhoOrigem))}</div></td>");
            linhas.Append($"<td>{H(l.Destino)}<div class=\"caminho\">{H(Pasta(l.CaminhoDestino))}</div></td>");
            linhas.Append($"<td class=\"tam\">{H(FormatarBytes.Formatar(l.Tamanho))}</td></tr>");
        }

        using var modelo = typeof(GeradorRelatorio).Assembly.GetManifestResourceStream("CubeBin.ModeloRelatorio.html")
                           ?? throw new InvalidOperationException("O modelo do relatório não está no programa.");
        using var leitor = new StreamReader(modelo, Encoding.UTF8);
        return leitor.ReadToEnd()
            .Replace("{{TITULO}}", H(titulo))
            .Replace("{{OPERACAO}}", H(dados.Operacao))
            .Replace("{{DATA}}", H(dados.Data.ToString("dd/MM/yyyy 'às' HH:mm")))
            .Replace("{{COLUNA_DESTINO}}", H(dados.ColunaDestino))
            .Replace("{{RESUMO}}", resumo.ToString())
            .Replace("{{AVISOS}}", avisos)
            .Replace("{{LINHAS}}", linhas.ToString());
    }

    /// <summary>Só a pasta (o nome do arquivo já está na primeira coluna).</summary>
    private static string Pasta(string caminho) =>
        string.IsNullOrEmpty(caminho) ? "" : Path.GetDirectoryName(caminho) ?? caminho;

    private static string Cartao(string valor, string rotulo, string classe) =>
        $"<div class=\"cartao {classe}\"><div class=\"valor\">{H(valor)}</div><div class=\"rotulo\">{H(rotulo)}</div></div>";

    /// <summary>Escapa só o necessário (acentos ficam legíveis no arquivo, que é UTF-8).</summary>
    private static string H(string texto) => (texto ?? "")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");
}
