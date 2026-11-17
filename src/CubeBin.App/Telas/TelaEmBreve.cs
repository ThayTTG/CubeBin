using Avalonia.Controls;
using Avalonia.Layout;
using CubeBin.App.Interface;

namespace CubeBin.App.Telas;

/// <summary>Página ainda não construída nesta etapa do roadmap.</summary>
public class TelaEmBreve : UserControl
{
    public TelaEmBreve(string nome)
    {
        var pilha = new StackPanel { Margin = new Avalonia.Thickness(32, 28), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        pilha.Children.Add(Ui.Texto("Em breve", "titulo"));
        pilha.Children.Add(Ui.Texto($"A tela \"{nome}\" chega numa próxima versão.", "descricao"));
        Content = pilha;
    }
}
