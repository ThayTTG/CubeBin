using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CubeBin.App;

/// <summary>
/// Diálogo para configurar, por extensão, uma subpasta dentro da pasta de destino da regra
/// (item 1 do feedback de uso — ex.: Compactados\WinRAR).
/// Abra com <c>await janela.ShowDialog&lt;bool&gt;(dona)</c>: devolve true quando o usuário salva.
/// </summary>
public partial class SubpastasWindow : Window
{
    /// <summary>Preenchido com o resultado (extensão -> subpasta, só entradas não vazias) quando o usuário clica OK.</summary>
    public Dictionary<string, string> Resultado { get; private set; } = new();

    private readonly List<ExtensaoSubpastaItem> _itens = new();

    public SubpastasWindow()
    {
        // Só para o previewer do Avalonia.
        InitializeComponent();
    }

    public SubpastasWindow(IEnumerable<string> extensoes, IReadOnlyDictionary<string, string> subpastasAtuais)
    {
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);

        _itens = extensoes
            .Select(ext => new ExtensaoSubpastaItem
            {
                Extensao = ext,
                Subpasta = subpastasAtuais.TryGetValue(ext, out var valor) ? valor : string.Empty
            })
            .ToList();

        GridExtensoes.ItemsSource = _itens;
    }

    private void BtnOk_Click(object? sender, RoutedEventArgs e)
    {
        // Confirma uma célula que ainda esteja sendo editada antes de ler os valores.
        GridExtensoes.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        Resultado = _itens
            .Where(i => !string.IsNullOrWhiteSpace(i.Subpasta))
            .ToDictionary(i => i.Extensao, i => i.Subpasta.Trim());

        Close(true);
    }

    private void BtnCancelar_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private class ExtensaoSubpastaItem
    {
        public string Extensao { get; set; } = string.Empty;
        public string Subpasta { get; set; } = string.Empty;
    }
}
