using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Comprobantes;

public sealed class ComprobantesRecientesOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class ComprobantesRecientesRunner
{
    private const int DefaultDias = 30;
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;
    private const int MaxDias = 3650;

    private readonly IComprobantesRecientesSpExecutor comprobantesRecientesSpExecutor;

    public ComprobantesRecientesRunner(IComprobantesRecientesSpExecutor comprobantesRecientesSpExecutor)
    {
        this.comprobantesRecientesSpExecutor = comprobantesRecientesSpExecutor;
    }

    public async Task<ComprobantesRecientesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var codigoCliente = ExtractString(parameters, "codigoCliente");
        var database = ExtractString(parameters, "_database");
        var dias = ExtractInt(parameters, "dias", DefaultDias, MaxDias);
        var limit = ExtractInt(parameters, "limit", DefaultLimit, MaxLimit);

        if (string.IsNullOrWhiteSpace(codigoCliente) || string.IsNullOrWhiteSpace(database)
            || dias is null || limit is null)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros codigoCliente, dias, limit y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new ComprobantesRecientesOutcome
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

            var rows = await comprobantesRecientesSpExecutor
                .ExecuteAsync(
                    connectionString,
                    codigoCliente.Trim(),
                    dias.Value,
                    limit.Value,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            return new ComprobantesRecientesOutcome
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

    private static int? ExtractInt(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        int defaultValue,
        int maxValue)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        var value = raw switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            decimal m => (int)m,
            string s when int.TryParse(s, out var parsed) => parsed,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n) => n,
            JsonElement je when je.ValueKind == JsonValueKind.String
                && int.TryParse(je.GetString(), out var ns) => ns,
            _ => (int?)null
        };

        if (value is null or < 1)
        {
            return null;
        }

        return Math.Min(value.Value, maxValue);
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

    private static ComprobantesRecientesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
