using CubeBin.Core.Models;

namespace CubeBin.Core.Services;

public enum ResultadoClassificacao
{
    Classificado,
    SemRegra
}

/// <summary>SubPasta é opcional: quando presente, o arquivo vai para PastaDestino\SubPasta (item 1 do feedback de uso).
/// TipoDestino/CaminhoDrive dizem se o destino é uma pasta local (padrão) ou o Google Drive.</summary>
public record ClassificacaoArquivo(
    ResultadoClassificacao Resultado,
    string? PastaDestino,
    string? ChaveRegra,
    string? SubPasta,
    TipoDestino TipoDestino,
    string? CaminhoDrive);

/// <summary>
/// Decide, para cada arquivo, se existe uma RegraExtensao correspondente (seção 3, passo 7 da spec).
/// Arquivos sem regra retornam SemRegra e devem ir para a fila de pendências (ver PendenciaService).
/// </summary>
public class ClassificadorService
{
    public ClassificacaoArquivo Classificar(FileInfo arquivo, AppConfig config, PastaMonitorada pasta)
    {
        var extensao = arquivo.Extension.ToLowerInvariant();
        var regras = (pasta.RegrasPersonalizadas is { Count: > 0 } ? pasta.RegrasPersonalizadas : config.RegrasExtensao).Values;

        foreach (var regra in regras)
        {
            if (!regra.Extensoes.Any(e => e.Equals(extensao, StringComparison.OrdinalIgnoreCase)))
                continue;

            var subpasta = regra.Subpastas.TryGetValue(extensao, out var valor) && !string.IsNullOrWhiteSpace(valor)
                ? valor
                : null;

            return new ClassificacaoArquivo(
                ResultadoClassificacao.Classificado, regra.Destino, regra.Chave, subpasta,
                regra.TipoDestino, regra.CaminhoDrive);
        }

        return new ClassificacaoArquivo(ResultadoClassificacao.SemRegra, null, null, null, TipoDestino.Local, null);
    }

    /// <summary>Resolve o caminho completo de destino, incluindo a subpasta opcional (item 1 do feedback de uso).</summary>
    public string ResolverCaminhoDestino(string caminhoPastaOrigem, ClassificacaoArquivo classificacao)
    {
        return string.IsNullOrWhiteSpace(classificacao.SubPasta)
            ? Path.Combine(caminhoPastaOrigem, classificacao.PastaDestino!)
            : Path.Combine(caminhoPastaOrigem, classificacao.PastaDestino!, classificacao.SubPasta);
    }

    /// <summary>
    /// Resolve conflito de nome no destino aplicando o prefixo da pasta de origem (seção 9 da spec).
    /// Ex.: "relatorio.pdf" vindo de Documentos -> "Doc-relatorio.pdf".
    /// </summary>
    public string ResolverNomeDestino(string nomeArquivo, string caminhoDestino, PastaMonitorada pastaOrigem)
    {
        var caminhoCompleto = Path.Combine(caminhoDestino, nomeArquivo);
        if (!File.Exists(caminhoCompleto))
            return nomeArquivo;

        var prefixo = pastaOrigem.ObterPrefixoEfetivo();
        var nomeComPrefixo = $"{prefixo}-{nomeArquivo}";

        // Em caso de novo conflito (raro), adiciona um sufixo numérico incremental.
        var candidato = nomeComPrefixo;
        var contador = 1;
        while (File.Exists(Path.Combine(caminhoDestino, candidato)))
        {
            var semExtensao = Path.GetFileNameWithoutExtension(nomeComPrefixo);
            var extensao = Path.GetExtension(nomeComPrefixo);
            candidato = $"{semExtensao} ({contador}){extensao}";
            contador++;
        }

        return candidato;
    }
}
