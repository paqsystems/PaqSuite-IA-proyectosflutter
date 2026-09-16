namespace PaqAgent.Auth;

/// <summary>Ejecuta PAQ_Auth_ChangePassword y devuelve el result set de status.</summary>
public interface IAuthChangePasswordSpExecutor
{
    Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
        string connectionString,
        int userId,
        string codigo,
        string passwordHash,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
