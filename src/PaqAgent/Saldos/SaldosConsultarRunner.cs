using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Saldos;

public sealed class SaldosConsultarOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class SaldosConsultarRunner
{
    private readonly ISaldosConsultarSpExecutor saldosConsultarSpExecutor;

    public SaldosConsultarRunner(ISaldosConsultarSpExecutor saldosConsultarSpExecutor)
    {
        this.saldosConsultarSpExecutor = saldosConsultarSpExecutor;
    }

    public async Task<SaldosConsultarOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var codigoCliente = ExtractString(parameters, "codigoCliente");
        var database = ExtractString(parameters, "_database");

        if (string.IsNullOrWhiteSpace(codigoCliente) || string.IsNullOrWhiteSpace(database))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros codigoCliente y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new SaldosConsultarOutcome
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

            var row = await saldosConsultarSpExecutor
                .ExecuteAsync(connectionString, codigoCliente.Trim(), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (row is null)
            {
                return Fail("NOT_FOUND", "Cliente no encontrado.");
            }

            return new SaldosConsultarOutcome
            {
                Status = JobStatuses.Success,
                Data = row
            };
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
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
            _ => raw.ToString()
        };
    }

    private static SaldosConsultarOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
