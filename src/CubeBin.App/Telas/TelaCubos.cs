using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core.Cubos;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Telas;

/// <summary>
/// A tela de Cubos (PRD 5.6), aberta pelo "Ver todos" da tela inicial: um botão para voltar ao início,
/// filtros e resumo à esquerda, os Cubos por data à direita. Restaurar devolve cada arquivo ao lugar de
/// origem usando o registro JSON do Cubo.
/// </summary>
public class TelaCubos : UserControl, ITela
{
    private readonly ServicosApp _servicos;
    private readonly MainWindow _janela;
    private readonly StackPanel _filtrosTipo = new() { Spacing = 6 };
    private readonly StackPanel _filtrosPeriodo = new() { Spacing = 6 };
    private readonly StackPanel _resumo = new() { Spacing = 10 };
    private readonly StackPanel _lista = new() { Spacing = 18 };
    private readonly ContentControl _detalhe = new();
    private string _tipo = "todos";
    private int _dias;
    private string? _selecionado;

    public TelaCubos(ServicosApp servicos, MainWindow janela)
    {
        _servicos = servicos;
        _janela = janela;
        _servicos.Cubos.Alterado += () => Dispatcher.UIThread.Post(() => { if (IsVisible) Atualizar(); });

        foreach (var (id, nome) in new[] { ("todos", "Todos"), ("organizado", "Organizados"), ("lixeira", "Na Lixeira"), ("nuvem", "Na nuvem"), ("apagado", "Apagados de vez") })
            _filtrosTipo.Children.Add(Chip(nome, "tipoCubo", id == _tipo, () => { _tipo = id; Atualizar(); }));
        foreach (var (dias, nome) in new[] { (0, "Sempre"), (7, "Últimos 7 dias"), (30, "Últimos 30 dias") })
            _filtrosPeriodo.Children.Add(Chip(nome, "periodoCubo", dias == _dias, () => { _dias = dias; Atualizar(); }));

        var voltar = Ui.Botao("Voltar ao início", "secondary", "IconeSetaEsquerda", (_, _) => _janela.Navegar("inicio"));
        voltar.HorizontalAlignment = HorizontalAlignment.Stretch;

        var esquerda = new StackPanel { Width = 230, Spacing = 10 };
        esquerda.Children.Add(voltar);
        esquerda.Children.Add(Espaco(8));
        esquerda.Children.Add(Ui.Texto("TIPO", "caps"));
        esquerda.Children.Add(_filtrosTipo);
        esquerda.Children.Add(Espaco(8));
        esquerda.Children.Add(Ui.Texto("PERÍODO", "caps"));
        esquerda.Children.Add(_filtrosPeriodo);
        esquerda.Children.Add(Espaco(8));
        esquerda.Children.Add(Ui.Texto("RESUMO", "caps"));
        esquerda.Children.Add(_resumo);

        var direita = new StackPanel { Spacing = 16 };
        direita.Children.Add(_detalhe);
        direita.Children.Add(_lista);

        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,28,*") };
        grade.Children.Add(new ScrollViewer { Content = esquerda });
        var rolagem = new ScrollViewer { Content = direita };
        Grid.SetColumn(rolagem, 2);
        grade.Children.Add(rolagem);

        var pagina = new DockPanel { Margin = new Thickness(32, 28, 32, 24) };
        var topo = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        topo.Children.Add(Ui.Texto("Cubos", "titulo"));
        topo.Children.Add(Ui.Texto("Cada organização ou limpeza é um Cubo. Clique num Cubo para ver os detalhes e restaurar.", "descricao"));
        DockPanel.SetDock(topo, Dock.Top);
        pagina.Children.Add(topo);
        pagina.Children.Add(grade);
        Content = pagina;
    }

    public void AoEntrar(object? parametro)
    {
        if (parametro is string id) _selecionado = id;
        Atualizar();
    }

    private void Atualizar()
    {
        var todos = _servicos.Cubos.Listar();
        var filtrados = todos.Where(c => _tipo switch
            {
                "organizado" => c.Tipo == TipoCubo.Organizado,
                "lixeira" => c.Tipo == TipoCubo.Lixeira,
                "nuvem" => c.Tipo == TipoCubo.Nuvem,
                "apagado" => c.Tipo == TipoCubo.ApagadoDeVez,
                _ => true,
            })
            .Where(c => _dias == 0 || c.Data >= DateTime.Now.AddDays(-_dias))
            .ToList();

        // Resumo (do que está filtrado).
        _resumo.Children.Clear();
        _resumo.Children.Add(Numero(filtrados.Count.ToString(), filtrados.Count == 1 ? "Cubo" : "Cubos"));
        _resumo.Children.Add(Numero(filtrados.Where(c => c.Tipo == TipoCubo.Organizado && !c.Restaurado).Sum(c => c.Arquivos).ToString(), "arquivos organizados"));
        var liberados = filtrados.Where(c => c.Tipo != TipoCubo.Organizado && !c.Restaurado && !c.EnvioAdiado).Sum(c => c.Bytes);
        _resumo.Children.Add(Numero(FormatarBytes.Formatar(liberados), "de espaço liberado"));
        var porTipo = filtrados.SelectMany(c => c.PorCategoria).GroupBy(p => p.Key).Select(g => (g.Key, g.Sum(p => p.Value)))
            .OrderByDescending(p => p.Item2).ToList();
        foreach (var (categoria, n) in porTipo)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            linha.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = Ui.CorDaCategoria(categoria), VerticalAlignment = VerticalAlignment.Center });
            linha.Children.Add(Ui.Texto(Cubo.NomeDaCategoria(categoria, n), "secondary"));
            _resumo.Children.Add(linha);
        }

        // Cubos por data.
        _lista.Children.Clear();
        if (filtrados.Count == 0)
            _lista.Children.Add(Ui.Texto(todos.Count == 0
                ? "Ainda não há Cubos. Eles aparecem depois da primeira organização ou limpeza."
                : "Nenhum Cubo com esses filtros.", "secondary"));
        foreach (var dia in filtrados.GroupBy(c => c.Data.Date))
        {
            var grupo = new StackPanel { Spacing = 10 };
            grupo.Children.Add(Ui.Texto(NomeDoDia(dia.Key).ToUpperInvariant(), "caps"));
            var cartoes = new WrapPanel();
            foreach (var cubo in dia)
            {
                var cartao = CartaoCubo.Criar(cubo, c =>
                {
                    _selecionado = c.Id;
                    Atualizar();
                }, 210);
                cartao.Margin = new Thickness(0, 0, 12, 12);
                cartao.Classes.Set("selecionado", cubo.Id == _selecionado);
                cartoes.Children.Add(cartao);
            }
            grupo.Children.Add(cartoes);
            _lista.Children.Add(grupo);
        }

        var selecionado = _selecionado is null ? null : todos.FirstOrDefault(c => c.Id == _selecionado);
        _detalhe.Content = selecionado is null ? null : Detalhe(selecionado);
    }

    private Control Detalhe(Cubo cubo)
    {
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,Auto") };

        var textos = new StackPanel { Spacing = 6 };
        var titulo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        titulo.Children.Add(Ui.IconeDeBloco(CartaoCubo.IconeDoTipo(cubo), 44));
        var nomes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nomes.Children.Add(Ui.Texto(cubo.Titulo, "h3"));
        nomes.Children.Add(Ui.Texto($"{cubo.Data:dd/MM/yyyy 'às' HH:mm} · {(cubo.Modo == "automatico" ? "automática" : "pedida por você")}", "pequeno"));
        titulo.Children.Add(nomes);
        textos.Children.Add(titulo);
        textos.Children.Add(Ui.Texto($"{Ui.Plural(cubo.Arquivos, "arquivo", "arquivos")} · {FormatarBytes.Formatar(cubo.Bytes)}" +
                                     (cubo.PastasDeOrigem.Count > 0 ? $" · de {string.Join(", ", cubo.PastasDeOrigem)}" : ""), "secondary"));
        textos.Children.Add(CartaoCubo.BarraDeTipos(cubo));
        textos.Children.Add(Ui.Texto(cubo.ResumoPorTipo, "secondary"));
        if (cubo.Falhas > 0) textos.Children.Add(Ui.Texto($"{cubo.Falhas} arquivo(s) não puderam ser mexidos (veja o relatório).", "perigo"));
        if (cubo.Restaurado && cubo.RestauradoEm is { } quando) textos.Children.Add(Ui.Texto($"Restaurado em {quando:dd/MM/yyyy 'às' HH:mm}.", "sucesso"));
        if (cubo.EnvioAdiado) textos.Children.Add(Ui.Texto($"Envio adiado: {cubo.MotivoAdiamento}. Os arquivos continuam no computador.", "secondary"));
        if (cubo.Tipo == TipoCubo.ApagadoDeVez) textos.Children.Add(Ui.Texto("Arquivos apagados de vez não podem ser restaurados.", "pequeno"));
        grade.Children.Add(textos);

        var acoes = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top, MinWidth = 180 };
        if (cubo.PodeRestaurar)
        {
            var restaurar = Ui.Botao("Restaurar", "primary", "IconeDesfazer", async (_, _) =>
            {
                var resposta = Dialogo.Show(
                    cubo.Tipo == TipoCubo.Organizado
                        ? $"Devolver os {cubo.Arquivos} arquivo(s) desta organização para onde estavam?"
                        : $"Trazer de volta os {cubo.Arquivos} arquivo(s) deste Cubo para onde estavam?",
                    "Restaurar", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes,
                    textoConfirmar: "Restaurar", textoCancelar: "Cancelar");
                if (resposta == MessageBoxResult.Yes) await _servicos.RestaurarAsync(cubo);
                Atualizar();
            });
            restaurar.HorizontalAlignment = HorizontalAlignment.Stretch;
            acoes.Children.Add(restaurar);
        }
        if (_servicos.AcaoExtraDoCubo(cubo) is { } extra) acoes.Children.Add(extra);
        if (!string.IsNullOrEmpty(cubo.CaminhoRelatorio) && File.Exists(cubo.CaminhoRelatorio))
        {
            var relatorio = Ui.Botao("Abrir relatório", "secondary", "IconeDocumento", (_, _) => Ui.Abrir(cubo.CaminhoRelatorio!));
            relatorio.HorizontalAlignment = HorizontalAlignment.Stretch;
            acoes.Children.Add(relatorio);
        }
        if (!string.IsNullOrEmpty(cubo.CaminhoRegistro) && File.Exists(cubo.CaminhoRegistro))
        {
            var registro = Ui.Botao("Ver registro (JSON)", "ghost", "IconeCodigo", (_, _) => Ui.Abrir(Path.GetDirectoryName(cubo.CaminhoRegistro!)!));
            registro.HorizontalAlignment = HorizontalAlignment.Stretch;
            acoes.Children.Add(registro);
        }
        Grid.SetColumn(acoes, 2);
        grade.Children.Add(acoes);

        var cartao = new Border { Child = grade };
        cartao.Classes.Add("card");
        return cartao;
    }

    private static Control Chip(string texto, string grupo, bool marcado, Action aoMarcar)
    {
        var chip = new RadioButton { Content = texto, GroupName = grupo, IsChecked = marcado, HorizontalAlignment = HorizontalAlignment.Left };
        chip.Classes.Add("chip");
        chip.IsCheckedChanged += (_, _) => { if (chip.IsChecked == true) aoMarcar(); };
        return chip;
    }

    private static Control Numero(string valor, string rotulo)
    {
        var pilha = new StackPanel();
        pilha.Children.Add(new TextBlock { Text = valor, FontSize = 22, FontWeight = FontWeight.Bold });
        pilha.Children.Add(Ui.Texto(rotulo, "pequeno"));
        var borda = new Border { Child = pilha };
        borda.Classes.Add("stat");
        return borda;
    }

    private static Control Espaco(double altura) => new Border { Height = altura };

    private static string NomeDoDia(DateTime dia)
    {
        if (dia == DateTime.Today) return "Hoje";
        if (dia == DateTime.Today.AddDays(-1)) return "Ontem";
        return dia.ToString("dd/MM/yyyy");
    }
}
