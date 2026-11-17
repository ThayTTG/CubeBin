using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace CubeBin.App.Interface;

/// <summary>
/// Só para desenvolvimento: com a variável CUBEBIN_CAPTURA="pagina=arquivo.png;pagina2=arquivo2.png",
/// abre cada página, salva uma imagem dela e fecha o app. Serve para conferir o visual das telas
/// sem precisar clicar (ex.: no CI ou no Linux sem tela, com Xvfb). Sem a variável, não faz nada.
/// </summary>
public static class Captura
{
    public static void TalvezCapturar(MainWindow janela, Action sair)
    {
        var pedido = Environment.GetEnvironmentVariable("CUBEBIN_CAPTURA");
        if (string.IsNullOrWhiteSpace(pedido)) return;

        var itens = pedido.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToList();

        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            foreach (var item in itens)
            {
                // "clique:NomeDoBotao=..." clica num botão; "pagina[:parametro]=arquivo.png" abre a página.
                foreach (var passo in item[0].Split('+'))
                {
                    var partes = passo.Split(':', 2);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (partes[0] == "clique")
                        {
                            var botao = janela.GetLogicalDescendants().OfType<Avalonia.Controls.Button>()
                                .FirstOrDefault(b => b.Name == partes[1] && b.IsEffectivelyVisible);
                            botao?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                        }
                        else if (partes[0] == "botao")
                        {
                            var botao = janela.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                                .FirstOrDefault(b => Equals(b.Content, partes[1]) && b.IsEffectivelyVisible);
                            botao?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                        }
                        else if (partes[0] == "bloco")
                        {
                            // Simula o clique num bloco pelo texto dentro dele.
                            var bloco = janela.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                                .FirstOrDefault(b => b.Classes.Contains("clicavel") && b.IsEffectivelyVisible &&
                                    b.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == partes[1]));
                            bloco?.RaiseEvent(new Avalonia.Input.PointerReleasedEventArgs(bloco, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
                                janela, default, 0, new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None, Avalonia.Input.MouseButton.Left)
                                { RoutedEvent = Avalonia.Input.InputElement.PointerReleasedEvent });
                        }
                        else janela.Navegar(partes[0], partes.Length > 1 ? partes[1] : null);
                    });
                    await Task.Delay(2500);
                }
                if (item[1] == "-") continue;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var tamanho = new PixelSize((int)janela.Bounds.Width, (int)janela.Bounds.Height);
                    using var bitmap = new RenderTargetBitmap(tamanho);
                    bitmap.Render(janela);
                    bitmap.Save(item[1]);
                });
            }
            await Dispatcher.UIThread.InvokeAsync(sair);
        });
    }
}
