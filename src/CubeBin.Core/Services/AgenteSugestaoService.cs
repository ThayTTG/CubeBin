using System.Security.Cryptography;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// O "agente de IA" da Camada 2 é, na prática, um conjunto de heurísticas locais — sem depender de
/// nenhuma IA externa, custo de API ou conexão com a internet, exatamente como pedido na releitura.
/// Analisa as mesmas pastas monitoradas já configuradas em Organizar/Limpar (nunca o disco inteiro,
/// por segurança e desempenho) e só gera sugestões — nunca apaga nada por conta própria.
/// </summary>
public class AgenteSugestaoService
{
    private static readonly string[] ExtensoesTemporarias = { ".tmp", ".temp", ".cache", ".bak", ".old", ".log" };

    public List<SugestaoLimpeza> Analisar(AppConfig config)
    {
        var arquivos = new List<FileInfo>();

        foreach (var pasta in config.TodasAsPastas)
        {
            if (!Directory.Exists(pasta.Caminho)) continue;

            try
            {
                // O agente sempre olha recursivamente — diferente de Organizar, aqui não há risco de
                // "reprocessar" nada (é só sugestão, nunca move/apaga sozinho), então o toggle
                // OrganizarSubpastas de cada pasta não se aplica.
                arquivos.AddRange(new DirectoryInfo(pasta.Caminho).GetFiles("*", SearchOption.AllDirectories));
            }
            catch (UnauthorizedAccessException)
            {
                // Alguma subpasta sem permissão de leitura — pula e segue com o resto.
            }
        }

        var sugestoes = new List<SugestaoLimpeza>();
        sugestoes.AddRange(DetectarDuplicados(arquivos));
        sugestoes.AddRange(DetectarTemporariosNaoUsados(arquivos, config));
        sugestoes.AddRange(DetectarArquivosGrandesParados(arquivos, config));
        return sugestoes;
    }

    /// <summary>Agrupa por tamanho primeiro (rápido, sem I/O) e só calcula hash dentro de grupos com
    /// mais de um arquivo do mesmo tamanho — evita hashear o disco inteiro desnecessariamente.</summary>
    private static List<SugestaoLimpeza> DetectarDuplicados(List<FileInfo> arquivos)
    {
        var resultado = new List<SugestaoLimpeza>();
        var gruposPorTamanho = arquivos.Where(a => a.Length > 0).GroupBy(a => a.Length).Where(g => g.Count() > 1);

        foreach (var grupoTamanho in gruposPorTamanho)
        {
            var porHash = new Dictionary<string, List<FileInfo>>();

            foreach (var arquivo in grupoTamanho)
            {
                string hash;
                try
                {
                    hash = CalcularHash(arquivo.FullName);
                }
                catch (IOException)
                {
                    continue; // arquivo em uso ou inacessível no momento — pula, não é crítico
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (!porHash.TryGetValue(hash, out var lista))
                {
                    lista = new List<FileInfo>();
                    porHash[hash] = lista;
                }
                lista.Add(arquivo);
            }

            foreach (var grupoIgual in porHash.Values.Where(g => g.Count > 1))
            {
                // Mantém o mais antigo (mais provável de ser o "original") como referência.
                var mantido = grupoIgual.OrderBy(a => a.LastWriteTime).First();

                foreach (var duplicado in grupoIgual.Where(a => a.FullName != mantido.FullName))
                {
                    resultado.Add(new SugestaoLimpeza
                    {
                        Nome = duplicado.Name,
                        Caminho = duplicado.FullName,
                        TamanhoBytes = duplicado.Length,
                        Tipo = TipoSugestao.Duplicado,
                        Motivo = $"Duplicado de {mantido.FullName}",
                        CaminhoDuplicadoDe = mantido.FullName
                    });
                }
            }
        }

        return resultado;
    }

    private static string CalcularHash(string caminho)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(caminho);
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static List<SugestaoLimpeza> DetectarTemporariosNaoUsados(List<FileInfo> arquivos, AppConfig config)
    {
        var limite = DateTime.Now.AddDays(-config.AgenteDiasTemporarioNaoUsado);
        var resultado = new List<SugestaoLimpeza>();

        foreach (var arquivo in arquivos)
        {
            if (!ExtensoesTemporarias.Contains(arquivo.Extension.ToLowerInvariant())) continue;

            var ultimoToque = ObterUltimoToque(arquivo);
            if (ultimoToque >= limite) continue;

            var dias = (int)(DateTime.Now - ultimoToque).TotalDays;
            resultado.Add(new SugestaoLimpeza
            {
                Nome = arquivo.Name,
                Caminho = arquivo.FullName,
                TamanhoBytes = arquivo.Length,
                Tipo = TipoSugestao.TemporarioNaoUsado,
                Motivo = $"Arquivo temporário/cache não acessado há {dias} dias"
            });
        }

        return resultado;
    }

    private static List<SugestaoLimpeza> DetectarArquivosGrandesParados(List<FileInfo> arquivos, AppConfig config)
    {
        var limite = DateTime.Now.AddDays(-config.AgenteDiasArquivoGrandeParado);
        var resultado = new List<SugestaoLimpeza>();

        foreach (var arquivo in arquivos)
        {
            if (arquivo.Length < config.AgenteTamanhoMinimoGrandeBytes) continue;

            var ultimoToque = ObterUltimoToque(arquivo);
            if (ultimoToque >= limite) continue;

            var dias = (int)(DateTime.Now - ultimoToque).TotalDays;
            var tamanhoGb = arquivo.Length / 1_073_741_824.0;
            resultado.Add(new SugestaoLimpeza
            {
                Nome = arquivo.Name,
                Caminho = arquivo.FullName,
                TamanhoBytes = arquivo.Length,
                Tipo = TipoSugestao.ArquivoGrandeParado,
                Motivo = $"Arquivo grande ({tamanhoGb:F1} GB) parado há {dias} dias"
            });
        }

        return resultado;
    }

    /// <summary>
    /// Data do último "toque" no arquivo. O Windows costuma desabilitar a atualização de
    /// LastAccessTime por performance desde o Vista (NtfsDisableLastAccessUpdate) — por isso usamos
    /// o mais recente entre LastAccessTime e LastWriteTime como aproximação melhor-esforço, não uma
    /// garantia exata de quando o arquivo foi realmente aberto pela última vez.
    /// </summary>
    private static DateTime ObterUltimoToque(FileInfo arquivo)
    {
        var acesso = arquivo.LastAccessTime;
        var escrita = arquivo.LastWriteTime;
        return acesso > escrita ? acesso : escrita;
    }
}
