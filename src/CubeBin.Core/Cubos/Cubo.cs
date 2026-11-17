using System.Text.Json;
using System.Text.Json.Serialization;
using CubeBin.Core.Configuracao;

namespace CubeBin.Core.Cubos;

/// <summary>Como o Cubo nasceu.</summary>
public enum TipoCubo
{
    /// <summary>Organizar Arquivos: os arquivos foram para as pastas por tipo.</summary>
    Organizado,
    /// <summary>Liberar Espaço → Lixeira do sistema.</summary>
    Lixeira,
    /// <summary>Liberar Espaço → Apagar de vez (não há como restaurar).</summary>
    ApagadoDeVez,
    /// <summary>Liberar Espaço → pacote compactado no Google Drive.</summary>
    Nuvem,
}

/// <summary>
/// Um "Cubo" é o resultado de uma execução: os arquivos que foram organizados, para a Lixeira ou
/// para a nuvem. Guarda o resumo para a tela (preview por tipo ao passar o mouse) e onde estão o
/// registro JSON (para Restaurar) e o relatório HTML.
/// </summary>
public class Cubo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TipoCubo Tipo { get; set; }

    public DateTime Data { get; set; } = DateTime.Now;

    /// <summary>"manual" ou "automatico".</summary>
    public string Modo { get; set; } = "manual";

    public int Arquivos { get; set; }
    public long Bytes { get; set; }
    public int Falhas { get; set; }

    /// <summary>Quantos arquivos de cada tipo: { "Imagens": 12, "Documentos": 3 }.</summary>
    public Dictionary<string, int> PorCategoria { get; set; } = new();

    /// <summary>De quais pastas os arquivos saíram (nomes amigáveis).</summary>
    public List<string> PastasDeOrigem { get; set; } = new();

    public string? CaminhoRegistro { get; set; }
    public string? CaminhoRelatorio { get; set; }

    public bool Restaurado { get; set; }
    public DateTime? RestauradoEm { get; set; }

    // ----- Nuvem (2.0) -----
    public string? NomePacote { get; set; }
    public string? IdPacoteNuvem { get; set; }
    public string? LinkNuvem { get; set; }
    /// <summary>O envio não pôde ser feito (sem internet, Drive cheio...): os arquivos continuam no computador.</summary>
    public bool EnvioAdiado { get; set; }
    public string? MotivoAdiamento { get; set; }

    [JsonIgnore]
    public bool PodeRestaurar => Tipo != TipoCubo.ApagadoDeVez && !Restaurado && !EnvioAdiado;

    [JsonIgnore]
    public string Titulo => Tipo switch
    {
        TipoCubo.Organizado => Modo == "automatico" ? "Organização automática" : "Organização",
        TipoCubo.Lixeira => "Enviado para a Lixeira",
        TipoCubo.ApagadoDeVez => "Apagado de vez",
        TipoCubo.Nuvem => EnvioAdiado ? "Envio para a nuvem adiado" : "Enviado para a nuvem",
        _ => "Cubo",
    };

    /// <summary>"12 Imagens · 3 Documentos" (o preview ao passar o mouse).</summary>
    [JsonIgnore]
    public string ResumoPorTipo => PorCategoria.Count == 0
        ? "Nenhum arquivo"
        : string.Join(" · ", PorCategoria.OrderByDescending(p => p.Value).Select(p => $"{p.Value} {p.Key}"));
}

/// <summary>
/// O índice dos Cubos (<c>cubos.json</c>), lido pela tela inicial e pela tela de Cubos sem precisar
/// abrir registro nenhum. Gravado inteiro num temporário e trocado no fim.
/// </summary>
public class CuboService
{
    private static readonly JsonSerializerOptions Opcoes = new(ConfiguracaoService.OpcoesJson);

    private readonly object _trava = new();
    private List<Cubo>? _cubos;

    public string Caminho { get; }

    public event Action? Alterado;

    public CuboService(string caminho) => Caminho = caminho;

    public List<Cubo> Listar()
    {
        lock (_trava)
        {
            _cubos ??= LerDoDisco();
            return _cubos.OrderByDescending(c => c.Data).ToList();
        }
    }

    public Cubo? Obter(string id) => Listar().FirstOrDefault(c => c.Id == id);

    public void Salvar(Cubo cubo)
    {
        lock (_trava)
        {
            _cubos ??= LerDoDisco();
            _cubos.RemoveAll(c => c.Id == cubo.Id);
            _cubos.Add(cubo);
            GravarNoDisco(_cubos);
        }
        Alterado?.Invoke();
    }

    private List<Cubo> LerDoDisco()
    {
        try
        {
            if (File.Exists(Caminho))
                return JsonSerializer.Deserialize<List<Cubo>>(File.ReadAllText(Caminho), Opcoes) ?? new();
        }
        catch (Exception)
        {
            try { File.Copy(Caminho, $"{Caminho}.quebrado-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: false); } catch { }
        }
        return new();
    }

    private void GravarNoDisco(List<Cubo> cubos)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        var temporario = Caminho + ".gravando";
        File.WriteAllText(temporario, JsonSerializer.Serialize(cubos.OrderByDescending(c => c.Data), Opcoes));
        File.Move(temporario, Caminho, overwrite: true);
    }
}
