namespace PaqAgent.Articulos;

public interface IArticulosObtenerSpExecutor
{
    Task<Dictionary<string, object?>?> ExecuteAsync(
        string connectionString,
        string codigo,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
