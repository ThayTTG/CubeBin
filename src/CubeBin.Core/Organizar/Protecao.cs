using CubeBin.Core.Configuracao;

namespace CubeBin.Core.Organizar;

/// <summary>Por que um arquivo ficou onde estava.</summary>
public enum MotivoPulado
{
    Atalho,
    Executavel,
    DownloadIncompleto,
    AlteradoRecentemente,
    EmUso,
    OcultoOuDoSistema,
    OneDriveAguardandoConfirmacao,
}

/// <summary>
/// As proteções do PRD 6.3, num lugar só, usadas pela prévia, pela execução e pelo modo automático.
/// </summary>
public class Protecao
{
    private readonly Protecoes _config;
    private readonly IArquivosEmUso _emUso;
    private readonly Func<DateTime> _agora;

    public Protecao(Protecoes config, IArquivosEmUso? emUso = null, Func<DateTime>? agora = null)
    {
        _config = config;
        _emUso = emUso ?? ArquivosEmUso.DoSistema();
        _agora = agora ?? (() => DateTime.Now);
    }

    public static string Explicar(MotivoPulado motivo) => motivo switch
    {
        MotivoPulado.Atalho => "Atalho (atalhos nunca são movidos)",
        MotivoPulado.Executavel => "Programa ou instalador (nunca são movidos)",
        MotivoPulado.DownloadIncompleto => "Download em andamento ou arquivo temporário",
        MotivoPulado.AlteradoRecentemente => "Alterado há poucos minutos (fica para a próxima vez)",
        MotivoPulado.EmUso => "Aberto em outro programa",
        MotivoPulado.OcultoOuDoSistema => "Arquivo oculto ou do sistema",
        MotivoPulado.OneDriveAguardandoConfirmacao => "Pasta do OneDrive: aguardando sua confirmação",
        _ => motivo.ToString(),
    };

    /// <summary>Devolve o motivo para NÃO mover o arquivo, ou null se ele pode ser movido.</summary>
    public MotivoPulado? MotivoParaNaoMover(FileInfo arquivo)
    {
        var nome = arquivo.Name;
        var ext = Extensoes.DoArquivo(nome);

        if (EhOcultoOuDoSistema(arquivo)) return MotivoPulado.OcultoOuDoSistema;
        if (Contem(_config.ExtensoesDeAtalho, ext)) return MotivoPulado.Atalho;
        if (Contem(_config.ExtensoesExecutaveis, ext)) return MotivoPulado.Executavel;
        if (Contem(_config.ExtensoesDeDownloadIncompleto, ext) || nome.EndsWith('~') || nome.StartsWith("~$", StringComparison.Ordinal))
            return MotivoPulado.DownloadIncompleto;
        if (FoiAlteradoRecentemente(arquivo)) return MotivoPulado.AlteradoRecentemente;
        if (_emUso.EstaEmUso(arquivo.FullName)) return MotivoPulado.EmUso;
        return null;
    }

    public bool FoiAlteradoRecentemente(FileInfo arquivo)
    {
        if (_config.MinutosSemAlteracao <= 0) return false;
        var limite = _agora() - TimeSpan.FromMinutes(_config.MinutosSemAlteracao);
        var ultima = arquivo.LastWriteTime;
        // Um download recém-terminado pode manter a data de modificação original do servidor:
        // a data de criação (quando o sistema guarda) pega esse caso.
        try
        {
            var criacao = arquivo.CreationTime;
            if (criacao > ultima && criacao <= _agora().AddMinutes(1)) ultima = criacao;
        }
        catch { /* sem data de criação */ }
        return ultima > limite;
    }

    public static bool EhOcultoOuDoSistema(FileSystemInfo item)
    {
        var nome = item.Name;
        if (nome.StartsWith('.')) return true;
        if (nome.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
        if (nome.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
        if (nome.Equals(".DS_Store", StringComparison.Ordinal)) return true;
        try
        {
            if (OperatingSystem.IsWindows() &&
                (item.Attributes & (FileAttributes.System | FileAttributes.Hidden)) != 0)
                return true;
        }
        catch { /* sem atributos: segue */ }
        return false;
    }

    private static bool Contem(IEnumerable<string> lista, string ext) =>
        ext.Length > 0 && lista.Any(e => Extensoes.Normalizar(e) == ext);

    // ---------- Pastas de projeto ----------

    /// <summary>
    /// Uma pasta é de projeto se tiver qualquer marcador da lista (PRD e pedido: .c3p, .sln, .csproj,
    /// package.json, pasta .git, ou Assets + ProjectSettings juntas). O CubeBin nunca mexe dentro dela.
    /// </summary>
    public static bool EhPastaDeProjeto(string pasta, IEnumerable<string> marcadores)
    {
        try
        {
            if (!Directory.Exists(pasta)) return false;
            foreach (var marcador in marcadores)
            {
                if (string.IsNullOrWhiteSpace(marcador)) continue;
                var partes = marcador.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (partes.Length > 0 && partes.All(p => TemMarcador(pasta, p)))
                    return true;
            }
        }
        catch (Exception)
        {
            // Sem permissão para olhar dentro: na dúvida, trata como projeto e não mexe.
            return true;
        }
        return false;
    }

    private static bool TemMarcador(string pasta, string marcador)
    {
        if (marcador.EndsWith('/') || marcador.EndsWith('\\'))
            return Directory.Exists(Path.Combine(pasta, marcador.TrimEnd('/', '\\')));
        if (marcador.Contains('*') || marcador.Contains('?'))
            return Directory.EnumerateFiles(pasta, marcador, SearchOption.TopDirectoryOnly).Any();
        return File.Exists(Path.Combine(pasta, marcador)) || Directory.Exists(Path.Combine(pasta, marcador));
    }

    // ---------- OneDrive ----------

    /// <summary>
    /// A pasta é sincronizada com o OneDrive? No Windows, pelas variáveis que o próprio OneDrive cria;
    /// em qualquer sistema, por um trecho do caminho que comece com "OneDrive" (ex.: "OneDrive - Escola").
    /// </summary>
    public static bool EstaNoOneDrive(string caminho)
    {
        foreach (var variavel in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var raiz = Environment.GetEnvironmentVariable(variavel);
            if (!string.IsNullOrWhiteSpace(raiz) && ConfiguracaoService.EstaDentro(caminho, raiz))
                return true;
        }
        var partes = caminho.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return partes.Any(p => p.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase));
    }
}
