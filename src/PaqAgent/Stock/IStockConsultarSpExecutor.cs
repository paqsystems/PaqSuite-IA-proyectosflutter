namespace PaqAgent.Stock;

public interface IStockConsultarSpExecutor
{
    Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string codigoArticulo,
        string? deposito,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
