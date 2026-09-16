using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Articulos;

public sealed class ArticulosObtenerOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class ArticulosObtenerRunner
{
    private readonly IArticulosObtenerSpExecutor articulosObtenerSpExecutor;

    public ArticulosObtenerRunner(IArticulosObtenerSpExecutor articulosObtenerSpExecutor)
    {
        this.articulosObtenerSpExecutor = articulosObtenerSpExecutor;
    }

    public async Task<ArticulosObtenerOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var codigo = ExtractString(parameters, "codigo");
        var database = ExtractString(parameters, "_database");

        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(database))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros codigo y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new ArticulosObtenerOutcome
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

            var row = await articulosObtenerSpExecutor
                .ExecuteAsync(connectionString, codigo.Trim(), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (row is null)
            {
                return Fail("NOT_FOUND", "Articulo no encontrado.");
            }

            return new ArticulosObtenerOutcome
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

    private static ArticulosObtenerOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
