using Avalonia.Controls;
using Avalonia.Interactivity;
using CubeBin.App.Interface;

namespace CubeBin.App.Telas;

public partial class TelaSobre : UserControl
{
    public TelaSobre()
    {
        InitializeComponent();
        TxtVersao.Text = "Versão " + InfoApp.Versao;
    }

    private void BtnSite_Click(object? sender, RoutedEventArgs e) => InfoApp.AbrirNoNavegador(InfoApp.UrlSite);
    private void BtnCodigo_Click(object? sender, RoutedEventArgs e) => InfoApp.AbrirNoNavegador(InfoApp.UrlCodigo);
    private void BtnProblema_Click(object? sender, RoutedEventArgs e) => InfoApp.AbrirNoNavegador(InfoApp.UrlProblema);
    private void BtnDados_Click(object? sender, RoutedEventArgs e) => Ui.Abrir(App.Servicos!.Ambiente.PastaDados);
}
