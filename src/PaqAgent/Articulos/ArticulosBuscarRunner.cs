using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Articulos;

public sealed class ArticulosBuscarOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class ArticulosBuscarRunner
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly IArticulosBuscarSpExecutor articulosBuscarSpExecutor;

    public ArticulosBuscarRunner(IArticulosBuscarSpExecutor articulosBuscarSpExecutor)
    {
        this.articulosBuscarSpExecutor = articulosBuscarSpExecutor;
    }

    public async Task<ArticulosBuscarOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var texto = ExtractString(parameters, "texto");
        var database = ExtractString(parameters, "_database");
        var limit = ExtractLimit(parameters);

        if (string.IsNullOrWhiteSpace(texto) || string.IsNullOrWhiteSpace(database) || limit is null)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros texto, limit y _database son obligatorios (limit 1..100).");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new ArticulosBuscarOutcome
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

            var rows = await articulosBuscarSpExecutor
                .ExecuteAsync(connectionString, texto.Trim(), limit.Value, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            return new ArticulosBuscarOutcome
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

    private static int? ExtractLimit(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("limit", out var raw) || raw is null)
        {
            return DefaultLimit;
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

        return Math.Min(value.Value, MaxLimit);
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

    private static ArticulosBuscarOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
