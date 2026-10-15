using System.IO.Compression;
using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

/// <summary>
/// Orquestra o fluxo "compactar -> enviar -> só então apagar localmente" (Camada 3). Mesma
/// filosofia de segurança do resto do app: nenhum arquivo local é apagado antes de confirmar que
/// o upload terminou com sucesso. Se qualquer etapa falhar antes dessa confirmação, nada local é
/// tocado — o usuário é avisado do erro, com a opção de simplesmente tentar de novo.
/// </summary>
public class BackupNuvemService
{
    private readonly GoogleDriveService _googleDriveService;
    private readonly PacoteBackupService _pacoteBackupService;

    public BackupNuvemService(GoogleDriveService googleDriveService, PacoteBackupService pacoteBackupService)
    {
        _googleDriveService = googleDriveService;
        _pacoteBackupService = pacoteBackupService;
    }

    public async Task<ResultadoBackupNuvem> EnviarEDeletarAsync(
        List<string> caminhosLocais, string caminhoArquivoPacotes, string caminhoDrivePastaBackups)
    {
        var resultado = new ResultadoBackupNuvem();
        var nomeZip = $"CubeBin-Backup-{DateTime.Now:yyyy-MM-dd_HHmmss}.zip";
        var caminhoZipTemp = Path.Combine(Path.GetTempPath(), nomeZip);
        var itensIncluidos = new List<ItemPacoteBackup>();

        try
        {
            CriarZip(caminhosLocais, caminhoZipTemp, itensIncluidos);
        }
        catch (Exception ex)
        {
            resultado.Sucesso = false;
            resultado.MensagemErro = $"Não foi possível compactar os arquivos: {ex.Message}";
            LimparZipTemp(caminhoZipTemp);
            return resultado;
        }

        if (itensIncluidos.Count == 0)
        {
            resultado.Sucesso = false;
            resultado.MensagemErro = "Nenhum arquivo válido encontrado para compactar.";
            LimparZipTemp(caminhoZipTemp);
            return resultado;
        }

        string idArquivoDrive;
        string? link;
        try
        {
            (idArquivoDrive, link) = await _googleDriveService.EnviarArquivoComLinkAsync(caminhoZipTemp, caminhoDrivePastaBackups);
        }
        catch (Exception ex)
        {
            // Upload falhou ou foi interrompido: nenhum arquivo local é apagado.
            resultado.Sucesso = false;
            resultado.MensagemErro = $"Falha no envio para o Google Drive: {ex.Message}";
            LimparZipTemp(caminhoZipTemp);
            return resultado;
        }

        // Upload confirmado — só a partir daqui os arquivos locais são apagados.
        var tamanhoZip = new FileInfo(caminhoZipTemp).Length;
        var pacote = new PacoteBackup
        {
            NomeArquivoZip = nomeZip,
            IdArquivoDrive = idArquivoDrive,
            LinkDrive = link,
            CaminhoDrive = caminhoDrivePastaBackups,
            TamanhoBytes = tamanhoZip,
            Itens = itensIncluidos
        };
        _pacoteBackupService.Adicionar(caminhoArquivoPacotes, pacote);

        foreach (var caminho in caminhosLocais)
        {
            // Se o arquivo já não existe mais (removido por outra coisa nesse meio-tempo), não há
            // nada a fazer — não conta como sucesso nem como falha, evita inflar o relatório final.
            if (!File.Exists(caminho)) continue;

            try
            {
                File.Delete(caminho);
                resultado.Deletados.Add(caminho);
            }
            catch (Exception ex)
            {
                // O arquivo já está seguro na nuvem; só não pôde ser removido localmente agora.
                resultado.FalhasAoDeletar.Add($"{Path.GetFileName(caminho)}: {ex.Message}");
            }
        }

        resultado.Sucesso = true;
        resultado.Pacote = pacote;
        LimparZipTemp(caminhoZipTemp);
        return resultado;
    }

    private static void CriarZip(List<string> caminhos, string caminhoZip, List<ItemPacoteBackup> itensIncluidos)
    {
        using var zip = ZipFile.Open(caminhoZip, ZipArchiveMode.Create);

        foreach (var caminho in caminhos)
        {
            if (!File.Exists(caminho)) continue;

            var info = new FileInfo(caminho);
            zip.CreateEntryFromFile(caminho, info.Name);

            itensIncluidos.Add(new ItemPacoteBackup
            {
                NomeArquivo = info.Name,
                CaminhoOriginal = info.FullName,
                TamanhoBytes = info.Length,
                Categoria = CategoriaArquivo.Classificar(info.Extension)
            });
        }
    }

    private static void LimparZipTemp(string caminho)
    {
        try
        {
            if (File.Exists(caminho))
                File.Delete(caminho);
        }
        catch (IOException)
        {
            // Best-effort — não é crítico se o temporário não puder ser removido agora.
        }
    }
}
