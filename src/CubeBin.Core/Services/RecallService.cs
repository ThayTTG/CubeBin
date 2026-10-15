using System.Text.Json;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// Mantém o histórico de arquivos movidos (recall.json) e permite devolvê-los ao local
/// original — a "rede de segurança" além do backup em CSV, que continua existindo sem alterações.
/// </summary>
public class RecallService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<ItemRecall> Carregar(string caminhoArquivo) =>
        ArquivoJsonUtils.CarregarListaResiliente<ItemRecall>(caminhoArquivo, JsonOptions);

    public void Salvar(string caminhoArquivo, List<ItemRecall> itens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminhoArquivo)!);
        var json = JsonSerializer.Serialize(itens, JsonOptions);
        File.WriteAllText(caminhoArquivo, json);
    }

    public void Adicionar(string caminhoArquivo, ItemRecall item)
    {
        var lista = Carregar(caminhoArquivo);
        lista.Add(item);
        Salvar(caminhoArquivo, lista);
    }

    /// <summary>
    /// Devolve um arquivo ao seu local de origem — de uma pasta local ou do Google Drive,
    /// dependendo de item.Armazenamento. Se já existir um arquivo com o mesmo nome no destino
    /// (ex.: o usuário criou outro depois), adiciona um sufixo " (restaurado)" para não sobrescrever.
    /// </summary>
    public async Task RestaurarAsync(string caminhoArquivoRecall, ItemRecall item, GoogleDriveService googleDriveService)
    {
        var diretorioOrigem = Path.GetDirectoryName(item.CaminhoOrigem)!;
        Directory.CreateDirectory(diretorioOrigem);
        var destinoFinal = ResolverConflitoDeNome(item.CaminhoOrigem);

        if (item.Armazenamento == "GoogleDrive")
        {
            if (string.IsNullOrWhiteSpace(item.IdArquivoDrive))
                throw new InvalidOperationException("Este item do Recall não tem um ID de arquivo do Google Drive associado.");

            // Só apaga do Drive depois de confirmar que o download terminou — mesma filosofia de
            // segurança do upload: nunca perder o arquivo por causa de uma etapa intermediária falhar.
            await googleDriveService.BaixarArquivoAsync(item.IdArquivoDrive, destinoFinal);
            await googleDriveService.ExcluirArquivoAsync(item.IdArquivoDrive);
        }
        else
        {
            if (!File.Exists(item.CaminhoAtual))
                throw new FileNotFoundException("O arquivo não está mais no local esperado — pode ter sido movido ou apagado manualmente.", item.CaminhoAtual);

            ArquivoUtils.MoverArquivo(item.CaminhoAtual, destinoFinal, sobrescrever: false);
        }

        var lista = Carregar(caminhoArquivoRecall);
        var registrado = lista.FirstOrDefault(i => i.Id == item.Id);
        if (registrado is not null)
            registrado.Status = "Restaurado";

        Salvar(caminhoArquivoRecall, lista);
    }

    private static string ResolverConflitoDeNome(string caminhoOrigem)
    {
        if (!File.Exists(caminhoOrigem))
            return caminhoOrigem;

        var diretorio = Path.GetDirectoryName(caminhoOrigem)!;
        var semExtensao = Path.GetFileNameWithoutExtension(caminhoOrigem);
        var extensao = Path.GetExtension(caminhoOrigem);
        var contador = 1;
        string candidato;
        do
        {
            candidato = Path.Combine(diretorio, $"{semExtensao} (restaurado{(contador > 1 ? $" {contador}" : "")}){extensao}");
            contador++;
        } while (File.Exists(candidato));

        return candidato;
    }
}
