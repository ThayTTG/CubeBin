using System.Globalization;
using System.Text;
using OrganizadorArquivos.Core.Models;

namespace OrganizadorArquivos.Core.Services;

/// <summary>
/// Gera o snapshot em CSV do estado dos arquivos antes de qualquer movimentação
/// (seção 4 da spec) e aplica a rotação de retenção configurada.
/// </summary>
public class BackupService
{
    /// <summary>Retorna null (sem criar arquivo nenhum) se não houver arquivos para fazer backup — evita
    /// gerar dezenas de arquivos "backup_*.csv" vazios (só com o cabeçalho) em execuções sem nada a organizar.</summary>
    public string? GerarBackupCsv(IEnumerable<FileInfo> arquivos, IReadOnlyDictionary<string, string> pastaOrigemPorCaminho, BackupConfig config)
    {
        var listaArquivos = arquivos as IReadOnlyCollection<FileInfo> ?? arquivos.ToList();
        if (listaArquivos.Count == 0)
            return null;

        Directory.CreateDirectory(config.DiretorioCsv);

        var nomeArquivo = $"backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.csv";
        var caminhoCompleto = Path.Combine(config.DiretorioCsv, nomeArquivo);

        var linhas = listaArquivos
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new
            {
                a.Name,
                Extensao = a.Extension,
                CaminhoOriginal = a.FullName,
                PastaOrigem = pastaOrigemPorCaminho.GetValueOrDefault(a.FullName, "desconhecida"),
                a.LastWriteTime,
                a.Length
            });

        var sb = new StringBuilder();
        sb.AppendLine("nome_arquivo,extensao,caminho_original,pasta_origem,data_modificacao,tamanho_bytes");
        foreach (var l in linhas)
        {
            sb.AppendLine(string.Join(",",
                CsvEscape(l.Name),
                CsvEscape(l.Extensao),
                CsvEscape(l.CaminhoOriginal),
                CsvEscape(l.PastaOrigem),
                l.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                l.Length.ToString(CultureInfo.InvariantCulture)));
        }

        File.WriteAllText(caminhoCompleto, sb.ToString(), Encoding.UTF8);
        AplicarRetencao(config);

        return caminhoCompleto;
    }

    private static string CsvEscape(string valor)
    {
        if (valor.Contains(',') || valor.Contains('"') || valor.Contains('\n'))
            return $"\"{valor.Replace("\"", "\"\"")}\"";
        return valor;
    }

    private static void AplicarRetencao(BackupConfig config)
    {
        var arquivos = new DirectoryInfo(config.DiretorioCsv)
            .GetFiles("backup_*.csv")
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        foreach (var antigo in arquivos.Skip(config.ManterUltimosN))
            antigo.Delete();
    }
}
