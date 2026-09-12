using System.Text.Json;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Menu;

public sealed class MenuAuthorizedOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class MenuAuthorizedRunner
{
    private const string StatusOk = "OK";
    private const string StatusInvalidParameters = "INVALID_PARAMETERS";
    private const string StatusSqlError = "SQL_ERROR";

    private readonly IMenuAuthorizedSpExecutor menuAuthorizedSpExecutor;

    public MenuAuthorizedRunner(IMenuAuthorizedSpExecutor menuAuthorizedSpExecutor)
    {
        this.menuAuthorizedSpExecutor = menuAuthorizedSpExecutor;
    }

    public async Task<MenuAuthorizedOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var userId = ExtractInt(parameters, "user_id", "userId");
        var empresaId = ExtractInt(parameters, "empresa_id", "empresaId");

        if (userId is null or <= 0 || empresaId is null or <= 0)
        {
            return new MenuAuthorizedOutcome
            {
                Status = JobStatuses.Failed,
                ErrorCode = StatusInvalidParameters,
                ErrorMessage = "Los parametros user_id y empresa_id son obligatorios y deben ser > 0."
            };
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new MenuAuthorizedOutcome
            {
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        try
        {
            var connectionString = SqlConnectionStringFactory.Build(agentOptions.Sql, connectTimeoutSeconds: 15);
            var resultSets = await menuAuthorizedSpExecutor
                .ExecuteAsync(connectionString, userId.Value, empresaId.Value, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (resultSets.Count == 0 || resultSets[0].Count == 0)
            {
                return Fail(StatusSqlError, "El procedimiento de menu no devolvio datos.");
            }

            var header = resultSets[0][0];
            var status = GetString(header, "status") ?? string.Empty;

            return status.ToUpperInvariant() switch
            {
                StatusOk => new MenuAuthorizedOutcome
                {
                    Status = JobStatuses.Success,
                    Data = BuildSuccessPayload(header, resultSets, empresaId.Value)
                },
                StatusInvalidParameters => Fail(
                    StatusInvalidParameters,
                    GetString(header, "error_message")
                    ?? "Parametros invalidos para menu.authorized."),
                StatusSqlError => Fail(
                    StatusSqlError,
                    GetString(header, "error_message")
                    ?? "Error interno al resolver el menu autorizado."),
                _ => Fail("INTERNAL_ERROR", $"Estado de menu no reconocido: {status}")
            };
        }
        catch (Exception ex)
        {
            return new MenuAuthorizedOutcome
            {
                Status = JobStatuses.Failed,
                ErrorCode = StatusSqlError,
                ErrorMessage = ex.GetType().Name + ": " + ex.Message
            };
        }
    }

    private static MenuAuthorizedOutcome Fail(string errorCode, string errorMessage) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    private static Dictionary<string, object?> BuildSuccessPayload(
        IReadOnlyDictionary<string, object?> header,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets,
        int empresaIdFallback)
    {
        var empresaId = ToInt(GetValue(header, "empresa_id")) ?? empresaIdFallback;
        var accesoTotal = ToBool(GetValue(header, "acceso_total"));

        var items = resultSets.Count > 1
            ? resultSets[1].Select(MapItemRow).ToList()
            : new List<Dictionary<string, object?>>();

        var procedimientos = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var proc = item.TryGetValue("procedimiento", out var raw) ? raw?.ToString()?.Trim() : null;
            if (!string.IsNullOrEmpty(proc) && seen.Add(proc))
            {
                procedimientos.Add(proc);
            }
        }

        return new Dictionary<string, object?>
        {
            ["empresaId"] = empresaId,
            ["empresa_id"] = empresaId,
            ["accesoTotal"] = accesoTotal,
            ["acceso_total"] = accesoTotal,
            ["items"] = items,
            ["procedimientos"] = procedimientos
        };
    }

    private static Dictionary<string, object?> MapItemRow(IReadOnlyDictionary<string, object?> row)
    {
        var parentRaw = GetValue(row, "parentId") ?? GetValue(row, "parent_id");
        var parentId = ToInt(parentRaw);
        if (parentId is null or <= 0)
        {
            parentId = null;
        }

        return new Dictionary<string, object?>
        {
            ["id"] = ToInt(GetValue(row, "id")) ?? 0,
            ["text"] = GetString(row, "text") ?? string.Empty,
            ["parentId"] = parentId,
            ["orden"] = ToInt(GetValue(row, "orden") ?? GetValue(row, "order")) ?? 0,
            ["routeName"] = GetString(row, "routeName") ?? GetString(row, "route_name"),
            ["procedimiento"] = GetString(row, "procedimiento"),
            ["icon_name"] = GetString(row, "icon_name") ?? GetString(row, "iconName"),
            ["tipo_proceso"] = GetString(row, "tipo_proceso") ?? GetString(row, "tipoProceso")
        };
    }

    private static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            var parsed = ToInt(raw);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ToInt(object? value) => value switch
    {
        null => null,
        int i => i,
        long l => checked((int)l),
        short s => s,
        byte b => b,
        decimal d => (int)d,
        double dbl => (int)dbl,
        float f => (int)f,
        string s when int.TryParse(s, out var parsed) => parsed,
        JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt32(out var n) => n,
        JsonElement { ValueKind: JsonValueKind.String } je when int.TryParse(je.GetString(), out var n) => n,
        _ => int.TryParse(value.ToString(), out var fallback) ? fallback : null
    };

    private static object? GetValue(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(IReadOnlyDictionary<string, object?> row, string key) =>
        GetValue(row, key)?.ToString();

    private static bool ToBool(object? value) => value switch
    {
        bool b => b,
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToInt64(value) != 0,
        string s when bool.TryParse(s, out var parsed) => parsed,
        string s when int.TryParse(s, out var n) => n != 0,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        JsonElement { ValueKind: JsonValueKind.Number } je => je.TryGetInt64(out var n) && n != 0,
        _ => false
    };
}
