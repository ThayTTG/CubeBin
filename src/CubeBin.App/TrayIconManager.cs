using System.Windows.Forms;
using CorDesenho = System.Drawing.Color;

namespace CubeBin.App;

/// <summary>
/// Ícone de bandeja do sistema — mantém o app residente em segundo plano (seção 7 da spec).
/// O menu do ícone é do WinForms (a bandeja do Windows não aceita menu WPF), então ele recebe as
/// cores do guia da NokyLabs por um desenhista próprio (<see cref="DesenhistaMenuNokyLabs"/>),
/// lidas de Theme/Colors.xaml. Cantos arredondados não são possíveis nesse tipo de menu.
/// </summary>
public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconManager(Action abrirConfiguracoes, Action executarAgora, Action sair)
    {
        var cores = new TabelaCoresNokyLabs();
        var menu = new ContextMenuStrip
        {
            Renderer = new DesenhistaMenuNokyLabs(cores),
            BackColor = cores.Fundo,
            ForeColor = cores.Texto,
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Segoe UI", 9.75f),
            Padding = new Padding(4)
        };
        menu.Items.Add("Abrir o CubeBin", null, (_, _) => abrirConfiguracoes());
        menu.Items.Add("Organizar agora", null, (_, _) => executarAgora());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => sair());

        foreach (ToolStripItem item in menu.Items)
            item.Padding = new Padding(6, 4, 6, 4);

        _notifyIcon = new NotifyIcon
        {
            Icon = ObterIconeDoApp(),
            Visible = true,
            Text = "CubeBin",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => abrirConfiguracoes();
    }

    /// <summary>
    /// Extrai o ícone embutido no próprio .exe em execução (o mesmo definido via ApplicationIcon
    /// no .csproj) — evita precisar copiar o .ico como arquivo solto junto do executável. Cai no
    /// ícone padrão do Windows se a extração falhar por qualquer motivo (ex.: rodando via
    /// "dotnet run", onde o executável real é o dotnet.exe, sem o ícone do app embutido).
    /// </summary>
    private static System.Drawing.Icon ObterIconeDoApp()
    {
        try
        {
            var caminhoExecutavel = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(caminhoExecutavel))
            {
                var icone = System.Drawing.Icon.ExtractAssociatedIcon(caminhoExecutavel);
                if (icone is not null) return icone;
            }
        }
        catch (Exception)
        {
            // Extração falhou — segue para o ícone padrão do sistema abaixo.
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    /// <summary>
    /// Cores do menu da bandeja, lidas de Theme/Colors.xaml. O WinForms não tem transparência
    /// por cima de outra cor no menu, então o "AccentSoft" do hover é misturado à mão sobre o fundo.
    /// </summary>
    private sealed class TabelaCoresNokyLabs : ProfessionalColorTable
    {
        public CorDesenho Fundo { get; } = Ler("SuperficieElevadaColor", CorDesenho.FromArgb(0x2A, 0x21, 0x49));
        public CorDesenho Borda { get; } = Ler("BordaColor", CorDesenho.FromArgb(0x2C, 0x23, 0x47));
        public CorDesenho Texto { get; } = Ler("TextoColor", CorDesenho.FromArgb(0xF2, 0xEE, 0xFF));
        public CorDesenho Hover { get; }

        public TabelaCoresNokyLabs()
        {
            Hover = Misturar(Ler("AccentSoftColor", CorDesenho.FromArgb(0x29, 0xC4, 0x36, 0x2E)), Fundo);
            UseSystemColors = false;
        }

        public override CorDesenho ToolStripDropDownBackground => Fundo;
        public override CorDesenho MenuBorder => Borda;
        public override CorDesenho MenuItemBorder => Hover;
        public override CorDesenho MenuItemSelected => Hover;
        public override CorDesenho MenuItemSelectedGradientBegin => Hover;
        public override CorDesenho MenuItemSelectedGradientEnd => Hover;
        public override CorDesenho ImageMarginGradientBegin => Fundo;
        public override CorDesenho ImageMarginGradientMiddle => Fundo;
        public override CorDesenho ImageMarginGradientEnd => Fundo;
        public override CorDesenho SeparatorDark => Borda;
        public override CorDesenho SeparatorLight => Borda;

        /// <summary>Lê uma cor de Theme/Colors.xaml; a reserva (valor do guia) só entra se a chave sumir.</summary>
        private static CorDesenho Ler(string chave, CorDesenho reserva)
        {
            if (System.Windows.Application.Current?.TryFindResource(chave) is System.Windows.Media.Color cor)
                return CorDesenho.FromArgb(cor.A, cor.R, cor.G, cor.B);
            return reserva;
        }

        /// <summary>Aplica uma cor com transparência por cima de um fundo sólido.</summary>
        private static CorDesenho Misturar(CorDesenho frente, CorDesenho fundo)
        {
            var a = frente.A / 255.0;
            return CorDesenho.FromArgb(
                (int)Math.Round(frente.R * a + fundo.R * (1 - a)),
                (int)Math.Round(frente.G * a + fundo.G * (1 - a)),
                (int)Math.Round(frente.B * a + fundo.B * (1 - a)));
        }
    }

    /// <summary>Desenhista que mantém o texto na cor do tema (o padrão escurece ao passar o mouse).</summary>
    private sealed class DesenhistaMenuNokyLabs : ToolStripProfessionalRenderer
    {
        private readonly TabelaCoresNokyLabs _cores;

        public DesenhistaMenuNokyLabs(TabelaCoresNokyLabs cores) : base(cores)
        {
            _cores = cores;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _cores.Texto;
            base.OnRenderItemText(e);
        }
    }
}
