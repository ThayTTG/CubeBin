namespace CubeBin.Core.Plataforma;

/// <summary>A Lixeira do sistema (Windows ou Linux), usada pelo Liberar Espaço.</summary>
public interface ILixeira
{
    /// <summary>Manda o arquivo ou a pasta para a Lixeira. Devolve uma "chave" para restaurar depois.</summary>
    string Enviar(string caminho);

    /// <summary>O item ainda está na Lixeira (a pessoa não esvaziou a Lixeira nem restaurou à mão)?</summary>
    bool Existe(string chave);

    /// <summary>Devolve o item para <paramref name="destino"/> (que não pode existir).</summary>
    void Restaurar(string chave, string destino);
}
