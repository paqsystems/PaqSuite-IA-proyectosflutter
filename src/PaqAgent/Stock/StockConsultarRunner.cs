using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Stock;

public sealed class StockConsultarOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class StockConsultarRunner
{
    private readonly IStockConsultarSpExecutor stockConsultarSpExecutor;

    public StockConsultarRunner(IStockConsultarSpExecutor stockConsultarSpExecutor)
    {
        this.stockConsultarSpExecutor = stockConsultarSpExecutor;
    }

    public async Task<StockConsultarOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var codigoArticulo = ExtractString(parameters, "codigoArticulo");
        var database = ExtractString(parameters, "_database");
        var deposito = ExtractOptionalString(parameters, "deposito");

        if (string.IsNullOrWhiteSpace(codigoArticulo) || string.IsNullOrWhiteSpace(database))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros codigoArticulo y _database son obligatorios (deposito opcional).");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new StockConsultarOutcome
            {
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        try
        {
            var connectionString = SqlConnectionStringFactory.Build(
                agentOptions.Sql,
                connectTimeoutSeconds: 15,
                databaseOverride: database);

            var rows = await stockConsultarSpExecutor
                .ExecuteAsync(
                    connectionString,
                    codigoArticulo.Trim(),
                    deposito,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            return new StockConsultarOutcome
            {
                Status = JobStatuses.Success,
                Data = rows
            };
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string? ExtractOptionalString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        var value = ExtractString(parameters, key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je when je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => raw.ToString()
        };
    }

    private static StockConsultarOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
