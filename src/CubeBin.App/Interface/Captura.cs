using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
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
