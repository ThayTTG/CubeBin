using CubeBin.Core.Configuracao;

namespace CubeBin.Core.Organizar;

/// <summary>Um arquivo que vai mudar de lugar.</summary>
public class Movimento
{
    public string Origem { get; init; } = "";
    public string Destino { get; set; } = "";
    public string PastaOrigemId { get; init; } = "";
    public string PastaOrigemNome { get; init; } = "";
    public string PrefixoOrigem { get; init; } = "";
    public string PastaDestinoId { get; init; } = "";
    public string PastaDestinoNome { get; init; } = "";
    public string PastaDestinoCaminho { get; init; } = "";
    public long Tamanho { get; init; }
    public bool NoOneDrive { get; init; }

    public string NomeArquivo => Path.GetFileName(Origem);
    public string NomeFinal => Path.GetFileName(Destino);
    public bool Renomeado => !string.Equals(NomeArquivo, NomeFinal, StringComparison.Ordinal);
}

public class ArquivoPulado
{
    public string Caminho { get; init; } = "";
    public MotivoPulado Motivo { get; init; }
    public string Explicacao => Protecao.Explicar(Motivo);
}

/// <summary>O que vai acontecer se a pessoa confirmar (a "Prévia" do PRD 5.1).</summary>
public class PlanoOrganizacao
{
    public DateTime CriadoEm { get; } = DateTime.Now;
    public List<Movimento> Movimentos { get; } = new();

    /// <summary>Arquivos com extensão que ainda não tem pasta (ficam onde estão).</summary>
    public List<string> SemPasta { get; } = new();

    public List<ArquivoPulado> Pulados { get; } = new();

    /// <summary>Pastas que parecem projetos (Construct, Unity, código...) e foram puladas inteiras.</summary>
    public List<string> PastasDeProjeto { get; } = new();

    /// <summary>Pastas monitoradas que não existem no disco.</summary>
    public List<string> PastasInexistentes { get; } = new();

    /// <summary>Pastas sincronizadas com o OneDrive com muitos arquivos para mover (PRD 6.3).</summary>
    public List<string> AvisosOneDrive { get; } = new();

    /// <summary>Pastas de destino que ainda não existem e serão criadas.</summary>
    public List<string> DestinosACriar { get; } = new();

    public long BytesAMover => Movimentos.Sum(m => m.Tamanho);
    public IEnumerable<string> ExtensoesSemPasta =>
        SemPasta.Select(Extensoes.DoArquivo).Where(e => e.Length > 0).Distinct();

    /// <summary>{ "Imagens": [movimentos...] } — para mostrar a prévia por pasta de destino.</summary>
    public IEnumerable<IGrouping<string, Movimento>> PorDestino =>
        Movimentos.GroupBy(m => m.PastaDestinoNome).OrderByDescending(g => g.Count());
}

/// <summary>
/// Monta a prévia: olha as pastas monitoradas, aplica as regras e as proteções e decide, para cada
/// arquivo, para onde ele vai e com que nome. Não mexe em nada.
/// </summary>
public class Planejador
{
    private readonly Configuracoes _config;
    private readonly Protecao _protecao;

    public Planejador(Configuracoes config, Protecao? protecao = null)
    {
        _config = config;
        _protecao = protecao ?? new Protecao(config.Protecoes);
    }

    public PlanoOrganizacao Planejar(CancellationToken cancelar = default)
    {
        var plano = new PlanoOrganizacao();
        var reservados = NomeSemConflito.NovoConjunto();

        // Todas as pastas de destino: nunca entramos nelas ao descer pelas subpastas.
        var destinos = _config.Regras
            .Select(r => _config.ObterPasta(r.PastaId))
            .Where(p => p is not null)
            .Select(p => p!.CaminhoEfetivo())
            .Where(c => c.Length > 0)
            .ToList();

        foreach (var pasta in _config.PastasMonitoradas)
        {
            cancelar.ThrowIfCancellationRequested();
            var caminho = pasta.CaminhoEfetivo();
            if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
            {
                plano.PastasInexistentes.Add(pasta.Nome);
                continue;
            }

            if (Protecao.EhPastaDeProjeto(caminho, _config.Protecoes.MarcadoresDeProjeto))
            {
                plano.PastasDeProjeto.Add(caminho);
                continue;
            }

            var noOneDrive = Protecao.EstaNoOneDrive(caminho);
            var antes = plano.Movimentos.Count;

            foreach (var arquivo in ListarArquivos(caminho, pasta.OrganizarSubpastas, destinos, plano, cancelar))
                Planejar(arquivo, pasta, noOneDrive, plano, reservados);

            if (noOneDrive && plano.Movimentos.Count - antes >= _config.Protecoes.LimiteAvisoOneDriveArquivos)
                plano.AvisosOneDrive.Add(pasta.Nome);
        }

        foreach (var destino in plano.Movimentos.Select(m => m.PastaDestinoCaminho).Distinct())
            if (!Directory.Exists(destino)) plano.DestinosACriar.Add(destino);

        return plano;
    }

    private void Planejar(FileInfo arquivo, PastaConfig pastaOrigem, bool noOneDrive, PlanoOrganizacao plano, ISet<string> reservados)
    {
        var motivo = _protecao.MotivoParaNaoMover(arquivo);
        if (motivo is not null)
        {
            // Ocultos e do sistema não aparecem na prévia: não são "arquivos do usuário".
            if (motivo != MotivoPulado.OcultoOuDoSistema)
                plano.Pulados.Add(new ArquivoPulado { Caminho = arquivo.FullName, Motivo = motivo.Value });
            return;
        }

        var ext = Extensoes.DoArquivo(arquivo.Name);
        if (ext.Length == 0 || _config.ExtensoesIgnoradas.Contains(ext)) return;

        var regra = _config.RegraDaExtensao(ext);
        var destino = regra is null ? null : _config.ObterPasta(regra.PastaId);
        if (destino is null)
        {
            plano.SemPasta.Add(arquivo.FullName);
            return;
        }

        var pastaDestino = destino.CaminhoEfetivo();
        if (string.IsNullOrWhiteSpace(pastaDestino)) return;

        // Já está na pasta certa (ou numa subpasta dela): não mexe. Isso também protege quem
        // organizou subpastas à mão dentro de Documentos, Imagens...
        if (ConfiguracaoService.EstaDentro(arquivo.DirectoryName ?? "", pastaDestino)) return;

        plano.Movimentos.Add(new Movimento
        {
            Origem = arquivo.FullName,
            Destino = NomeSemConflito.Resolver(pastaDestino, arquivo.Name, pastaOrigem.PrefixoEfetivo(), reservados),
            PastaOrigemId = pastaOrigem.Id,
            PastaOrigemNome = pastaOrigem.Nome,
            PrefixoOrigem = pastaOrigem.PrefixoEfetivo(),
            PastaDestinoId = destino.Id,
            PastaDestinoNome = destino.Nome,
            PastaDestinoCaminho = pastaDestino,
            Tamanho = arquivo.Length,
            NoOneDrive = noOneDrive,
        });
    }

    /// <summary>
    /// Os arquivos de uma pasta monitorada. Com subpastas ligadas, desce nelas, mas nunca entra em
    /// pastas de destino, pastas ocultas, atalhos de pasta (links) ou pastas que parecem projetos.
    /// </summary>
    private IEnumerable<FileInfo> ListarArquivos(string raiz, bool subpastas, List<string> destinos,
        PlanoOrganizacao plano, CancellationToken cancelar)
    {
        var pendentes = new Stack<DirectoryInfo>();
        pendentes.Push(new DirectoryInfo(raiz));
        var primeira = true;

        while (pendentes.Count > 0)
        {
            cancelar.ThrowIfCancellationRequested();
            var pasta = pendentes.Pop();

            if (!primeira)
            {
                if (Protecao.EhOcultoOuDoSistema(pasta) || pasta.LinkTarget is not null) continue;
                if (destinos.Any(d => ConfiguracaoService.MesmoCaminho(d, pasta.FullName))) continue;
                if (Protecao.EhPastaDeProjeto(pasta.FullName, _config.Protecoes.MarcadoresDeProjeto))
                {
                    plano.PastasDeProjeto.Add(pasta.FullName);
                    continue;
                }
            }
            primeira = false;

            FileInfo[] arquivos;
            try { arquivos = pasta.GetFiles(); }
            catch (Exception) { continue; } // sem permissão: segue com o resto

            foreach (var arquivo in arquivos.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (arquivo.LinkTarget is not null) continue; // links simbólicos ficam onde estão
                yield return arquivo;
            }

            if (!subpastas) continue;
            try
            {
                foreach (var sub in pasta.GetDirectories().OrderByDescending(d => d.Name))
                    pendentes.Push(sub);
            }
            catch (Exception) { /* sem permissão */ }
        }
    }
}
