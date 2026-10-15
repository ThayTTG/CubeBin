using System.Text.Json;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// Mantém a fila de arquivos não classificados (seção 5 da spec) em pendencias.json,
/// para que o usuário decida na próxima interação com a UI.
/// </summary>
public class PendenciaService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<ArquivoPendente> Carregar(string caminhoArquivo) =>
        ArquivoJsonUtils.CarregarListaResiliente<ArquivoPendente>(caminhoArquivo, JsonOptions);

    public void Salvar(string caminhoArquivo, List<ArquivoPendente> pendencias)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminhoArquivo)!);
        var json = JsonSerializer.Serialize(pendencias, JsonOptions);
        File.WriteAllText(caminhoArquivo, json);
    }

    public void Adicionar(string caminhoArquivo, ArquivoPendente pendente)
    {
        var lista = Carregar(caminhoArquivo);

        // Evita duplicar o mesmo arquivo caso ele reapareça em execuções seguintes.
        if (lista.Any(p => p.CaminhoOriginal == pendente.CaminhoOriginal && p.Status == "Pendente"))
            return;

        lista.Add(pendente);
        Salvar(caminhoArquivo, lista);
    }

    /// <summary>Usuário resolveu manualmente (moveu, criou regra ou marcou para ignorar) na UI.</summary>
    public void AtualizarStatus(string caminhoArquivo, string caminhoOriginalArquivoPendente, string novoStatus)
    {
        var lista = Carregar(caminhoArquivo);
        var item = lista.FirstOrDefault(p => p.CaminhoOriginal == caminhoOriginalArquivoPendente);
        if (item is null) return;

        item.Status = novoStatus;
        Salvar(caminhoArquivo, lista);
    }
}
