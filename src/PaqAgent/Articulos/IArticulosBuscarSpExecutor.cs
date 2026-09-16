namespace PaqAgent.Articulos;

public interface IArticulosBuscarSpExecutor
{
    Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string texto,
        int limit,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
