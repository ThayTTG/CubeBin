using Avalonia.Controls;
using Avalonia.Interactivity;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.App.Telas;

namespace CubeBin.App;

/// <summary>Uma página que quer saber quando é mostrada (para recarregar dados).</summary>
public interface ITela
{
    void AoEntrar(object? parametro);
}

/// <summary>A janela principal: menu lateral e a página da vez.</summary>
public partial class MainWindow : Window
{
    private readonly ServicosApp _servicos;
    private readonly Dictionary<string, Control> _paginas = new();
    private bool _navegandoPeloCodigo;

    public MainWindow() : this(App.Servicos!) { }

    public MainWindow(ServicosApp servicos)
    {
        _servicos = servicos;
        InitializeComponent();
        TemaJanelaEscura.Aplicar(this);
        Creditos.Version = "v" + InfoApp.Versao;

        foreach (var nav in new[] { NavInicio, NavOrganizar, NavLiberar, NavCubos, NavConfiguracoes, NavSobre })
            nav.IsCheckedChanged += AoTrocarMenu;

        Navegar("inicio");
    }

    private void AoTrocarMenu(object? sender, RoutedEventArgs e)
    {
        if (_navegandoPeloCodigo || sender is not RadioButton { IsChecked: true, Tag: string pagina }) return;
        Navegar(pagina);
    }

    /// <summary>Mostra uma página. <paramref name="parametro"/> vai para ITela.AoEntrar.</summary>
    public void Navegar(string pagina, object? parametro = null)
    {
        if (!_paginas.TryGetValue(pagina, out var controle))
        {
            controle = CriarPagina(pagina);
            _paginas[pagina] = controle;
        }

        _navegandoPeloCodigo = true;
        foreach (var nav in new[] { NavInicio, NavOrganizar, NavLiberar, NavCubos, NavConfiguracoes, NavSobre })
            nav.IsChecked = Equals(nav.Tag, pagina) || (pagina == "regras" && Equals(nav.Tag, "configuracoes"));
        _navegandoPeloCodigo = false;

        Pagina.Content = controle;
        (controle as ITela)?.AoEntrar(parametro);
    }

    private Control CriarPagina(string pagina) => pagina switch
    {
        "inicio" => new TelaInicio(_servicos, this),
        "organizar" => new TelaOrganizar(_servicos, this),
        "liberar" => new TelaLiberar(_servicos, this),
        "sobre" => new TelaSobre(),
        _ => new TelaEmBreve(pagina),
    };
}
