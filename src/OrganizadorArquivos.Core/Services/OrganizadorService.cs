using OrganizadorArquivos.Core.Models;

namespace OrganizadorArquivos.Core.Services;

/// <summary>
/// Orquestra o pipeline completo de uma execução (seção 3 da spec):
/// checagem de ausência -> aviso com cancelamento -> backup -> temporário ->
/// classificação -> destino final (local ou Google Drive) -> notificação de resultado.
/// </summary>
public class OrganizadorService
{
    private readonly ConfigService _configService;
    private readonly BackupService _backupService;
    private readonly ClassificadorService _classificadorService;
    private readonly PendenciaService _pendenciaService;
    private readonly RecallService _recallService;
    private readonly GoogleDriveService _googleDriveService;
    private readonly IdleDetectionService _idleDetectionService;
    private readonly INotificationService _notificationService;

    /// <summary>Disparado quando uma organização (manual ou automática) começa de fato a mover arquivos.</summary>
    public event Action? ExecucaoIniciada;

    /// <summary>Disparado quando a organização termina (com sucesso, falha parcial, ou erro).</summary>
    public event Action? ExecucaoFinalizada;

    public OrganizadorService(
        ConfigService configService,
        BackupService backupService,
        ClassificadorService classificadorService,
        PendenciaService pendenciaService,
        RecallService recallService,
        GoogleDriveService googleDriveService,
        IdleDetectionService idleDetectionService,
        INotificationService notificationService)
    {
        _configService = configService;
        _backupService = backupService;
        _classificadorService = classificadorService;
        _pendenciaService = pendenciaService;
        _recallService = recallService;
        _googleDriveService = googleDriveService;
        _idleDetectionService = idleDetectionService;
        _notificationService = notificationService;
    }

    public async Task<ResultadoExecucao?> ExecutarCicloAsync(AppConfig config)
    {
        if (!_idleDetectionService.UsuarioEstaAusente(config.TempoAusenciaMinutos))
            return null; // Usuário ativo: aguarda o próximo ciclo do agendador.

        var cancelado = await _notificationService.NotificarInicioComCancelamentoAsync(config.JanelaCancelamentoSegundos);
        if (cancelado)
            return new ResultadoExecucao { InicioEm = DateTime.Now, FimEm = DateTime.Now, Cancelado = true };

        // Roda em background thread — importante agora que a organização pode envolver upload
        // para o Google Drive (chamada de rede, mais lenta que I/O local), para não travar a UI
        // durante uma execução automática disparada pelo agendador.
        return await Task.Run(() => ExecutarOrganizacaoAsync(config));
    }

    /// <summary>
    /// Executa a organização imediatamente, a pedido do usuário (menu da bandeja ou botão na UI).
    /// Pula a checagem de ausência e a janela de cancelamento — o próprio clique já é a confirmação.
    /// O trabalho de I/O roda em background thread para não travar a UI.
    /// </summary>
    public Task<ResultadoExecucao> ExecutarManualmenteAsync(AppConfig config)
    {
        return Task.Run(() => ExecutarOrganizacaoAsync(config));
    }

    private async Task<ResultadoExecucao> ExecutarOrganizacaoAsync(AppConfig config)
    {
        ExecucaoIniciada?.Invoke();
        try
        {
            return await ExecutarOrganizacaoInternoAsync(config);
        }
        finally
        {
            ExecucaoFinalizada?.Invoke();
        }
    }

    private async Task<ResultadoExecucao> ExecutarOrganizacaoInternoAsync(AppConfig config)
    {
        var resultado = new ResultadoExecucao { InicioEm = DateTime.Now };

        // 1) Escaneia todas as pastas monitoradas (respeitando OrganizarSubpastas por pasta).
        // Quando recursivo, nunca entra nas próprias pastas de destino geridas por essa pasta —
        // senão arquivos já organizados seriam "redescobertos" e reorganizados a cada execução,
        // gerando duplicatas no Recall e no backup (item 3 do feedback de uso).
        var pastaOrigemPorCaminho = new Dictionary<string, string>();
        var pastaPorCaminhoArquivo = new Dictionary<string, PastaMonitorada>();
        var arquivosEncontrados = new List<FileInfo>();

        foreach (var pasta in config.TodasAsPastas)
        {
            if (!Directory.Exists(pasta.Caminho)) continue;

            foreach (var arquivo in EscanearArquivosDaPasta(pasta, config))
            {
                if (DeveIgnorarArquivoDeSistema(arquivo)) continue;

                arquivosEncontrados.Add(arquivo);
                pastaOrigemPorCaminho[arquivo.FullName] = pasta.Nome;
                pastaPorCaminhoArquivo[arquivo.FullName] = pasta;
            }
        }

        // 2) Backup em CSV, em ordem alfabética, antes de qualquer movimentação (seção 4).
        resultado.CaminhoBackupCsv = _backupService.GerarBackupCsv(arquivosEncontrados, pastaOrigemPorCaminho, config.Backup);

        // 3) Move para o diretório temporário. Usa um prefixo único por arquivo no nome do estágio
        // temporário — dois arquivos de mesmo nome vindos de pastas de origem diferentes (ex.:
        // "notas.txt" em Documentos E em Downloads) não podem colidir aqui; o nome original é
        // preservado separadamente e usado ao mover para o destino final.
        Directory.CreateDirectory(config.DiretorioTemporario);
        var arquivosNoTemporario = new List<(FileInfo Arquivo, PastaMonitorada Origem, string CaminhoOriginal, string NomeOriginal)>();

        foreach (var arquivo in arquivosEncontrados)
        {
            try
            {
                var caminhoOriginal = arquivo.FullName;
                var nomeEstagio = $"{Guid.NewGuid():N}_{arquivo.Name}";
                var destinoTemp = Path.Combine(config.DiretorioTemporario, nomeEstagio);
                ArquivoUtils.MoverArquivo(arquivo.FullName, destinoTemp, sobrescrever: false);
                arquivosNoTemporario.Add((new FileInfo(destinoTemp), pastaPorCaminhoArquivo[arquivo.FullName], caminhoOriginal, arquivo.Name));
            }
            catch (Exception ex)
            {
                resultado.Falhas.Add((arquivo.FullName, ex.Message));
                resultado.ArquivosComFalha++;
            }
        }

        // 4) Classifica e move para o destino definitivo — local ou Google Drive — (ou fila de pendências).
        foreach (var (arquivoTemp, pastaOrigem, caminhoOriginal, nomeOriginal) in arquivosNoTemporario)
        {
            var classificacao = _classificadorService.Classificar(arquivoTemp, config, pastaOrigem);

            if (classificacao.Resultado == ResultadoClassificacao.SemRegra)
            {
                _pendenciaService.Adicionar(config.ArquivoFilaPendencias, new ArquivoPendente
                {
                    NomeArquivo = nomeOriginal,
                    CaminhoOriginal = arquivoTemp.FullName,
                    Extensao = arquivoTemp.Extension,
                    PastaOrigemId = pastaOrigem.Id,
                });
                resultado.ArquivosPendentes++;
                continue;
            }

            if (classificacao.TipoDestino == TipoDestino.GoogleDrive)
            {
                await ProcessarUploadDriveAsync(arquivoTemp, nomeOriginal, caminhoOriginal, classificacao, resultado, config);
                continue;
            }

            try
            {
                var pastaDestino = _classificadorService.ResolverCaminhoDestino(pastaOrigem.Caminho, classificacao);
                Directory.CreateDirectory(pastaDestino);

                var nomeFinal = _classificadorService.ResolverNomeDestino(nomeOriginal, pastaDestino, pastaOrigem);
                var caminhoFinal = Path.Combine(pastaDestino, nomeFinal);
                ArquivoUtils.MoverArquivo(arquivoTemp.FullName, caminhoFinal, sobrescrever: false);

                // A partir daqui o arquivo já foi movido com sucesso — isso conta, independente
                // do que acontecer a seguir. Registrar no Recall é só bookkeeping complementar.
                resultado.ArquivosMovidos++;

                try
                {
                    _recallService.Adicionar(config.ArquivoRecall, new ItemRecall
                    {
                        NomeArquivo = nomeFinal,
                        CaminhoOrigem = caminhoOriginal,
                        CaminhoAtual = caminhoFinal,
                        MovidoEm = DateTime.Now
                    });
                }
                catch (Exception exRecall)
                {
                    // Falha ao registrar no recall.json não deve fazer uma movimentação bem-sucedida
                    // ser reportada como falha — mas também não deve ficar completamente invisível.
                    resultado.AvisosRecall.Add($"{nomeFinal}: {exRecall.Message}");
                }
            }
            catch (Exception ex)
            {
                resultado.Falhas.Add((arquivoTemp.FullName, ex.Message));
                resultado.ArquivosComFalha++;
            }
        }

        resultado.FimEm = DateTime.Now;
        _notificationService.NotificarResultadoFinal(resultado);
        return resultado;
    }

    /// <summary>
    /// Envia um arquivo classificado para o Google Drive em vez de uma pasta local. Só apaga o
    /// arquivo do temporário DEPOIS de confirmar que o upload terminou — qualquer falha (rede,
    /// autenticação, o que for) deixa o arquivo intocado no temporário, contabilizado como falha.
    /// </summary>
    private async Task ProcessarUploadDriveAsync(
        FileInfo arquivoTemp, string nomeOriginal, string caminhoOriginal,
        ClassificacaoArquivo classificacao, ResultadoExecucao resultado, AppConfig config)
    {
        try
        {
            var caminhoDrive = string.IsNullOrWhiteSpace(classificacao.SubPasta)
                ? classificacao.CaminhoDrive ?? string.Empty
                : $"{classificacao.CaminhoDrive}/{classificacao.SubPasta}";

            var idArquivo = await _googleDriveService.EnviarArquivoAsync(arquivoTemp.FullName, caminhoDrive);

            File.Delete(arquivoTemp.FullName);
            resultado.ArquivosMovidos++;

            try
            {
                _recallService.Adicionar(config.ArquivoRecall, new ItemRecall
                {
                    NomeArquivo = nomeOriginal,
                    CaminhoOrigem = caminhoOriginal,
                    CaminhoAtual = $"Google Drive: {caminhoDrive}/{nomeOriginal}",
                    Armazenamento = "GoogleDrive",
                    IdArquivoDrive = idArquivo,
                    MovidoEm = DateTime.Now
                });
            }
            catch (Exception exRecall)
            {
                resultado.AvisosRecall.Add($"{nomeOriginal}: {exRecall.Message}");
            }
        }
        catch (Exception ex)
        {
            resultado.Falhas.Add((arquivoTemp.FullName, $"Google Drive: {ex.Message}"));
            resultado.ArquivosComFalha++;
        }
    }

    /// <summary>
    /// Lista os arquivos de uma pasta monitorada. Se OrganizarSubpastas estiver ativo, desce
    /// recursivamente mas nunca entra nas pastas que são destino de alguma regra local dessa
    /// mesma pasta — essas pastas são "geridas" pelo próprio organizador, e reprocessá-las
    /// causaria um ciclo (arquivo já organizado sendo "descoberto" e reorganizado de novo a cada
    /// execução). Regras com destino no Google Drive não criam pasta local nenhuma, então não
    /// entram nesse cálculo.
    /// </summary>
    private static List<FileInfo> EscanearArquivosDaPasta(PastaMonitorada pasta, AppConfig config)
    {
        if (!pasta.OrganizarSubpastas)
            return new DirectoryInfo(pasta.Caminho).GetFiles("*", SearchOption.TopDirectoryOnly).ToList();

        var pastasGerenciadas = ObterPastasGerenciadas(pasta, config);
        var resultado = new List<FileInfo>();
        ColetarArquivosRecursivo(new DirectoryInfo(pasta.Caminho), pastasGerenciadas, resultado);
        return resultado;
    }

    private static HashSet<string> ObterPastasGerenciadas(PastaMonitorada pasta, AppConfig config)
    {
        var conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var regra in config.RegrasExtensao.Values)
        {
            if (regra.TipoDestino != TipoDestino.Local) continue; // Drive não cria pasta local.
            if (string.IsNullOrWhiteSpace(regra.Destino)) continue;

            var caminhoDestino = Path.Combine(pasta.Caminho, regra.Destino).TrimEnd(Path.DirectorySeparatorChar);
            conjunto.Add(caminhoDestino);

            foreach (var subpasta in regra.Subpastas.Values)
            {
                if (string.IsNullOrWhiteSpace(subpasta)) continue;
                conjunto.Add(Path.Combine(caminhoDestino, subpasta).TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        return conjunto;
    }

    private static void ColetarArquivosRecursivo(DirectoryInfo diretorio, HashSet<string> pastasGerenciadas, List<FileInfo> resultado)
    {
        if (pastasGerenciadas.Contains(diretorio.FullName.TrimEnd(Path.DirectorySeparatorChar)))
            return;

        resultado.AddRange(diretorio.GetFiles());

        foreach (var subdiretorio in diretorio.GetDirectories())
            ColetarArquivosRecursivo(subdiretorio, pastasGerenciadas, resultado);
    }

    /// <summary>
    /// Identifica arquivos de metadados do próprio Windows (não são "arquivos do usuário") que
    /// nunca devem ser organizados — movê-los quebraria customizações da pasta de origem (ícone,
    /// modo de exibição, miniaturas em cache).
    /// </summary>
    private static bool DeveIgnorarArquivoDeSistema(FileInfo arquivo)
    {
        if ((arquivo.Attributes & FileAttributes.System) != 0) return true;
        if (arquivo.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
        if (arquivo.Name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
