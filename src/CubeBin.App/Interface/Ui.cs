using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Plataforma;
using FormaPath = Avalonia.Controls.Shapes.Path;

namespace CubeBin.App.Interface;

/// <summary>
/// Atalhos para montar pedaços de tela em código (listas que mudam: blocos de pasta, bloquinhos de
/// extensão, Cubos), sempre com as classes do Kit NokyLabs — nenhuma cor escrita à mão.
/// </summary>
public static class Ui
{
    public static object? Recurso(string chave) =>
        Application.Current?.TryFindResource(chave, Application.Current.ActualThemeVariant, out var valor) == true ? valor : null;

    public static Viewbox Icone(string chave, double tamanho = 18, IBrush? cor = null)
    {
        var path = new FormaPath { Data = Recurso(chave) as Geometry };
        path.Classes.Add("icon");
        if (cor is not null) path.Stroke = cor;
        return new Viewbox { Width = tamanho, Height = tamanho, Child = path };
    }

    public static TextBlock Texto(string texto, params string[] classes)
    {
        var bloco = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap };
        foreach (var c in classes) bloco.Classes.Add(c);
        return bloco;
    }

    public static Button Botao(string texto, string classes, string? icone, EventHandler<RoutedEventArgs> aoClicar)
    {
        var botao = new Button { Content = texto };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) botao.Classes.Add(c);
        if (icone is not null)
        {
            botao.Classes.Add("ic");
            botao.Tag = Recurso(icone);
        }
        botao.Click += aoClicar;
        return botao;
    }

    /// <summary>Quadradinho com o ícone de um bloco (fundo suave na cor do app).</summary>
    public static Border IconeDeBloco(string chave, double tamanho = 40)
    {
        var borda = new Border { Width = tamanho, Height = tamanho, Child = Icone(chave, tamanho * 0.48) };
        borda.Classes.Add("iconeBloco");
        return borda;
    }

    public static Border Selo(string texto, string tipo = "")
    {
        var selo = new Border { Child = new TextBlock { Text = texto }, VerticalAlignment = VerticalAlignment.Center };
        selo.Classes.Add("badge");
        if (tipo.Length > 0) selo.Classes.Add(tipo);
        return selo;
    }

    public static Border Aviso(string texto, string tipo = "", string icone = "IconeInfo")
    {
        var linha = new DockPanel();
        var ic = Icone(icone, 16);
        ic.Margin = new Thickness(0, 1, 10, 0);
        ic.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(ic, Dock.Left);
        linha.Children.Add(ic);
        linha.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap });
        var aviso = new Border { Child = linha };
        aviso.Classes.Add("notice");
        if (tipo.Length > 0) aviso.Classes.Add(tipo);
        return aviso;
    }

    /// <summary>O ícone de uma pasta pelo tipo (Imagens → imagem...).</summary>
    public static string IconeDaPasta(PastaConfig? pasta) => pasta?.Padrao switch
    {
        PastaPadrao.Imagens => "IconeImagem",
        PastaPadrao.Videos => "IconeVideo",
        PastaPadrao.Musicas => "IconeMusica",
        PastaPadrao.Documentos => "IconeDocumento",
        PastaPadrao.Downloads => "IconeBaixar",
        PastaPadrao.AreaDeTrabalho => "IconeInicio",
        _ => "IconePasta",
    };

    public static string IconeDaCategoria(string categoria) => categoria switch
    {
        "Imagens" => "IconeImagem",
        "Vídeos" => "IconeVideo",
        "Músicas" => "IconeMusica",
        "Documentos" => "IconeDocumento",
        _ => "IconeCubo",
    };

    public static IBrush? CorDaCategoria(string categoria) => Recurso(categoria switch
    {
        "Imagens" => "CorImagens",
        "Vídeos" => "CorVideos",
        "Músicas" => "CorMusicas",
        "Documentos" => "CorDocumentos",
        _ => "CorOutros",
    }) as IBrush;

    public static string Plural(int n, string singular, string plural) => n == 1 ? $"1 {singular}" : $"{n} {plural}";

    /// <summary>Abre um arquivo ou pasta no programa padrão do sistema (relatório, pasta de destino).</summary>
    public static bool Abrir(string caminho)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return ComandoExterno.Abrir("xdg-open", caminho);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(caminho) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
