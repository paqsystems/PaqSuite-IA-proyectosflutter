namespace PaqAgent.Clientes;

public interface IClientesBuscarSpExecutor
{
    Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string texto,
        int limit,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
