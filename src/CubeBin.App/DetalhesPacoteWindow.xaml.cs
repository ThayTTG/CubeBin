using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using CubeBin.Core.Models;
using CubeBin.Core.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace CubeBin.App;

/// <summary>
/// Detalhes de um pacote de backup ("Cubo" — Camada 4): lista de arquivos, data do envio, link
/// de acesso no Drive, e a opção de restaurar (baixar o .zip de volta e descompactar).
/// </summary>
public partial class DetalhesPacoteWindow : Window
{
    private readonly PacoteBackup _pacote;
    private readonly GoogleDriveService _googleDriveService;

    public DetalhesPacoteWindow(PacoteBackup pacote, GoogleDriveService googleDriveService)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);
        _pacote = pacote;
        _googleDriveService = googleDriveService;

        TxtTituloPacote.Text = pacote.NomeArquivoZip;
        TxtDataPacote.Text = $"Enviado em {pacote.EnviadoEm:dd/MM/yyyy HH:mm} — {FormatarBytes.Formatar(pacote.TamanhoBytes)}";
        TxtCaminhoDrivePacote.Text = $"Google Drive: {pacote.CaminhoDrive}";

        GridItensPacote.ItemsSource = pacote.Itens.Select(i => new ItemExibicao(i)).ToList();

        BtnAbrirNoDrive.IsEnabled = !string.IsNullOrWhiteSpace(pacote.LinkDrive);
    }

    private void BtnAbrirNoDrive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pacote.LinkDrive)) return;

        try
        {
            Process.Start(new ProcessStartInfo(_pacote.LinkDrive) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o link: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRestaurarPacote_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Escolher pasta para restaurar o pacote" };
        if (dialogo.ShowDialog() != true) return;

        BtnRestaurarPacote.IsEnabled = false;
        BtnRestaurarPacote.Content = "Restaurando...";

        var caminhoZipTemp = Path.Combine(Path.GetTempPath(), _pacote.NomeArquivoZip);

        try
        {
            await _googleDriveService.BaixarArquivoAsync(_pacote.IdArquivoDrive, caminhoZipTemp);
            ZipFile.ExtractToDirectory(caminhoZipTemp, dialogo.FolderName, overwriteFiles: true);

            MessageBox.Show($"Pacote restaurado em:\n{dialogo.FolderName}", "CubeBin",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível restaurar o pacote: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { if (File.Exists(caminhoZipTemp)) File.Delete(caminhoZipTemp); }
            catch (IOException) { /* best-effort */ }

            BtnRestaurarPacote.IsEnabled = true;
            BtnRestaurarPacote.Content = "Restaurar (baixar de volta)";
        }
    }

    private class ItemExibicao
    {
        public string NomeArquivo { get; }
        public string Categoria { get; }
        public string TamanhoTexto { get; }
        public string CaminhoOriginal { get; }

        public ItemExibicao(ItemPacoteBackup item)
        {
            NomeArquivo = item.NomeArquivo;
            Categoria = item.Categoria;
            TamanhoTexto = FormatarBytes.Formatar(item.TamanhoBytes);
            CaminhoOriginal = item.CaminhoOriginal;
        }
    }
}
