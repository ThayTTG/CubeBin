using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CubeBin.App.Interface;
using CubeBin.App.Servicos;
using CubeBin.Core.Configuracao;

namespace CubeBin.App.Telas;

/// <summary>
/// Configurações (PRD 5.6): duas colunas — o menu com as seis seções à esquerda (Pastas, Regras,
/// Automação, Nuvem, Agente, Proteções) e o conteúdo à direita. Cada mudança salva sozinha.
/// </summary>
public class TelaConfiguracoes : UserControl, ITela
{
    public static readonly (string Id, string Nome, string Icone, string Descricao)[] Secoes =
    {
        ("pastas", "Pastas", "IconePasta", "Quais pastas o CubeBin organiza e para onde os arquivos vão."),
        ("regras", "Regras", "IconeOrganizar", "Arraste cada tipo de arquivo até a pasta onde ele deve ficar."),
        ("automacao", "Automação", "IconeRaio", "Organizar sozinho quando você estiver longe do computador."),
        ("nuvem", "Nuvem", "IconeNuvem", "A conta do Google Drive que guarda os Cubos."),
        ("agente", "Agente", "IconeAgente", "Sugestões de limpeza em segundo plano. Ele nunca age sozinho."),
        ("protecoes", "Proteções", "IconeEscudo", "O que o CubeBin nunca mexe."),
    };

    /// <summary>Seções preenchidas por outras partes do app (a nuvem e o agente, na versão 2.0).</summary>
    public static readonly Dictionary<string, Func<ServicosApp, TelaConfiguracoes, Control>> SecoesExtras = new();

    private readonly ServicosApp _servicos;
    private readonly MainWindow _janela;
    private readonly StackPanel _menu = new() { Spacing = 3 };
    private readonly ContentControl _conteudo = new();
    private readonly TextBlock _titulo = Ui.Texto("", "h2");
    private readonly TextBlock _descricao = Ui.Texto("", "secondary");
    private readonly TextBlock _salvo = Ui.Texto("", "pequeno");
    private string _secao = "pastas";
    private bool _preenchendo;
    private PainelRegras? _regras;

    public TelaConfiguracoes(ServicosApp servicos, MainWindow janela)
    {
        _servicos = servicos;
        _janela = janela;

        foreach (var (id, nome, icone, _) in Secoes)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            linha.Children.Add(Ui.Icone(icone, 18));
            linha.Children.Add(new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center });
            var botao = new RadioButton { Content = linha, GroupName = "secoesConfig", Tag = id };
            botao.Classes.Add("nav");
            botao.IsCheckedChanged += (_, _) =>
            {
                if (botao.IsChecked == true && !_preenchendo) MostrarSecao(id);
            };
            _menu.Children.Add(botao);
        }

        var editarJson = Ui.Botao("Abrir o arquivo de configurações", "link", null, (_, _) => AbrirJson());
        editarJson.Margin = new Thickness(6, 16, 0, 0);
        var recarregar = Ui.Botao("Recarregar depois de editar", "link", null, (_, _) =>
        {
            _servicos.Config.Recarregar();
            MostrarSecao(_secao);
        });
        recarregar.Margin = new Thickness(6, 0, 0, 0);

        var colunaMenu = new StackPanel { Width = 210, Spacing = 4 };
        colunaMenu.Children.Add(Ui.Texto("SEÇÕES", "caps"));
        colunaMenu.Children.Add(_menu);
        colunaMenu.Children.Add(editarJson);
        colunaMenu.Children.Add(recarregar);
        colunaMenu.Children.Add(Ui.Texto("Avançado: o arquivo é um JSON legível. Depois de editar, clique em Recarregar.", "pequeno"));

        var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        _salvo.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(_salvo, Dock.Right);
        cabecalho.Children.Add(_salvo);
        var titulos = new StackPanel { Spacing = 4 };
        titulos.Children.Add(_titulo);
        titulos.Children.Add(_descricao);
        cabecalho.Children.Add(titulos);

        var colunaConteudo = new DockPanel();
        DockPanel.SetDock(cabecalho, Dock.Top);
        colunaConteudo.Children.Add(cabecalho);
        colunaConteudo.Children.Add(_conteudo);

        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,28,*") };
        grade.Children.Add(colunaMenu);
        Grid.SetColumn(colunaConteudo, 2);
        grade.Children.Add(colunaConteudo);

        var pagina = new DockPanel { Margin = new Thickness(32, 28, 32, 24) };
        var topo = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        topo.Children.Add(Ui.Texto("Configurações", "titulo"));
        topo.Children.Add(Ui.Texto("Cada mudança é salva na hora.", "descricao"));
        DockPanel.SetDock(topo, Dock.Top);
        pagina.Children.Add(topo);
        pagina.Children.Add(grade);
        Content = pagina;
    }

    public void AoEntrar(object? parametro) =>
        MostrarSecao(parametro as string ?? _secao);

    public void MostrarSecao(string id)
    {
        if (Secoes.All(s => s.Id != id)) id = "pastas";
        _secao = id;
        _preenchendo = true;
        foreach (var botao in _menu.Children.OfType<RadioButton>()) botao.IsChecked = Equals(botao.Tag, id);
        _preenchendo = false;

        var secao = Secoes.First(s => s.Id == id);
        _titulo.Text = secao.Nome;
        _descricao.Text = secao.Descricao;
        _salvo.Text = "";

        _conteudo.Content = id switch
        {
            "pastas" => Rolar(SecaoPastas()),
            "regras" => _regras = new PainelRegras(_servicos),
            "automacao" => Rolar(SecaoAutomacao()),
            "protecoes" => Rolar(SecaoProtecoes()),
            _ when SecoesExtras.TryGetValue(id, out var criar) => Rolar(criar(_servicos, this)),
            _ => Rolar(Ui.Aviso("Chega na versão 2.0.", "", "IconeRelogio")),
        };
        if (id == "regras") _regras?.Atualizar();
    }

    private static Control Rolar(Control c) => new ScrollViewer { Content = c };

    /// <summary>Salva, avisa "Salvo" por alguns segundos e reaplica o que depende da configuração.</summary>
    public void Salvar(Action<Configuracoes> alteracao)
    {
        _servicos.Config.Alterar(alteracao);
        _salvo.Text = "✓ Salvo";
        DispatcherTimer.RunOnce(() => _salvo.Text = "", TimeSpan.FromSeconds(2.5));
    }

    // ===================== Pastas =====================

    private Control SecaoPastas()
    {
        var config = _servicos.Config.Carregar();
        var pilha = new StackPanel { Spacing = 10 };

        pilha.Children.Add(Ui.Texto("PASTAS PADRÃO", "caps"));
        pilha.Children.Add(Ui.Texto("As pastas do sistema. Podem ser organizadas e esvaziadas, mas nunca removidas.", "pequeno"));
        foreach (var pasta in config.Pastas.Where(p => p.EhPadrao))
            pilha.Children.Add(LinhaPasta(pasta));

        var cabecalho = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var adicionar = Ui.Botao("Adicionar pasta", "soft", "IconePastaMais", async (_, _) => await AdicionarPastaAsync());
        DockPanel.SetDock(adicionar, Dock.Right);
        cabecalho.Children.Add(adicionar);
        cabecalho.Children.Add(Ui.Texto("PASTAS PERSONALIZADAS", "caps"));
        pilha.Children.Add(cabecalho);

        var personalizadas = config.Pastas.Where(p => !p.EhPadrao).ToList();
        if (personalizadas.Count == 0)
            pilha.Children.Add(Ui.Texto("Nenhuma ainda. Adicione uma pasta para o CubeBin organizar, ou crie uma arrastando um tipo para \"Nova pasta\" em Regras.", "secondary"));
        foreach (var pasta in personalizadas)
            pilha.Children.Add(LinhaPasta(pasta));
        return pilha;
    }

    private Control LinhaPasta(PastaConfig pasta)
    {
        var caminho = pasta.CaminhoEfetivo();
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var icone = Ui.IconeDeBloco(Ui.IconeDaPasta(pasta), 38);
        icone.Margin = new Thickness(0, 0, 14, 0);
        icone.VerticalAlignment = VerticalAlignment.Top;
        grade.Children.Add(icone);

        var textos = new StackPanel { Spacing = 2 };
        textos.Children.Add(new TextBlock { Text = pasta.Nome, FontWeight = FontWeight.SemiBold });
        textos.Children.Add(new TextBlock { Text = caminho, FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!Directory.Exists(caminho)) textos.Children.Add(Ui.Texto("Ainda não existe: será criada quando precisar.", "pequeno"));

        var organizar = new CheckBox { Content = "Organizar os arquivos que caem aqui", IsChecked = pasta.Monitorar, Margin = new Thickness(0, 6, 0, 0) };
        var subpastas = new CheckBox { Content = "Incluir as subpastas", IsChecked = pasta.OrganizarSubpastas, IsEnabled = pasta.Monitorar };
        organizar.IsCheckedChanged += (_, _) =>
        {
            var ligado = organizar.IsChecked == true;
            Salvar(c => c.ObterPasta(pasta.Id)!.Monitorar = ligado);
            subpastas.IsEnabled = ligado;
        };
        subpastas.IsCheckedChanged += (_, _) =>
        {
            var ligado = subpastas.IsChecked == true;
            if (ligado && pasta.OrganizarSubpastas != true)
            {
                // PRD 5.1: subpastas são opcionais e vêm com aviso de risco de desorganização.
                var resposta = Dialogo.Show(
                    $"Organizar as subpastas de {pasta.Nome} tira os arquivos de dentro delas e leva para as pastas por tipo.\n\n" +
                    "Se você separou arquivos em subpastas de propósito (por matéria, por projeto…), essa separação vai se perder. " +
                    "Pastas de projeto continuam protegidas, e dá para desfazer cada organização.",
                    "Incluir subpastas?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
                    textoConfirmar: "Incluir subpastas", textoCancelar: "Deixar como está");
                if (resposta != MessageBoxResult.Yes)
                {
                    subpastas.IsChecked = false;
                    return;
                }
            }
            Salvar(c => c.ObterPasta(pasta.Id)!.OrganizarSubpastas = ligado);
        };
        textos.Children.Add(organizar);
        textos.Children.Add(subpastas);
        Grid.SetColumn(textos, 1);
        grade.Children.Add(textos);

        var acoes = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        var abrir = Ui.Botao("Abrir", "ghost", "IconeLinkExterno", (_, _) => Ui.Abrir(caminho));
        acoes.Children.Add(abrir);
        if (pasta.PodeRemover)
        {
            var remover = Ui.Botao("Remover", "danger", "IconeX", (_, _) =>
            {
                var resposta = Dialogo.Show(
                    $"Tirar \"{pasta.Nome}\" da lista do CubeBin?\n\nA pasta e os arquivos dela continuam no computador; só deixam de ser organizados. " +
                    "Os tipos que iam para ela voltam para \"sem pasta\".",
                    "Remover pasta", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                    textoConfirmar: "Remover da lista", textoCancelar: "Cancelar");
                if (resposta != MessageBoxResult.Yes) return;
                Salvar(c => ConfiguracaoService.RemoverPastaPersonalizada(c, pasta.Id));
                MostrarSecao("pastas");
            });
            acoes.Children.Add(remover);
        }
        Grid.SetColumn(acoes, 2);
        grade.Children.Add(acoes);

        var bloco = new Border { Child = grade };
        bloco.Classes.Add("bloco");
        return bloco;
    }

    private async Task AdicionarPastaAsync()
    {
        var topo = TopLevel.GetTopLevel(this);
        if (topo is null) return;
        var escolhidas = await topo.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Escolher uma pasta para o CubeBin organizar" });
        var caminho = escolhidas.FirstOrDefault()?.TryGetLocalPath();
        if (caminho is null) return;

        var problema = ConfiguracaoService.ValidarPastaPersonalizada(_servicos.Config.Carregar(), caminho);
        if (problema is not null)
        {
            Dialogo.Show(problema, "Adicionar pasta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Salvar(c => ConfiguracaoService.CriarPastaPersonalizada(c, Path.GetFileName(caminho.TrimEnd(Path.DirectorySeparatorChar)), caminho, monitorar: true));
        MostrarSecao("pastas");
    }

    // ===================== Automação =====================

    private Control SecaoAutomacao()
    {
        var a = _servicos.Config.Carregar().Automacao;
        var pilha = new StackPanel { Spacing = 14 };

        var ligar = new ToggleSwitch { IsChecked = a.Ligada, OnContent = "Ligada", OffContent = "Desligada" };
        ligar.IsCheckedChanged += (_, _) => Salvar(c => c.Automacao.Ligada = ligar.IsChecked == true);
        pilha.Children.Add(Bloco("Organização automática",
            "Quando você ficar um tempo longe do computador, o CubeBin avisa e organiza. Sem prévia, mas sempre com tempo para cancelar e com Desfazer depois.",
            ligar));

        pilha.Children.Add(Bloco("Tempo longe do computador",
            "Quantos minutos sem mexer no mouse ou no teclado antes de organizar.",
            Numero(a.MinutosDeAusencia, 1, 240, "min", v => Salvar(c => c.Automacao.MinutosDeAusencia = v))));

        pilha.Children.Add(Bloco("Tempo para cancelar",
            "Quanto tempo o aviso fica na tela antes de começar.",
            Numero(a.SegundosParaCancelar, 10, 600, "s", v => Salvar(c => c.Automacao.SegundosParaCancelar = v))));

        pilha.Children.Add(Bloco("Intervalo entre organizações",
            "Mesmo longe do computador várias vezes, o CubeBin organiza no máximo uma vez nesse intervalo.",
            Numero(a.HorasEntreExecucoes, 1, 168, "h", v => Salvar(c => c.Automacao.HorasEntreExecucoes = v))));

        var iniciar = new ToggleSwitch { IsChecked = a.IniciarComOSistema, OnContent = "Sim", OffContent = "Não" };
        iniciar.IsCheckedChanged += (_, _) => Salvar(c => c.Automacao.IniciarComOSistema = iniciar.IsChecked == true);
        pilha.Children.Add(Bloco("Abrir junto com o sistema",
            "Para a organização automática funcionar, o CubeBin precisa estar aberto (fica perto do relógio).",
            iniciar));

        var ultima = a.UltimaExecucaoAutomatica is { } d ? CartaoCubo.DataAmigavel(d) : "nunca";
        pilha.Children.Add(Ui.Texto($"Última organização automática: {ultima}. Feitas: {a.ExecucoesAutomaticas} · canceladas no aviso: {a.ExecucoesCanceladas}.", "pequeno"));
        return pilha;
    }

    // ===================== Proteções =====================

    private Control SecaoProtecoes()
    {
        var p = _servicos.Config.Carregar().Protecoes;
        var pilha = new StackPanel { Spacing = 14 };

        pilha.Children.Add(Bloco("Pastas de projeto",
            "Uma pasta com qualquer um destes marcadores é um projeto, e o CubeBin nunca mexe dentro dela. " +
            "\"*.sln\" = um arquivo com essa extensão; \".git/\" = uma subpasta; \"Assets/+ProjectSettings/\" = as duas juntas (Unity).",
            ListaEditavel(p.MarcadoresDeProjeto, "Novo marcador (ex.: *.blend)", (c, lista) => c.Protecoes.MarcadoresDeProjeto = lista)));

        pilha.Children.Add(Bloco("Atalhos",
            "Nunca são movidos: a pessoa quer os atalhos onde eles estão.",
            ListaEditavel(p.ExtensoesDeAtalho, "Nova extensão", (c, lista) => c.Protecoes.ExtensoesDeAtalho = lista)));

        pilha.Children.Add(Bloco("Programas e instaladores",
            "Nunca são movidos: podem ser programas portáteis que só funcionam no lugar onde estão.",
            ListaEditavel(p.ExtensoesExecutaveis, "Nova extensão (ex.: .apk)", (c, lista) => c.Protecoes.ExtensoesExecutaveis = lista)));

        pilha.Children.Add(Bloco("Downloads em andamento e temporários",
            "Arquivos com estas extensões ficam onde estão.",
            ListaEditavel(p.ExtensoesDeDownloadIncompleto, "Nova extensão", (c, lista) => c.Protecoes.ExtensoesDeDownloadIncompleto = lista)));

        pilha.Children.Add(Bloco("Arquivos alterados há pouco",
            "Arquivos mexidos há menos que isso ficam para a próxima vez (podem estar sendo usados ou baixados).",
            Numero(p.MinutosSemAlteracao, 0, 1440, "min", v => Salvar(c => c.Protecoes.MinutosSemAlteracao = v))));

        pilha.Children.Add(Bloco("Pastas sincronizadas com o OneDrive",
            "A partir de quantos arquivos o CubeBin pede uma confirmação extra antes de mover (e deixa de fora do modo automático).",
            Numero(p.LimiteAvisoOneDriveArquivos, 1, 100000, "arquivos", v => Salvar(c => c.Protecoes.LimiteAvisoOneDriveArquivos = v))));

        pilha.Children.Add(Ui.Aviso("Sempre valem, sem como desligar: arquivos abertos em outro programa não são mexidos, " +
                                    "nada é sobrescrito (nomes repetidos ganham o prefixo da pasta de origem) e pastas padrão nunca são apagadas.",
            "", "IconeEscudo"));
        return pilha;
    }

    // ===================== Peças =====================

    public static Control Bloco(string titulo, string descricao, Control controle)
    {
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto") };
        var textos = new StackPanel { Spacing = 3 };
        textos.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeight.SemiBold });
        textos.Children.Add(Ui.Texto(descricao, "secondary"));
        grade.Children.Add(textos);
        if (controle is WrapPanel or StackPanel { Orientation: Orientation.Vertical })
        {
            // Listas ocupam a linha de baixo, inteira.
            var pilha = new StackPanel { Spacing = 10 };
            pilha.Children.Add(grade);
            pilha.Children.Add(controle);
            var b1 = new Border { Child = pilha };
            b1.Classes.Add("bloco");
            return b1;
        }
        controle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(controle, 2);
        grade.Children.Add(controle);
        var bloco = new Border { Child = grade };
        bloco.Classes.Add("bloco");
        return bloco;
    }

    private Control Numero(int valor, int minimo, int maximo, string unidade, Action<int> aoMudar)
    {
        var campo = new NumericUpDown { Value = valor, Minimum = minimo, Maximum = maximo, Increment = 1, FormatString = "0", Width = 130 };
        campo.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } v) aoMudar((int)Math.Clamp(v, minimo, maximo));
        };
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        linha.Children.Add(campo);
        linha.Children.Add(new TextBlock { Text = unidade, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 });
        return linha;
    }

    private Control ListaEditavel(List<string> itens, string dica, Action<Configuracoes, List<string>> aoMudar)
    {
        var lista = itens.ToList();
        var painel = new StackPanel { Spacing = 8, Orientation = Orientation.Vertical };
        var chips = new WrapPanel();

        void Redesenhar()
        {
            chips.Children.Clear();
            foreach (var item in lista.ToList())
            {
                var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                linha.Children.Add(new TextBlock { Text = item, FontWeight = FontWeight.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                var x = new Button { Content = Ui.Icone("IconeX", 10), Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
                ToolTip.SetTip(x, "Tirar da lista");
                x.Click += (_, _) =>
                {
                    lista.Remove(item);
                    Salvar(c => aoMudar(c, lista.ToList()));
                    Redesenhar();
                };
                linha.Children.Add(x);
                var chip = new Border { Child = linha, Cursor = null };
                chip.Classes.Add("ext");
                chips.Children.Add(chip);
            }
        }

        var novo = new TextBox { Watermark = dica, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        novo.Classes.Add("field");
        novo.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || string.IsNullOrWhiteSpace(novo.Text)) return;
            var texto = novo.Text.Trim();
            if (!lista.Contains(texto)) lista.Add(texto);
            novo.Text = "";
            Salvar(c => aoMudar(c, lista.ToList()));
            Redesenhar();
        };

        Redesenhar();
        painel.Children.Add(chips);
        painel.Children.Add(novo);
        return painel;
    }

    private void AbrirJson()
    {
        var caminho = _servicos.Config.Caminho;
        if (!Ui.Abrir(caminho))
            Dialogo.Show($"O arquivo de configurações está em:\n{caminho}", "Configurações");
    }
}
