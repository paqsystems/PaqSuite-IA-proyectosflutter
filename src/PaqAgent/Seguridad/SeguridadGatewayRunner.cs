using System.Text.Json;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Seguridad;

public sealed class SeguridadOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class SeguridadGatewayRunner
{
    private static readonly HashSet<string> BitParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "activo",
        "inhabilitado",
    };

    private readonly IInformesSpExecutor dictionarySpExecutor;

    public SeguridadGatewayRunner(IInformesSpExecutor dictionarySpExecutor)
    {
        this.dictionarySpExecutor = dictionarySpExecutor;
    }

    public async Task<SeguridadOutcome> RunAsync(
        SeguridadOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!agentOptions.HasSqlConfig)
        {
            return new SeguridadOutcome
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
                connectTimeoutSeconds: 15);

            var spParams = MapSpParameters(definition, parameters);
            var resultSets = await dictionarySpExecutor
                .ExecuteAsync(
                    connectionString,
                    definition.StoredProcedure,
                    spParams,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            var rows = resultSets.FirstOrDefault() ?? Array.Empty<Dictionary<string, object?>>();
            var items = rows
                .Select(row => new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return new SeguridadOutcome
            {
                Status = JobStatuses.Success,
                Data = new Dictionary<string, object?> { ["items"] = items }
            };
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Dictionary<string, object?> MapSpParameters(
        SeguridadOperationDefinition definition,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var paramName in definition.Parameters)
        {
            var value = ExtractParameter(parameters, paramName);
            if (value is null)
            {
                continue;
            }

            if (BitParameters.Contains(paramName))
            {
                mapped[ToSpParameterName(paramName)] = ToBool(value);
                continue;
            }

            if (paramName.StartsWith("id_", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ToInt(value);
                if (parsed is > 0)
                {
                    mapped[ToSpParameterName(paramName)] = parsed;
                }

                continue;
            }

            var text = value.ToString()?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                mapped[ToSpParameterName(paramName)] = text;
            }
        }

        return mapped;
    }

    private static object? ExtractParameter(IReadOnlyDictionary<string, object?> parameters, string paramName)
    {
        if (parameters.TryGetValue(paramName, out var direct) && direct is not null)
        {
            return direct;
        }

        var camel = ToCamelCase(paramName);
        if (parameters.TryGetValue(camel, out var camelValue) && camelValue is not null)
        {
            return camelValue;
        }

        return null;
    }

    private static string ToSpParameterName(string paramName) =>
        "@" + string.Join(
            string.Empty,
            paramName.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string ToCamelCase(string snake) =>
        string.Join(
            string.Empty,
            snake.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select((part, index) => index == 0
                    ? part
                    : char.ToUpperInvariant(part[0]) + part[1..]));

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

    private static SeguridadOutcome Fail(string errorCode, string errorMessage) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}
