namespace PaqAgent.Comprobantes;

public interface IComprobantesRecientesSpExecutor
{
    Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string codigoCliente,
        int dias,
        int limit,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
