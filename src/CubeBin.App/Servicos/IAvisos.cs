namespace CubeBin.App.Servicos;

/// <summary>As notificações do sistema (PRD 5.4): início com tempo para cancelar e fim.</summary>
public interface IAvisos
{
    /// <summary>Avisa que a organização vai começar e espera. Devolve true se a pessoa cancelou.</summary>
    Task<bool> AvisarInicioAsync(int segundos, int quantidade);

    /// <summary>Avisa o fim. <paramref name="aoClicar"/> abre o CubeBin no resultado.</summary>
    void AvisarFim(string titulo, string mensagem, Action? aoClicar);

    void Avisar(string titulo, string mensagem);
}
