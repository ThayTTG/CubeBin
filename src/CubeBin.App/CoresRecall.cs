using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CubeBin.App;

/// <summary>
/// Cor de cada linha da lista "Arquivos movidos" (Histórico), a partir de CorLinha do item:
/// "Azul" = organizado, "Amarelo" = marcado para devolver (não salvo), "Verde" = já devolvido.
/// A cor nunca é a única pista: a coluna "Situação" escreve o mesmo estado.
/// </summary>
public static class CoresRecall
{
    public static readonly IValueConverter Fundo = new FuncValueConverter<string?, IBrush?>(cor => cor switch
    {
        "Azul" => Recurso("RecallAtivoBrush"),
        "Amarelo" => Recurso("RecallPendenteBrush"),
        "Verde" => Recurso("RecallRestauradoBrush"),
        _ => Brushes.Transparent
    });

    public static readonly IValueConverter Texto = new FuncValueConverter<string?, IBrush?>(cor => cor switch
    {
        "Azul" => Recurso("AccentTextBrush"),
        _ => Recurso("TextBrush")
    });

    private static IBrush? Recurso(string chave)
    {
        var app = Application.Current;
        if (app is not null && app.TryFindResource(chave, app.ActualThemeVariant, out var valor) && valor is IBrush pincel)
            return pincel;
        return null;
    }
}
