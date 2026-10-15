using System.Text.Json;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// Lê/grava sugestoes.json — histórico de sugestões do agente, com seus status (Pendente,
/// Aprovado, Ignorado, Excluido). Mesma filosofia de PendenciaService/RecallService.
/// </summary>
public class SugestaoService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<SugestaoLimpeza> Carregar(string caminhoArquivo) =>
        ArquivoJsonUtils.CarregarListaResiliente<SugestaoLimpeza>(caminhoArquivo, JsonOptions);

    public void Salvar(string caminhoArquivo, List<SugestaoLimpeza> itens)
    {
        var diretorio = Path.GetDirectoryName(caminhoArquivo);
        if (!string.IsNullOrEmpty(diretorio))
            Directory.CreateDirectory(diretorio);

        File.WriteAllText(caminhoArquivo, JsonSerializer.Serialize(itens, JsonOptions));
    }

    /// <summary>
    /// Acrescenta novas sugestões encontradas numa análise, sem duplicar um mesmo arquivo+tipo que
    /// já esteja Pendente ou Aprovado (evita a mesma sugestão reaparecer a cada análise).
    /// </summary>
    public void MesclarNovasSugestoes(string caminhoArquivo, List<SugestaoLimpeza> novas)
    {
        var existentes = Carregar(caminhoArquivo);
        var chavesAtivas = existentes
            .Where(s => s.Status is "Pendente" or "Aprovado")
            .Select(s => (s.Caminho, s.Tipo))
            .ToHashSet();

        foreach (var nova in novas)
        {
            if (chavesAtivas.Contains((nova.Caminho, nova.Tipo))) continue;
            existentes.Add(nova);
        }

        Salvar(caminhoArquivo, existentes);
    }

    /// <summary>Atualiza o status de UM item pelo Id, preservando todos os outros — nunca sobrescreve
    /// o arquivo inteiro com uma lista parcial (evitaria perder itens Ignorado/Excluido do histórico).</summary>
    public void AtualizarStatus(string caminhoArquivo, string id, string novoStatus)
    {
        var lista = Carregar(caminhoArquivo);
        var item = lista.FirstOrDefault(s => s.Id == id);
        if (item is null) return;

        item.Status = novoStatus;
        Salvar(caminhoArquivo, lista);
    }
}
