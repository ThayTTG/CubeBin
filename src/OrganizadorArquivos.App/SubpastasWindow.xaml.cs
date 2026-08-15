using System.Windows;

namespace OrganizadorArquivos.App;

/// <summary>
/// Diálogo para configurar, por extensão, uma subpasta dentro da pasta de destino da regra
/// (item 1 do feedback de uso — ex.: Compactados\WinRAR).
/// </summary>
public partial class SubpastasWindow : Window
{
    /// <summary>Preenchido com o resultado (extensão -> subpasta, só entradas não vazias) quando o usuário clica OK.</summary>
    public Dictionary<string, string> Resultado { get; private set; } = new();

    private readonly List<ExtensaoSubpastaItem> _itens;

    public SubpastasWindow(IEnumerable<string> extensoes, IReadOnlyDictionary<string, string> subpastasAtuais)
    {
        InitializeComponent();

        _itens = extensoes
            .Select(ext => new ExtensaoSubpastaItem
            {
                Extensao = ext,
                Subpasta = subpastasAtuais.TryGetValue(ext, out var valor) ? valor : string.Empty
            })
            .ToList();

        GridExtensoes.ItemsSource = _itens;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Resultado = _itens
            .Where(i => !string.IsNullOrWhiteSpace(i.Subpasta))
            .ToDictionary(i => i.Extensao, i => i.Subpasta.Trim());

        DialogResult = true;
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private class ExtensaoSubpastaItem
    {
        public string Extensao { get; set; } = string.Empty;
        public string Subpasta { get; set; } = string.Empty;
    }
}
