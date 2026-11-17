using System.IO.Compression;
using System.Net.Sockets;
using System.Text.Json;
using CubeBin.Core.Configuracao;
using CubeBin.Core.Cubos;
using CubeBin.Core.LiberarEspaco;
using CubeBin.Core.Organizar;
using CubeBin.Core.Registros;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace CubeBin.Core.Nuvem;

/// <summary>
/// "Liberar Espaço → Nuvem" (PRD 5.2, 6.2 e 6.3):
/// 1. compacta os arquivos num pacote (um por envio, nomeado pela data);
/// 2. envia para <c>CubeBin/Cubos</c> no Google Drive;
/// 3. confere se chegou inteiro (tamanho e hash calculados pelo próprio Drive);
/// 4. só então apaga os arquivos do computador;
/// 5. guarda o relatório HTML e o registro JSON em <c>CubeBin/Relatórios</c> e atualiza o
///    <c>CubeBin/indice.json</c>, que a tela de Cubos lê sem baixar os pacotes.
/// Sem internet, com o Drive cheio ou com a autorização vencida, o envio é ADIADO: nada local é
/// apagado, a pessoa é avisada e o Cubo fica com "Tentar de novo".
/// </summary>
public class EnvioNuvem
{
    public const string PastaRaiz = "CubeBin";
    public const string PastaCubos = "CubeBin/Cubos";
    public const string PastaRelatorios = "CubeBin/Relatórios";
    public const string NomeIndice = "indice.json";

    private readonly Ambiente _ambiente;
    private readonly CuboService _cubos;
    private readonly IArmazenamentoNuvem _nuvem;

    public EnvioNuvem(Ambiente ambiente, CuboService cubos, IArmazenamentoNuvem nuvem)
    {
        _ambiente = ambiente;
        _cubos = cubos;
        _nuvem = nuvem;
    }

    // ===================== Enviar =====================

    public async Task<ResultadoLiberar> EnviarAsync(SelecaoLiberar selecao, IProgress<Progresso>? progresso = null,
        CancellationToken cancelar = default, Cubo? reenvio = null)
    {
        var resultado = new ResultadoLiberar { Destino = DestinoLiberar.Nuvem };
        if (selecao.Itens.Count == 0) return resultado;

        var cubo = reenvio ?? new Cubo { Tipo = TipoCubo.Nuvem };
        cubo.Tipo = TipoCubo.Nuvem;
        cubo.Data = DateTime.Now;
        var nomePacote = $"Cubo {cubo.Data:yyyy-MM-dd HH-mm-ss}.zip";
        Directory.CreateDirectory(_ambiente.PastaEnviosPendentes);
        var zip = Path.Combine(_ambiente.PastaEnviosPendentes, cubo.Id + ".zip");
        var registro = RegistroExecucao.Criar(_ambiente.PastaRegistros, "nuvem", "manual", cubo.Id);
        var acoes = new List<(ItemLiberar Item, int N)>();

        try
        {
            // 1. Compactar, anotando no registro onde cada item ficou dentro do pacote.
            progresso?.Report(new Progresso(0, 3, "Compactando…"));
            _ambiente.Log($"Envio para a nuvem começou: {selecao.Descricao}, {selecao.Itens.Count} item(ns).");
            var entradas = CriarPacote(selecao, zip, cancelar);
            foreach (var item in selecao.Itens)
                acoes.Add((item, registro.AnotarAcao(TipoAcao.Nuvem, item.Caminho, $"{nomePacote}/{entradas[item.Caminho]}",
                    item.PastaOrigemNome, "Nuvem", item.Tamanho, extra: entradas[item.Caminho])));

            var tamanho = new FileInfo(zip).Length;
            var md5 = Hash.Md5DoArquivo(zip);

            // 2. Espaço no Drive e envio.
            progresso?.Report(new Progresso(1, 3, $"Enviando {nomePacote}…"));
            var conta = await _nuvem.ContaAsync(cancelar);
            if (conta.TotalBytes is { } total && total - conta.UsadoBytes < tamanho)
                throw new CotaNuvemException();

            var pastaCubos = await _nuvem.GarantirPastaAsync(PastaCubos, cancelar);
            var enviado = await _nuvem.EnviarAsync(zip, pastaCubos, nomePacote, "application/zip", null, cancelar);

            // 3. Conferir a integridade antes de apagar qualquer coisa (PRD 6.3).
            progresso?.Report(new Progresso(2, 3, "Conferindo se chegou inteiro…"));
            await ConferirAsync(enviado.Id, tamanho, md5, cancelar);

            cubo.NomePacote = nomePacote;
            cubo.IdPacoteNuvem = enviado.Id;
            cubo.LinkNuvem = enviado.Link;
            cubo.EnvioAdiado = false;
            cubo.MotivoAdiamento = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelar.IsCancellationRequested)
        {
            // Nada local foi apagado. O Cubo fica como "envio adiado", com Tentar de novo.
            var motivo = ExplicarErro(ex);
            foreach (var (_, n) in acoes) registro.AnotarResultado(n, ok: false, erro: "envio adiado: " + motivo);
            registro.AnotarFim();
            registro.Dispose();
            ApagarSemErro(zip);
            resultado.MotivoAdiamento = motivo;
            cubo.EnvioAdiado = true;
            cubo.MotivoAdiamento = motivo;
            Liberador.PreencherCubo(cubo, selecao.Itens, 0, registro.Caminho);
            _cubos.Salvar(cubo);
            resultado.Cubo = cubo;
            _ambiente.Log($"Envio para a nuvem adiado: {motivo} ({ex.GetType().Name}: {ex.Message}). Nenhum arquivo foi apagado.");
            return resultado;
        }
        catch (OperationCanceledException)
        {
            foreach (var (_, n) in acoes) registro.AnotarResultado(n, ok: false, erro: "cancelado");
            registro.AnotarFim();
            registro.Dispose();
            ApagarSemErro(zip);
            resultado.Cancelado = true;
            return resultado;
        }

        // 4. O pacote está seguro na nuvem: agora sim, apaga do computador.
        progresso?.Report(new Progresso(3, 3, "Liberando espaço…"));
        foreach (var (item, n) in acoes)
        {
            try
            {
                if (item.EhPasta) Directory.Delete(item.Caminho, recursive: true);
                else File.Delete(item.Caminho);
                registro.AnotarResultado(n, ok: true);
                resultado.Feitos.Add(item);
            }
            catch (Exception ex)
            {
                // O arquivo já está seguro na nuvem; só não pôde sair do computador agora.
                registro.AnotarResultado(n, ok: false, erro: ex.Message);
                resultado.Falhas.Add(new FalhaArquivo { Caminho = item.Caminho, Motivo = "já está na nuvem, mas não saiu do computador: " + Organizador.TraduzirErro(ex) });
            }
        }
        registro.AnotarFim();
        registro.Dispose();
        ApagarSemErro(zip);

        Liberador.PreencherCubo(cubo, resultado.Feitos, resultado.Falhas.Count, registro.Caminho);
        cubo.CaminhoRelatorio = resultado.CaminhoRelatorio =
            new Liberador(_ambiente, _cubos, null).GerarRelatorio(selecao, resultado, "Nuvem (Google Drive)");
        _cubos.Salvar(cubo);
        resultado.Cubo = cubo;

        // 5. Relatório, registro e índice na nuvem. Se falhar, não desfaz nada: o pacote já está lá.
        await GuardarRelatoriosNaNuvemAsync(cubo, cancelar);
        await AtualizarIndiceAsync(cubo, cancelar);
        _ambiente.Log($"Envio para a nuvem terminou: {nomePacote}, {resultado.Feitos.Count} item(ns) saíram do computador.");
        return resultado;
    }

    /// <summary>"Tentar de novo" de um envio adiado: os mesmos arquivos (os que ainda existem).</summary>
    public Task<ResultadoLiberar> TentarDeNovoAsync(Cubo cubo, Configuracoes config, IProgress<Progresso>? progresso = null, CancellationToken cancelar = default)
    {
        var caminhos = cubo.CaminhoRegistro is null
            ? new List<string>()
            : LeitorRegistro.LerItens(cubo.CaminhoRegistro).Select(i => i.Origem).Where(c => File.Exists(c) || Directory.Exists(c)).ToList();
        var selecao = Liberador.SelecionarArquivos(caminhos, config, _ambiente.PastaDados);
        return EnviarAsync(selecao, progresso, cancelar, cubo);
    }

    private async Task ConferirAsync(string id, long tamanho, string md5, CancellationToken cancelar)
    {
        var naNuvem = await _nuvem.ObterAsync(id, cancelar);
        if (naNuvem is null || naNuvem.Tamanho != tamanho)
            throw new IntegridadeException();
        if (naNuvem.Md5 is { Length: > 0 } md5Nuvem)
        {
            if (!string.Equals(md5Nuvem, md5, StringComparison.OrdinalIgnoreCase)) throw new IntegridadeException();
            return;
        }
        // A nuvem não informou o hash: baixa de volta e confere.
        var copia = Path.Combine(_ambiente.PastaEnviosPendentes, "conferencia-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await _nuvem.BaixarAsync(id, copia, null, cancelar);
            if (!string.Equals(Hash.Md5DoArquivo(copia), md5, StringComparison.OrdinalIgnoreCase)) throw new IntegridadeException();
        }
        finally
        {
            ApagarSemErro(copia);
        }
    }

    /// <summary>Monta o pacote. Devolve, para cada item, onde ele ficou dentro do zip
    /// (arquivo: "Downloads/foto.jpg"; pasta: "Downloads/viagem/").</summary>
    internal static Dictionary<string, string> CriarPacote(SelecaoLiberar selecao, string zip, CancellationToken cancelar)
    {
        ApagarSemErro(zip);
        var entradas = new Dictionary<string, string>();
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var pacote = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var item in selecao.Itens)
        {
            cancelar.ThrowIfCancellationRequested();
            var pasta = LimparNome(string.IsNullOrWhiteSpace(item.PastaOrigemNome) ? "Arquivos" : item.PastaOrigemNome);
            var nome = UnicoNoPacote(pasta, LimparNome(item.Nome), usados);
            if (item.EhPasta)
            {
                var prefixo = nome + "/";
                foreach (var arquivo in Directory.EnumerateFiles(item.Caminho, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                {
                    var relativo = Path.GetRelativePath(item.Caminho, arquivo).Replace('\\', '/');
                    pacote.CreateEntryFromFile(arquivo, prefixo + relativo, CompressionLevel.Optimal);
                }
                entradas[item.Caminho] = prefixo;
            }
            else
            {
                pacote.CreateEntryFromFile(item.Caminho, nome, CompressionLevel.Optimal);
                entradas[item.Caminho] = nome;
            }
        }
        return entradas;
    }

    private static string UnicoNoPacote(string pasta, string nome, HashSet<string> usados)
    {
        var candidato = $"{pasta}/{nome}";
        var semExt = Path.GetFileNameWithoutExtension(nome);
        var ext = Path.GetExtension(nome);
        for (var i = 2; !usados.Add(candidato); i++) candidato = $"{pasta}/{semExt} ({i}){ext}";
        return candidato;
    }

    private static string LimparNome(string nome) =>
        string.Concat(nome.Select(c => c is '/' or '\\' or ':' ? '_' : c));

    // ===================== Relatórios e índice na nuvem =====================

    private async Task GuardarRelatoriosNaNuvemAsync(Cubo cubo, CancellationToken cancelar)
    {
        try
        {
            var pasta = await _nuvem.GarantirPastaAsync(PastaRelatorios, cancelar);
            var baseNome = Path.GetFileNameWithoutExtension(cubo.NomePacote ?? cubo.Id);
            if (cubo.CaminhoRegistro is { } registro && File.Exists(registro))
                cubo.IdRegistroNuvem = (await _nuvem.EnviarAsync(registro, pasta, $"{baseNome} - registro.jsonl", "application/json", null, cancelar)).Id;
            if (cubo.CaminhoRelatorio is { Length: > 0 } relatorio && File.Exists(relatorio))
                cubo.IdRelatorioNuvem = (await _nuvem.EnviarAsync(relatorio, pasta, $"{baseNome} - relatório.html", "text/html", null, cancelar)).Id;
            _cubos.Salvar(cubo);
        }
        catch (Exception ex)
        {
            _ambiente.Log($"Não foi possível guardar o relatório na nuvem ({ExplicarErro(ex)}). O pacote já está seguro.");
        }
    }

    /// <summary>Acrescenta o Cubo ao CubeBin/indice.json do Drive.</summary>
    private async Task AtualizarIndiceAsync(Cubo cubo, CancellationToken cancelar)
    {
        try
        {
            var raiz = await _nuvem.GarantirPastaAsync(PastaRaiz, cancelar);
            var existente = await _nuvem.ProcurarAsync(raiz, NomeIndice, cancelar);
            var indice = existente is null ? new List<Cubo>() : await LerIndiceAsync(existente.Id, cancelar);
            indice.RemoveAll(c => c.Id == cubo.Id);
            indice.Add(ParaIndice(cubo));

            var temporario = Path.Combine(_ambiente.PastaEnviosPendentes, "indice-" + Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(_ambiente.PastaEnviosPendentes);
            await File.WriteAllTextAsync(temporario, JsonSerializer.Serialize(indice.OrderByDescending(c => c.Data), ConfiguracaoService.OpcoesJson), cancelar);
            try
            {
                if (existente is null) await _nuvem.EnviarAsync(temporario, raiz, NomeIndice, "application/json", null, cancelar);
                else await _nuvem.AtualizarAsync(existente.Id, temporario, "application/json", cancelar);
            }
            finally
            {
                ApagarSemErro(temporario);
            }
        }
        catch (Exception ex)
        {
            _ambiente.Log($"Não foi possível atualizar o índice na nuvem ({ExplicarErro(ex)}).");
        }
    }

    /// <summary>
    /// Lê o índice do Drive e acrescenta à tela de Cubos os Cubos que este computador não conhece
    /// (ex.: depois de reinstalar). Não baixa nenhum pacote. Devolve quantos foram acrescentados.
    /// </summary>
    public async Task<int> SincronizarIndiceAsync(CancellationToken cancelar = default)
    {
        var raiz = await _nuvem.GarantirPastaAsync(PastaRaiz, cancelar);
        var existente = await _nuvem.ProcurarAsync(raiz, NomeIndice, cancelar);
        if (existente is null) return 0;
        var conhecidos = _cubos.Listar().Select(c => c.Id).ToHashSet();
        var novos = 0;
        foreach (var cubo in await LerIndiceAsync(existente.Id, cancelar))
        {
            if (conhecidos.Contains(cubo.Id)) continue;
            _cubos.Salvar(cubo);
            novos++;
        }
        return novos;
    }

    private async Task<List<Cubo>> LerIndiceAsync(string id, CancellationToken cancelar)
    {
        var temporario = Path.Combine(_ambiente.PastaEnviosPendentes, "indice-lido-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(_ambiente.PastaEnviosPendentes);
        try
        {
            await _nuvem.BaixarAsync(id, temporario, null, cancelar);
            return JsonSerializer.Deserialize<List<Cubo>>(await File.ReadAllTextAsync(temporario, cancelar), ConfiguracaoService.OpcoesJson) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
        finally
        {
            ApagarSemErro(temporario);
        }
    }

    /// <summary>No índice vão só os dados da nuvem (caminhos locais não servem em outro computador).</summary>
    private static Cubo ParaIndice(Cubo c) => new()
    {
        Id = c.Id, Tipo = c.Tipo, Data = c.Data, Modo = c.Modo, Arquivos = c.Arquivos, Bytes = c.Bytes, Falhas = c.Falhas,
        PorCategoria = c.PorCategoria, PastasDeOrigem = c.PastasDeOrigem, NomePacote = c.NomePacote,
        IdPacoteNuvem = c.IdPacoteNuvem, LinkNuvem = c.LinkNuvem, IdRegistroNuvem = c.IdRegistroNuvem,
        IdRelatorioNuvem = c.IdRelatorioNuvem, Restaurado = c.Restaurado, RestauradoEm = c.RestauradoEm,
    };

    // ===================== Restaurar =====================

    /// <summary>
    /// Baixa o pacote e devolve cada arquivo ao lugar de origem (com a regra de conflito: nunca
    /// sobrescreve). O pacote continua no Drive, como cópia de segurança.
    /// </summary>
    public async Task<ResultadoRestauracao> RestaurarAsync(Cubo cubo, IProgress<Progresso>? progresso = null, CancellationToken cancelar = default)
    {
        var resultado = new ResultadoRestauracao();
        if (cubo.IdPacoteNuvem is null)
        {
            resultado.Falhas.Add(new FalhaArquivo { Motivo = "este Cubo não tem pacote na nuvem" });
            return resultado;
        }

        // O registro diz de onde cada arquivo saiu; se não estiver neste computador, vem da nuvem.
        if (cubo.CaminhoRegistro is null || !File.Exists(cubo.CaminhoRegistro))
        {
            if (cubo.IdRegistroNuvem is null)
            {
                resultado.Falhas.Add(new FalhaArquivo { Motivo = "o registro deste Cubo não foi encontrado" });
                return resultado;
            }
            Directory.CreateDirectory(_ambiente.PastaRegistros);
            cubo.CaminhoRegistro = Path.Combine(_ambiente.PastaRegistros, $"{cubo.Id}-da-nuvem.jsonl");
            await _nuvem.BaixarAsync(cubo.IdRegistroNuvem, cubo.CaminhoRegistro, null, cancelar);
        }

        Directory.CreateDirectory(_ambiente.PastaEnviosPendentes);
        var zip = Path.Combine(_ambiente.PastaEnviosPendentes, "restaurar-" + cubo.Id + ".zip");
        progresso?.Report(new Progresso(0, 1, "Baixando o pacote…"));
        await _nuvem.BaixarAsync(cubo.IdPacoteNuvem, zip, null, cancelar);

        try
        {
            using var pacote = ZipFile.OpenRead(zip);
            var itens = LeitorRegistro.LerItens(cubo.CaminhoRegistro).Where(i => i.Acao == TipoAcao.Nuvem && i.Ok == true && !i.Restaurado).ToList();
            var reservados = NomeSemConflito.NovoConjunto();
            using var registro = RegistroExecucao.Reabrir(cubo.CaminhoRegistro);
            for (var i = 0; i < itens.Count; i++)
            {
                var item = itens[i];
                progresso?.Report(new Progresso(i, itens.Count, Path.GetFileName(item.Origem)));
                var pastaOrigem = Path.GetDirectoryName(item.Origem)!;
                Directory.CreateDirectory(pastaOrigem);
                var para = NomeSemConflito.Resolver(pastaOrigem, Path.GetFileName(item.Origem), "Nuvem", reservados);
                if (!string.Equals(para, item.Origem, StringComparison.Ordinal)) resultado.Renomeados.Add(para);
                try
                {
                    var entrada = item.Extra ?? "";
                    if (entrada.EndsWith('/'))
                    {
                        foreach (var e in pacote.Entries.Where(e => e.FullName.StartsWith(entrada, StringComparison.Ordinal) && e.Name.Length > 0))
                        {
                            var alvo = Path.GetFullPath(Path.Combine(para, e.FullName[entrada.Length..]));
                            if (!alvo.StartsWith(Path.GetFullPath(para), StringComparison.Ordinal)) continue; // nunca fora da pasta
                            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
                            e.ExtractToFile(alvo, overwrite: false);
                        }
                        Directory.CreateDirectory(para);
                    }
                    else
                    {
                        var e = pacote.GetEntry(entrada) ?? throw new FileNotFoundException("o arquivo não está no pacote");
                        e.ExtractToFile(para, overwrite: false);
                    }
                    registro.AnotarRestauracao(item.N, $"{cubo.NomePacote}/{entrada}", para, ok: true);
                    resultado.Restaurados++;
                }
                catch (Exception ex)
                {
                    registro.AnotarRestauracao(item.N, item.Extra ?? "", para, ok: false, Organizador.TraduzirErro(ex));
                    resultado.Falhas.Add(new FalhaArquivo { Caminho = item.Origem, Motivo = Organizador.TraduzirErro(ex) });
                }
            }
        }
        finally
        {
            ApagarSemErro(zip);
        }

        if (resultado.Falhas.Count == 0)
        {
            cubo.Restaurado = true;
            cubo.RestauradoEm = DateTime.Now;
        }
        _cubos.Salvar(cubo);
        await AtualizarIndiceAsync(cubo, cancelar);
        _ambiente.Log($"Restauração da nuvem ({cubo.NomePacote}): {resultado.Restaurados} item(ns) de volta.");
        return resultado;
    }

    // ===================== Erros =====================

    /// <summary>O motivo do adiamento em palavras simples (PRD 6.3).</summary>
    public static string ExplicarErro(Exception ex) => ex switch
    {
        CotaNuvemException => "o Google Drive está cheio",
        IntegridadeException => "o pacote não chegou inteiro na nuvem",
        TokenResponseException => "a autorização do Google expirou: conecte a conta de novo em Configurações › Nuvem",
        GoogleApiException g when g.Error?.Errors?.Any(e => e.Reason is "storageQuotaExceeded" or "quotaExceeded") == true => "o Google Drive está cheio",
        GoogleApiException g when g.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized => "a autorização do Google expirou: conecte a conta de novo em Configurações › Nuvem",
        HttpRequestException or SocketException or TaskCanceledException => "sem conexão com a internet",
        InvalidOperationException => ex.Message.TrimEnd('.'),
        _ when ex.InnerException is not null => ExplicarErro(ex.InnerException),
        _ => ex.Message.TrimEnd('.'),
    };

    private static void ApagarSemErro(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch { /* sobra temporária */ }
    }
}

public sealed class CotaNuvemException : Exception
{
    public CotaNuvemException() : base("O Google Drive está cheio.") { }
}

public sealed class IntegridadeException : Exception
{
    public IntegridadeException() : base("O pacote não chegou inteiro na nuvem.") { }
}
