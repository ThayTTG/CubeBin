using System.Text.Json;

namespace CubeBin.Core.Services;

/// <summary>
/// Leitura resiliente dos arquivos JSON de estado auxiliar do app (pendências, recall, sugestões,
/// pacotes de backup). Usada pelos serviços de persistência para nunca deixar um arquivo
/// corrompido (ex.: gravação interrompida por queda de energia ou o processo sendo encerrado no
/// meio de uma escrita) travar a inicialização do app inteiro — todos esses `Carregar()` rodam no
/// construtor da MainWindow, então uma exceção aqui derrubaria o app antes mesmo de abrir.
/// Se o JSON não puder ser lido/desserializado, o arquivo problemático é renomeado (não perde o
/// rastro dele, dá pra investigar depois) e uma lista vazia é retornada em vez de propagar a exceção.
/// </summary>
public static class ArquivoJsonUtils
{
    public static List<T> CarregarListaResiliente<T>(string caminhoArquivo, JsonSerializerOptions opcoes)
    {
        if (!File.Exists(caminhoArquivo))
            return new List<T>();

        try
        {
            var json = File.ReadAllText(caminhoArquivo);
            return JsonSerializer.Deserialize<List<T>>(json, opcoes) ?? new List<T>();
        }
        catch (Exception)
        {
            RenomearComoCorrompido(caminhoArquivo);
            return new List<T>();
        }
    }

    private static void RenomearComoCorrompido(string caminhoArquivo)
    {
        try
        {
            var destino = $"{caminhoArquivo}.corrompido-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(caminhoArquivo, destino);
        }
        catch (Exception)
        {
            // Best-effort — se nem isso for possível, seguimos mesmo assim com a lista vazia.
        }
    }
}
