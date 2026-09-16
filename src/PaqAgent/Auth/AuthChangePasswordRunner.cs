using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Auth;

public sealed class AuthChangePasswordOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AuthChangePasswordRunner
{
    private const string StatusOk = "OK";
    private const string StatusNotFound = "NOT_FOUND";
    private const string StatusInvalidParameters = "INVALID_PARAMETERS";
    private const string StatusSqlError = "SQL_ERROR";

    private readonly IAuthChangePasswordSpExecutor authChangePasswordSpExecutor;

    public AuthChangePasswordRunner(IAuthChangePasswordSpExecutor authChangePasswordSpExecutor)
    {
        this.authChangePasswordSpExecutor = authChangePasswordSpExecutor;
    }

    public async Task<AuthChangePasswordOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var userId = ExtractInt(parameters, "user_id", "userId");
        var codigo = ExtractString(parameters, "codigo", "Codigo");
        var passwordHash = ExtractString(parameters, "password_hash", "passwordHash");

        if (userId is null or <= 0
            || string.IsNullOrWhiteSpace(codigo)
            || string.IsNullOrWhiteSpace(passwordHash)
            || passwordHash.Length < 20)
        {
            return Fail(StatusInvalidParameters, "Los parametros user_id, codigo y password_hash son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new AuthChangePasswordOutcome
            {
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        try
        {
            var connectionString = SqlConnectionStringFactory.Build(agentOptions.Sql, connectTimeoutSeconds: 15);
            var resultSets = await authChangePasswordSpExecutor
                .ExecuteAsync(
                    connectionString,
                    userId.Value,
                    codigo,
                    passwordHash,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            if (resultSets.Count == 0 || resultSets[0].Count == 0)
            {
                return Fail(StatusSqlError, "El procedimiento de cambio de contrasena no devolvio datos.");
            }

            var header = resultSets[0][0];
            var status = GetString(header, "status") ?? string.Empty;

            return status.ToUpperInvariant() switch
            {
                StatusOk => new AuthChangePasswordOutcome
                {
                    Status = JobStatuses.Success,
                    Data = new Dictionary<string, object?> { ["status"] = StatusOk }
                },
                StatusNotFound => Fail(StatusNotFound, "Usuario no encontrado."),
                StatusInvalidParameters => Fail(
                    StatusInvalidParameters,
                    GetString(header, "error_message")
                    ?? "Parametros invalidos para auth.changePassword."),
                StatusSqlError => Fail(
                    StatusSqlError,
                    GetString(header, "error_message")
                    ?? "Error interno al cambiar la contrasena."),
                _ => Fail("INTERNAL_ERROR", $"Estado de cambio de contrasena no reconocido: {status}")
            };
        }
        catch (Exception ex)
        {
            return new AuthChangePasswordOutcome
            {
                Status = JobStatuses.Failed,
                ErrorCode = StatusSqlError,
                ErrorMessage = ex.GetType().Name + ": " + ex.Message
            };
        }
    }

    private static AuthChangePasswordOutcome Fail(string errorCode, string errorMessage) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    private static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, params string[] keys)
    {
        var raw = ExtractRaw(parameters, keys);
        return raw switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.TryGetInt32(out var n) ? n : null,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => int.TryParse(raw.ToString(), out var fromObject) ? fromObject : null
        };
    }

    private static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, params string[] keys)
    {
        var raw = ExtractRaw(parameters, keys);
        return raw switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement je => je.ToString(),
            _ => raw.ToString()
        };
    }

    private static object? ExtractRaw(IReadOnlyDictionary<string, object?> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.ToString() : null;
}
