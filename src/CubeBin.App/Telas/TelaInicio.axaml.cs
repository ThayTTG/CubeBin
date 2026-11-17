using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core.Cubos;

namespace CubeBin.App.Telas;

/// <summary>A tela inicial.</summary>
public partial class TelaInicio : UserControl, ITela
{
    private readonly ServicosApp _servicos;
    private readonly MainWindow _janela;
    private string? _ultimoRelatorio;
    private bool _preenchendo;

    public TelaInicio() : this(App.Servicos!, null!) { }

    public TelaInicio(ServicosApp servicos, MainWindow janela)
    {
        _servicos = servicos;
        _janela = janela;
        InitializeComponent();
        _servicos.Cubos.Alterado += () => Avalonia.Threading.Dispatcher.UIThread.Post(Atualizar);
    }

    public void AoEntrar(object? parametro) => Atualizar();

    private void Atualizar()
    {
        _preenchendo = true;
        var config = _servicos.Config.Carregar();
        var hora = DateTime.Now.Hour;
        TxtSaudacao.Text = hora < 12 ? "Bom dia!" : hora < 18 ? "Boa tarde!" : "Boa noite!";

        ChaveAutomacao.IsChecked = config.Automacao.Ligada;
        TxtAutomacao.Text = config.Automacao.Ligada ? "Organização automática ligada" : "Organização automática desligada";
        TxtAutomacaoDetalhe.Text = config.Automacao.Ligada
            ? $"Quando você ficar {config.Automacao.MinutosDeAusencia} min longe do computador, o CubeBin avisa e organiza (dá {config.Automacao.SegundosParaCancelar} s para cancelar)."
            : "Ligue para o CubeBin organizar sozinho quando você estiver longe do computador.";

        var cubos = _servicos.Cubos.Listar();
        ListaCubos.Children.Clear();
        foreach (var cubo in cubos.Take(6))
            ListaCubos.Children.Add(CartaoCubo.Criar(cubo, c => _janela.Navegar("cubos", c.Id)));
        TxtSemCubos.IsVisible = cubos.Count == 0;

        var ultimo = cubos.FirstOrDefault(c => !string.IsNullOrEmpty(c.CaminhoRelatorio) && File.Exists(c.CaminhoRelatorio));
        _ultimoRelatorio = ultimo?.CaminhoRelatorio;
        CartaoRelatorio.IsVisible = ultimo is not null;
        if (ultimo is not null)
            TxtRelatorio.Text = $"{ultimo.Titulo} · {CartaoCubo.DataAmigavel(ultimo.Data)} · {Path.GetFileName(ultimo.CaminhoRelatorio)}";

        var ultimaOrganizacao = cubos.FirstOrDefault(c => c.Tipo == TipoCubo.Organizado);
        TxtStatus.Text = ultimaOrganizacao is null
            ? "Seus arquivos, sempre no lugar certo — e sempre com um caminho de volta."
            : $"Última organização: {CartaoCubo.DataAmigavel(ultimaOrganizacao.Data).ToLowerInvariant()}, " +
              $"{Ui.Plural(ultimaOrganizacao.Arquivos, "arquivo", "arquivos")}.";

        PainelAvisos.Children.Clear();
        if (config.ExtensoesSemPasta.Count > 0)
        {
            var aviso = Ui.Aviso($"{Ui.Plural(config.ExtensoesSemPasta.Count, "tipo de arquivo está", "tipos de arquivo estão")} " +
                                 $"esperando você escolher a pasta ({string.Join(", ", config.ExtensoesSemPasta.Take(6))}). " +
                                 "Clique aqui para decidir.", "warning", "IconeDuvida");
            aviso.Cursor = new Cursor(StandardCursorType.Hand);
            aviso.PointerReleased += (_, _) => _janela.Navegar("regras");
            PainelAvisos.Children.Add(aviso);
        }
        if (_servicos.Config.AvisoDeCarga is { } avisoDeCarga)
            PainelAvisos.Children.Add(Ui.Aviso(avisoDeCarga, "danger", "IconeAlerta"));
        _preenchendo = false;
    }

    private void ChaveAutomacao_Changed(object? sender, RoutedEventArgs e)
    {
        if (_preenchendo) return;
        var ligada = ChaveAutomacao.IsChecked == true;
        _servicos.Config.Alterar(c => c.Automacao.Ligada = ligada);
        Atualizar();
    }

    private void BtnOrganizar_Click(object? sender, RoutedEventArgs e) => _janela.Navegar("organizar");
    private void BlocoOrganizar_Click(object? sender, PointerReleasedEventArgs e) => _janela.Navegar("organizar");
    private void BtnLiberar_Click(object? sender, RoutedEventArgs e) => _janela.Navegar("liberar");
    private void BlocoLiberar_Click(object? sender, PointerReleasedEventArgs e) => _janela.Navegar("liberar");
    private void BtnVerTodos_Click(object? sender, RoutedEventArgs e) => _janela.Navegar("cubos");

    private void BtnRelatorio_Click(object? sender, RoutedEventArgs e)
    {
        if (_ultimoRelatorio is not null && !Ui.Abrir(_ultimoRelatorio))
            Dialogo.Show($"O relatório está em:\n{_ultimoRelatorio}", "Relatório");
    }
}
