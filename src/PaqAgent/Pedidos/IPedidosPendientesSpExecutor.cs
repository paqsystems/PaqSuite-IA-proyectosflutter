namespace PaqAgent.Pedidos;

public interface IPedidosPendientesSpExecutor
{
    Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string codigoCliente,
        int limit,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
