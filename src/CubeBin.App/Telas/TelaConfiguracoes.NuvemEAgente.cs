using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CubeBin.App.Interface;
using CubeBin.Core.Nuvem;
using CubeBin.Core.Plataforma;

namespace CubeBin.App.Telas;

/// <summary>As seções Nuvem e Agente das Configurações (versão 2.0).</summary>
public partial class TelaConfiguracoes
{
    // ===================== Nuvem =====================

    private Control SecaoNuvem()
    {
        var pilha = new StackPanel { Spacing = 14 };
        var config = _servicos.Config.Carregar();

        if (_servicos.NuvemDeTeste is not null)
        {
            pilha.Children.Add(Ui.Aviso("Modo de teste: a \"nuvem\" é uma pasta do computador (variável CUBEBIN_NUVEM_DE_TESTE). " +
                                        "O fluxo é o mesmo do Google Drive: pacote, conferência, relatórios e índice.", "", "IconeInfo"));
        }
        else if (!GoogleDriveNuvem.CredenciaisDoAppConfiguradas)
        {
            pilha.Children.Add(Ui.Aviso(
                "Este CubeBin foi compilado sem as credenciais do Google Drive, então a nuvem fica desligada. " +
                "Quem desenvolve o app cadastra o ID do cliente OAuth uma vez (README, \"Configurar a integração com o Google Drive\") e todo mundo só precisa clicar em Conectar.",
                "warning", "IconeAlerta"));
        }

        var status = new StackPanel { Spacing = 6 };
        var conectada = _servicos.NuvemConectada;
        status.Children.Add(new TextBlock
        {
            Text = _servicos.NuvemDeTeste is not null ? "Conectada à pasta de teste" : conectada ? $"Conectada: {config.Nuvem.ContaConectada ?? "conta do Google"}" : "Nenhuma conta conectada",
            FontWeight = FontWeight.SemiBold,
        });
        var espaco = Ui.Texto(conectada ? "Consultando o espaço…" : "Conecte a sua conta do Google Drive para guardar os Cubos na nuvem.", "secondary");
        status.Children.Add(espaco);
        var barra = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
        barra.Classes.Add("slim");
        status.Children.Add(barra);

        Button botao;
        if (conectada && _servicos.NuvemDeTeste is not null)
        {
            botao = Ui.Botao("Abrir a pasta de teste", "secondary", "IconePasta", (_, _) => Ui.Abrir(Environment.GetEnvironmentVariable("CUBEBIN_NUVEM_DE_TESTE")!));
            _ = PreencherEspacoAsync(espaco, barra);
        }
        else if (conectada)
        {
            botao = Ui.Botao("Desconectar", "danger", "IconeX", async (_, _) =>
            {
                var resposta = Dialogo.Show("Desconectar a conta do Google Drive? Os Cubos que já estão na nuvem continuam lá.",
                    "Nuvem", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, textoConfirmar: "Desconectar", textoCancelar: "Cancelar");
                if (resposta != MessageBoxResult.Yes) return;
                await _servicos.DesconectarNuvemAsync();
                MostrarSecao("nuvem");
            });
            _ = PreencherEspacoAsync(espaco, barra);
        }
        else
        {
            botao = Ui.Botao("Conectar conta", "primary", "IconeNuvem", async (_, _) =>
            {
                try
                {
                    var email = await _servicos.ConectarNuvemAsync();
                    Dialogo.Show($"Pronto! Conectado como {email}.", "Nuvem");
                }
                catch (Exception ex)
                {
                    Dialogo.Show($"Não foi possível conectar: {EnvioNuvem.ExplicarErro(ex)}.", "Nuvem", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                MostrarSecao("nuvem");
            });
            botao.IsEnabled = GoogleDriveNuvem.CredenciaisDoAppConfiguradas;
        }
        pilha.Children.Add(Bloco("Google Drive", "O login abre no navegador, uma vez só. O CubeBin só enxerga o que ele mesmo criou.", botao));
        var cartaoStatus = new Border { Child = status };
        cartaoStatus.Classes.Add("bloco");
        pilha.Children.Add(cartaoStatus);

        var como = new StackPanel { Spacing = 8 };
        como.Children.Add(new TextBlock { Text = "Como fica no seu Drive", FontWeight = FontWeight.SemiBold });
        foreach (var (pasta, texto) in new[]
                 {
                     ("CubeBin/Cubos", "um pacote compactado por envio, com a data no nome"),
                     ("CubeBin/Relatórios", "o relatório e o registro de cada envio, para restaurar tudo no lugar certo"),
                     ("CubeBin/indice.json", "a lista dos Cubos, para a tela de Cubos mostrar o resumo sem baixar nada"),
                 })
        {
            var linha = new DockPanel();
            var icone = Ui.Icone(pasta.EndsWith(".json") ? "IconeDocumento" : "IconePasta", 16);
            icone.Margin = new Thickness(0, 2, 10, 0);
            icone.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(icone, Dock.Left);
            linha.Children.Add(icone);
            var frase = new TextBlock { TextWrapping = TextWrapping.Wrap };
            frase.Inlines!.Add(new Avalonia.Controls.Documents.Run(pasta) { FontWeight = FontWeight.SemiBold });
            frase.Inlines.Add(new Avalonia.Controls.Documents.Run(" — " + texto) { Foreground = Ui.Recurso("TextSecondaryBrush") as IBrush });
            linha.Children.Add(frase);
            como.Children.Add(linha);
        }
        como.Children.Add(Ui.Texto("Antes de apagar qualquer coisa do computador, o CubeBin confere se o pacote chegou inteiro (tamanho e código de conferência). " +
                                   "Sem internet, com o Drive cheio ou com a autorização vencida, o envio é adiado e nada sai do computador.", "pequeno"));
        var cartaoComo = new Border { Child = como };
        cartaoComo.Classes.Add("bloco");
        pilha.Children.Add(cartaoComo);
        return pilha;
    }

    private async Task PreencherEspacoAsync(TextBlock texto, ProgressBar barra)
    {
        try
        {
            var conta = await _servicos.Nuvem.ContaAsync();
            if (conta.TotalBytes is { } total && total > 0)
            {
                texto.Text = $"{FormatarBytes.Formatar(conta.UsadoBytes)} usados de {FormatarBytes.Formatar(total)} " +
                             $"({FormatarBytes.Formatar(total - conta.UsadoBytes)} livres)";
                barra.Value = Math.Clamp((double)conta.UsadoBytes / total, 0, 1);
                barra.IsVisible = true;
            }
            else texto.Text = $"{FormatarBytes.Formatar(conta.UsadoBytes)} usados.";
        }
        catch (Exception ex)
        {
            texto.Text = $"Não foi possível consultar o Drive agora: {EnvioNuvem.ExplicarErro(ex)}.";
        }
    }

    // ===================== Agente =====================

    private Control SecaoAgente()
    {
        var a = _servicos.Config.Carregar().Agente;
        var pilha = new StackPanel { Spacing = 14 };

        var ligar = new ToggleSwitch { IsChecked = a.Ligado, OnContent = "Ligado", OffContent = "Desligado" };
        ligar.IsCheckedChanged += (_, _) => Salvar(c => c.Agente.Ligado = ligar.IsChecked == true);
        pilha.Children.Add(Bloco("Agente de sugestões",
            "Como um antivírus: quando você está longe do computador, ele procura o que talvez você não precise (no máximo uma vez por dia). " +
            "Ele só sugere — quem tira os arquivos é você, pelo Liberar Espaço. Tudo acontece no computador, sem internet.",
            ligar));

        pilha.Children.Add(Bloco("Temporários esquecidos",
            "Arquivos .tmp, .bak, .old, .log… parados há mais de:",
            Numero(a.DiasTemporarioParado, 1, 3650, "dias", v => Salvar(c => c.Agente.DiasTemporarioParado = v))));
        pilha.Children.Add(Bloco("Arquivos grandes parados",
            "A partir deste tamanho…",
            Numero(a.TamanhoMinimoGrandeMb, 10, 1_000_000, "MB", v => Salvar(c => c.Agente.TamanhoMinimoGrandeMb = v))));
        pilha.Children.Add(Bloco("…e sem uso há mais de",
            "Contando a última vez que o arquivo foi aberto ou alterado.",
            Numero(a.DiasArquivoGrandeParado, 1, 3650, "dias", v => Salvar(c => c.Agente.DiasArquivoGrandeParado = v))));

        var resultado = Ui.Texto(a.UltimaAnalise is { } d ? $"Última análise: {CartaoCubo.DataAmigavel(d)}." : "Ainda não analisou.", "pequeno");
        Button? analisar = null;
        analisar = Ui.Botao("Analisar agora", "soft", "IconeAgente", async (_, _) =>
        {
            analisar!.IsEnabled = false;
            analisar.Content = "Analisando…";
            try
            {
                var r = await _servicos.AnalisarAgoraAsync();
                resultado.Text = r.Sugestoes.Count == 0
                    ? "Nada para sugerir agora."
                    : $"{r.Sugestoes.Count} sugestão(ões), {FormatarBytes.Formatar(r.Bytes)}. Veja em Liberar Espaço.";
            }
            finally
            {
                analisar.IsEnabled = true;
                analisar.Content = "Analisar agora";
            }
        });
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        linha.Children.Add(analisar);
        linha.Children.Add(resultado);
        resultado.VerticalAlignment = VerticalAlignment.Center;
        pilha.Children.Add(linha);
        pilha.Children.Add(Ui.Aviso("Nunca entram nas sugestões: pastas de projeto, atalhos, programas, arquivos ocultos ou abertos, e nada fora das pastas do CubeBin.", "", "IconeEscudo"));
        return pilha;
    }
}
