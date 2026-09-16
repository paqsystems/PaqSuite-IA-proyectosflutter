namespace PaqAgent.Saldos;

public interface ISaldosConsultarSpExecutor
{
    Task<Dictionary<string, object?>?> ExecuteAsync(
        string connectionString,
        string codigoCliente,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
