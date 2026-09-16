namespace PaqAgent.Clientes;

public interface IClientesObtenerSpExecutor
{
    Task<Dictionary<string, object?>?> ExecuteAsync(
        string connectionString,
        string codigo,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
