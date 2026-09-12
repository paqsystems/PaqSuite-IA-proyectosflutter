namespace PaqAgent.Menu;

/// <summary>Ejecuta PAQ_User_Menu_Authorized y devuelve result sets tipados como filas/columnas.</summary>
public interface IMenuAuthorizedSpExecutor
{
    Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
        string connectionString,
        int userId,
        int empresaId,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
