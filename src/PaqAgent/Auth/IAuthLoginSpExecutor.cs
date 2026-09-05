namespace PaqAgent.Auth;

/// <summary>Ejecuta PAQ_Auth_Login y devuelve result sets tipados como filas/columnas.</summary>
public interface IAuthLoginSpExecutor
{
    Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
        string connectionString,
        string codigo,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
