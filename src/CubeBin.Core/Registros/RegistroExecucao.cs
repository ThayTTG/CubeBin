using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CubeBin.Core.Configuracao;

namespace CubeBin.Core.Registros;

/// <summary>O que uma linha do registro descreve.</summary>
public enum TipoLinha
{
    /// <summary>Primeira linha: qual operação, quando, manual ou automática.</summary>
    Inicio,
    /// <summary>Escrita ANTES de mexer no arquivo: de onde, para onde.</summary>
    Acao,
    /// <summary>Escrita DEPOIS: deu certo ou não.</summary>
    Resultado,
    /// <summary>Uma devolução feita pelo Desfazer/Restaurar.</summary>
    Restauracao,
    /// <summary>Última linha: a execução terminou inteira.</summary>
    Fim,
}

/// <summary>O que foi feito com o arquivo.</summary>
public enum TipoAcao
{
    Mover,
    Lixeira,
    ApagarDeVez,
    Nuvem,
}

/// <summary>Uma linha do registro. Só os campos que fazem sentido para o tipo vêm preenchidos.</summary>
public class LinhaRegistro
{
    public TipoLinha Tipo { get; set; }
    public int N { get; set; }
    public DateTime Data { get; set; }

    // Início
    public string? Operacao { get; set; }
    public string? Modo { get; set; }
    public string? CuboId { get; set; }

    // Ação
    public TipoAcao? Acao { get; set; }
    public string? Origem { get; set; }
    public string? Destino { get; set; }
    public string? PastaOrigem { get; set; }
    public string? PastaDestino { get; set; }
    public long? Tamanho { get; set; }
    /// <summary>Para a Lixeira do Linux: o nome usado dentro de Trash/files.</summary>
    public string? Extra { get; set; }

    // Resultado / Restauração
    public bool? Ok { get; set; }
    public string? Erro { get; set; }
}

/// <summary>
/// O registro de uma execução (PRD 6: "registro do estado anterior em JSON antes de cada execução",
/// e 6.3: "gravar o JSON arquivo por arquivo, durante a execução").
///
/// Formato: JSON Lines — um objeto JSON por linha. Cada arquivo ganha uma linha ANTES de ser mexido
/// (de onde → para onde) e outra DEPOIS (deu certo ou não), e cada linha vai para o disco na hora
/// (flush com gravação direta). Se o app travar ou o computador desligar no meio, tudo o que já foi
/// mexido está anotado, e o Desfazer encontra cada arquivo olhando onde ele está agora. Acentos,
/// vírgulas e aspas nos nomes não são problema (era isso que quebrava o CSV).
/// </summary>
public sealed class RegistroExecucao : IDisposable
{
    public static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FileStream _arquivo;
    private readonly object _trava = new();
    private int _contador;

    public string Caminho { get; }

    private RegistroExecucao(string caminho)
    {
        Caminho = caminho;
        _arquivo = new FileStream(caminho, FileMode.Append, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.WriteThrough);
    }

    /// <summary>Cria o registro em <paramref name="pasta"/> e grava a linha de início.</summary>
    public static RegistroExecucao Criar(string pasta, string operacao, string modo, string cuboId)
    {
        Directory.CreateDirectory(pasta);
        var nome = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{operacao}_{cuboId[..Math.Min(6, cuboId.Length)]}.jsonl";
        var registro = new RegistroExecucao(Path.Combine(pasta, nome));
        registro.Gravar(new LinhaRegistro { Tipo = TipoLinha.Inicio, Operacao = operacao, Modo = modo, CuboId = cuboId });
        return registro;
    }

    /// <summary>Reabre um registro existente para acrescentar linhas (usado pelo Desfazer).</summary>
    public static RegistroExecucao Reabrir(string caminho)
    {
        var registro = new RegistroExecucao(caminho);
        registro._contador = LeitorRegistro.Ler(caminho).Where(l => l.Tipo == TipoLinha.Acao).Select(l => l.N).DefaultIfEmpty(0).Max();
        return registro;
    }

    /// <summary>Anota a intenção antes de mexer no arquivo. Devolve o número da ação.</summary>
    public int AnotarAcao(TipoAcao acao, string origem, string? destino, string? pastaOrigem, string? pastaDestino,
        long tamanho, string? extra = null)
    {
        int n;
        lock (_trava) n = ++_contador;
        Gravar(new LinhaRegistro
        {
            Tipo = TipoLinha.Acao, N = n, Acao = acao, Origem = origem, Destino = destino,
            PastaOrigem = pastaOrigem, PastaDestino = pastaDestino, Tamanho = tamanho, Extra = extra,
        });
        return n;
    }

    public void AnotarResultado(int n, bool ok, string? erro = null, string? destinoFinal = null, string? extra = null) =>
        Gravar(new LinhaRegistro { Tipo = TipoLinha.Resultado, N = n, Ok = ok, Erro = erro, Destino = destinoFinal, Extra = extra });

    public void AnotarRestauracao(int n, string de, string para, bool ok, string? erro = null) =>
        Gravar(new LinhaRegistro { Tipo = TipoLinha.Restauracao, N = n, Origem = de, Destino = para, Ok = ok, Erro = erro });

    public void AnotarFim() => Gravar(new LinhaRegistro { Tipo = TipoLinha.Fim });

    private void Gravar(LinhaRegistro linha)
    {
        if (linha.Data == default) linha.Data = DateTime.Now;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(linha, Opcoes) + "\n");
        lock (_trava)
        {
            _arquivo.Write(bytes);
            _arquivo.Flush(flushToDisk: true);
        }
    }

    public void Dispose() => _arquivo.Dispose();
}

/// <summary>Uma ação do registro já juntada com o resultado e as restaurações.</summary>
public class ItemRegistro
{
    public int N { get; init; }
    public TipoAcao Acao { get; init; }
    public string Origem { get; init; } = "";
    public string? Destino { get; set; }
    public string? PastaOrigem { get; init; }
    public string? PastaDestino { get; init; }
    public long Tamanho { get; init; }
    public string? Extra { get; set; }
    /// <summary>null = o app parou antes de anotar o resultado (pode ter sido feito ou não).</summary>
    public bool? Ok { get; set; }
    public string? Erro { get; set; }
    public bool Restaurado { get; set; }
    public string? RestauradoPara { get; set; }
}

/// <summary>Lê um registro, aguentando uma última linha cortada pela metade.</summary>
public static class LeitorRegistro
{
    public static List<LinhaRegistro> Ler(string caminho)
    {
        var linhas = new List<LinhaRegistro>();
        if (!File.Exists(caminho)) return linhas;

        using var leitor = new StreamReader(new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8);
        string? texto;
        while ((texto = leitor.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(texto)) continue;
            try
            {
                var linha = JsonSerializer.Deserialize<LinhaRegistro>(texto, RegistroExecucao.Opcoes);
                if (linha is not null) linhas.Add(linha);
            }
            catch (JsonException)
            {
                // Linha cortada (o computador desligou no meio da gravação): as anteriores valem.
            }
        }
        return linhas;
    }

    public static List<ItemRegistro> LerItens(string caminho)
    {
        var itens = new Dictionary<int, ItemRegistro>();
        foreach (var linha in Ler(caminho))
        {
            switch (linha.Tipo)
            {
                case TipoLinha.Acao when linha.Acao is { } acao && linha.Origem is not null:
                    itens[linha.N] = new ItemRegistro
                    {
                        N = linha.N, Acao = acao, Origem = linha.Origem, Destino = linha.Destino,
                        PastaOrigem = linha.PastaOrigem, PastaDestino = linha.PastaDestino,
                        Tamanho = linha.Tamanho ?? 0, Extra = linha.Extra,
                    };
                    break;
                case TipoLinha.Resultado when itens.TryGetValue(linha.N, out var item):
                    item.Ok = linha.Ok;
                    item.Erro = linha.Erro;
                    if (linha.Destino is not null) item.Destino = linha.Destino;
                    if (linha.Extra is not null) item.Extra = linha.Extra;
                    break;
                case TipoLinha.Restauracao when itens.TryGetValue(linha.N, out var restaurado) && linha.Ok == true:
                    restaurado.Restaurado = true;
                    restaurado.RestauradoPara = linha.Destino;
                    break;
            }
        }
        return itens.Values.OrderBy(i => i.N).ToList();
    }

    /// <summary>Contagem de arquivos por categoria (Imagens, Vídeos...), para o preview dos Cubos.</summary>
    public static Dictionary<string, int> ContarPorCategoria(IEnumerable<ItemRegistro> itens) =>
        itens.GroupBy(i => RegrasPadrao.Categoria(Extensoes.DoArquivo(i.Origem)))
            .ToDictionary(g => g.Key, g => g.Count());
}
