using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Auth;

public sealed class AuthLoginOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AuthLoginRunner
{
    private const string StatusOk = "OK";
    private const string StatusNotFound = "NOT_FOUND";
    private const string StatusInactive = "INACTIVE";
    private const string StatusNoEmpresas = "NO_EMPRESAS";
    private const string StatusSqlError = "SQL_ERROR";

    private readonly IAuthLoginSpExecutor authLoginSpExecutor;

    public AuthLoginRunner(IAuthLoginSpExecutor authLoginSpExecutor)
    {
        this.authLoginSpExecutor = authLoginSpExecutor;
    }

    public async Task<AuthLoginOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var codigo = ExtractCodigo(parameters);
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return new AuthLoginOutcome
            {
                Status = JobStatuses.Failed,
                ErrorCode = "INVALID_PARAMETERS",
                ErrorMessage = "El parametro codigo es obligatorio."
            };
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new AuthLoginOutcome
            {
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        try
        {
            var connectionString = BuildConnectionString(agentOptions.Sql);
            var resultSets = await authLoginSpExecutor
                .ExecuteAsync(connectionString, codigo, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (resultSets.Count == 0 || resultSets[0].Count == 0)
            {
                return new AuthLoginOutcome
                {
                    Status = JobStatuses.Failed,
                    ErrorCode = StatusSqlError,
                    ErrorMessage = "El procedimiento de login no devolvio datos."
                };
            }

            var header = resultSets[0][0];
            var status = GetString(header, "status") ?? string.Empty;

            return status.ToUpperInvariant() switch
            {
                StatusOk => new AuthLoginOutcome
                {
                    Status = JobStatuses.Success,
                    Data = BuildSuccessPayload(header, resultSets)
                },
                StatusNotFound => Fail(StatusNotFound, "Credenciales invalidas."),
                StatusInactive => Fail(StatusInactive, "Usuario inactivo."),
                StatusNoEmpresas => Fail(
                    StatusNoEmpresas,
                    "No tiene empresas asignadas. Contacte al administrador."),
                StatusSqlError => Fail(
                    StatusSqlError,
                    GetString(header, "error_message")
                    ?? "Error interno al procesar la solicitud de autenticacion."),
                _ => Fail("INTERNAL_ERROR", $"Estado de login no reconocido: {status}")
            };
        }
        catch (Exception ex)
        {
            return new AuthLoginOutcome
            {
                Status = JobStatuses.Failed,
                ErrorCode = StatusSqlError,
                ErrorMessage = ex.GetType().Name + ": " + ex.Message
            };
        }
    }

    private static AuthLoginOutcome Fail(string errorCode, string errorMessage) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    private static Dictionary<string, object?> BuildSuccessPayload(
        IReadOnlyDictionary<string, object?> header,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var empresas = resultSets.Count > 1
            ? resultSets[1].Select(MapEmpresaRow).ToList()
            : new List<Dictionary<string, object?>>();

        return new Dictionary<string, object?>
        {
            ["status"] = StatusOk,
            ["user"] = new Dictionary<string, object?>
            {
                ["id"] = GetValue(header, "user_id"),
                ["codigo"] = GetString(header, "codigo"),
                ["name_user"] = GetString(header, "name_user"),
                ["email"] = GetString(header, "email"),
                ["password_hash"] = GetString(header, "password_hash"),
                ["locale"] = GetString(header, "locale") ?? "es",
                ["menu_abrir_nueva_pestana"] = ToBool(GetValue(header, "menu_abrir_nueva_pestana")),
                ["sidebar_collapsed"] = ToBool(GetValue(header, "sidebar_collapsed"))
            },
            ["es_admin"] = ToBool(GetValue(header, "es_admin")),
            ["redirectTo"] = GetString(header, "redirectTo"),
            ["empresas"] = empresas,
            ["error_message"] = null
        };
    }

    private static Dictionary<string, object?> MapEmpresaRow(IReadOnlyDictionary<string, object?> row) =>
        new()
        {
            ["id"] = GetValue(row, "id"),
            ["nombreEmpresa"] = GetString(row, "nombreEmpresa"),
            ["nombreBd"] = GetString(row, "nombreBd"),
            ["theme"] = GetString(row, "theme") ?? "default",
            ["imagen"] = GetString(row, "imagen")
        };

    private static string BuildConnectionString(SqlOptions sql) =>
        SqlConnectionStringFactory.Build(sql, connectTimeoutSeconds: 15);

    private static string? ExtractCodigo(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("codigo", out var raw) && !parameters.TryGetValue("Codigo", out raw))
        {
            return null;
        }

        return NormalizeParameterValue(raw);
    }

    private static string? NormalizeParameterValue(object? raw) =>
        raw switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement je => je.ToString(),
            _ => raw.ToString()
        };

    private static object? GetValue(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(IReadOnlyDictionary<string, object?> row, string key) =>
        GetValue(row, key)?.ToString();

    private static bool ToBool(object? value) => value switch
    {
        bool b => b,
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value) != 0,
        _ => false
    };
}
