using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CubeBin.Core.Cubos;
using CubeBin.Core.Plataforma;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace CubeBin.App.Interface;

/// <summary>
/// O cartão de um Cubo (tela inicial e tela de Cubos): ícone pelo tipo, título, data, quantidade e uma
/// barrinha colorida com a proporção de cada tipo de arquivo. Ao passar o mouse, o preview com a
/// quantidade de arquivos por tipo (PRD 5.6).
/// </summary>
public static class CartaoCubo
{
    public static Border Criar(Cubo cubo, Action<Cubo>? aoClicar, double largura = 200)
    {
        var pilha = new StackPanel { Spacing = 8 };

        var topo = new DockPanel();
        var icone = Ui.IconeDeBloco(IconeDoTipo(cubo), 36);
        DockPanel.SetDock(icone, Dock.Left);
        icone.Margin = new Thickness(0, 0, 10, 0);
        topo.Children.Add(icone);
        var titulos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titulos.Children.Add(new TextBlock { Text = cubo.Titulo, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        titulos.Children.Add(Ui.Texto(DataAmigavel(cubo.Data), "pequeno"));
        topo.Children.Add(titulos);
        pilha.Children.Add(topo);

        pilha.Children.Add(Ui.Texto($"{Ui.Plural(cubo.Arquivos, "arquivo", "arquivos")} · {FormatarBytes.Formatar(cubo.Bytes)}", "secondary"));
        pilha.Children.Add(BarraDeTipos(cubo));

        if (cubo.Restaurado) pilha.Children.Add(Ui.Selo("Restaurado", "success"));
        else if (cubo.EnvioAdiado) pilha.Children.Add(Ui.Selo("Envio adiado", "warning"));
        else if (cubo.Falhas > 0) pilha.Children.Add(Ui.Selo($"{cubo.Falhas} com problema", "danger"));

        var cartao = new Border { Child = pilha, Width = largura };
        cartao.Classes.Add("bloco");
        if (aoClicar is not null)
        {
            cartao.Classes.Add("clicavel");
            cartao.PointerReleased += (_, _) => aoClicar(cubo);
        }
        ToolTip.SetTip(cartao, Preview(cubo));
        return cartao;
    }

    /// <summary>O preview do PRD: quantos arquivos de cada tipo.</summary>
    public static Control Preview(Cubo cubo)
    {
        var pilha = new StackPanel { Spacing = 4 };
        pilha.Children.Add(new TextBlock { Text = cubo.Titulo, FontWeight = FontWeight.SemiBold });
        if (cubo.PorCategoria.Count == 0) pilha.Children.Add(new TextBlock { Text = "Nenhum arquivo" });
        foreach (var (categoria, n) in cubo.PorCategoria.OrderByDescending(p => p.Value))
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            linha.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Ui.CorDaCategoria(categoria), VerticalAlignment = VerticalAlignment.Center });
            linha.Children.Add(new TextBlock { Text = Cubo.NomeDaCategoria(categoria, n) });
            pilha.Children.Add(linha);
        }
        if (cubo.PastasDeOrigem.Count > 0)
            pilha.Children.Add(new TextBlock { Text = "De: " + string.Join(", ", cubo.PastasDeOrigem), Opacity = 0.75 });
        return pilha;
    }

    /// <summary>Barrinha com a proporção de cada tipo, nas cores das categorias.</summary>
    public static Control BarraDeTipos(Cubo cubo)
    {
        var grade = new Grid { Height = 6, ClipToBounds = true };
        var total = Math.Max(1, cubo.PorCategoria.Values.Sum());
        var coluna = 0;
        foreach (var (categoria, n) in cubo.PorCategoria.OrderByDescending(p => p.Value))
        {
            grade.ColumnDefinitions.Add(new ColumnDefinition(new GridLength((double)n / total, GridUnitType.Star)));
            var parte = new Border { Background = Ui.CorDaCategoria(categoria), Margin = new Thickness(0, 0, 2, 0) };
            Grid.SetColumn(parte, coluna++);
            grade.Children.Add(parte);
        }
        if (coluna == 0)
            grade.Children.Add(new Border { Background = Ui.Recurso("SurfaceRaisedBrush") as IBrush });
        return new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = grade };
    }

    public static string IconeDoTipo(Cubo cubo) => cubo.Tipo switch
    {
        TipoCubo.Organizado => "IconeOrganizar",
        TipoCubo.Lixeira => "IconeLixeira",
        TipoCubo.ApagadoDeVez => "IconeArquivoX",
        TipoCubo.Nuvem => "IconeNuvem",
        _ => "IconeCubo",
    };

    public static string DataAmigavel(DateTime data)
    {
        var hoje = DateTime.Today;
        if (data.Date == hoje) return $"Hoje, {data:HH:mm}";
        if (data.Date == hoje.AddDays(-1)) return $"Ontem, {data:HH:mm}";
        return data.ToString("dd/MM/yyyy, HH:mm");
    }
}
