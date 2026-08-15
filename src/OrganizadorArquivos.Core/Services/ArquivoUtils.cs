namespace OrganizadorArquivos.Core.Services;

/// <summary>
/// Movimentação de arquivo blindada contra o caso de origem e destino estarem em discos/volumes
/// diferentes (ex.: mover de C: para D:) — cenário que pode falhar com File.Move puro dependendo
/// do sistema de arquivos e da versão do Windows. Aqui há um fallback explícito para copiar e só
/// então apagar o original, garantindo que a movimentação funcione entre volumes distintos.
/// </summary>
public static class ArquivoUtils
{
    public static void MoverArquivo(string origem, string destino, bool sobrescrever = false)
    {
        try
        {
            File.Move(origem, destino, sobrescrever);
        }
        catch (IOException)
        {
            // Provável falha de movimentação entre discos diferentes: copia e só depois apaga o
            // original, para nunca perder o arquivo mesmo se a cópia for interrompida no meio.
            File.Copy(origem, destino, sobrescrever);
            File.Delete(origem);
        }
    }
}
