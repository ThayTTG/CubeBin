using System.Text.Json;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>Lê/grava pacotes.json — o histórico de todos os pacotes de backup já enviados,
/// consumido pela aba "Cubos" (Camada 4).</summary>
public class PacoteBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<PacoteBackup> Carregar(string caminhoArquivo) =>
        ArquivoJsonUtils.CarregarListaResiliente<PacoteBackup>(caminhoArquivo, JsonOptions);

    public void Salvar(string caminhoArquivo, List<PacoteBackup> pacotes)
    {
        var diretorio = Path.GetDirectoryName(caminhoArquivo);
        if (!string.IsNullOrEmpty(diretorio))
            Directory.CreateDirectory(diretorio);

        File.WriteAllText(caminhoArquivo, JsonSerializer.Serialize(pacotes, JsonOptions));
    }

    public void Adicionar(string caminhoArquivo, PacoteBackup pacote)
    {
        var lista = Carregar(caminhoArquivo);
        lista.Add(pacote);
        Salvar(caminhoArquivo, lista);
    }
}
