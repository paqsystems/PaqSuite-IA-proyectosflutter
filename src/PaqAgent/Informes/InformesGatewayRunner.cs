using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Informes;

public sealed class InformesOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IInformesSpExecutor
{
    Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
        string connectionString,
        string storedProcedure,
        IReadOnlyDictionary<string, object?> spParameters,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}

public sealed class SqlInformesSpExecutor : IInformesSpExecutor
{
    public async Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
        string connectionString,
        string storedProcedure,
        IReadOnlyDictionary<string, object?> spParameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(storedProcedure, connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };

        foreach (var (key, value) in spParameters)
        {
            command.Parameters.Add(CreateParameter(key, value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var allResultSets = new List<IReadOnlyList<Dictionary<string, object?>>>();
        do
        {
            allResultSets.Add(await ReadResultSetAsync(reader, cancellationToken).ConfigureAwait(false));
        }
        while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

        return allResultSets;
    }

    private static SqlParameter CreateParameter(string name, object? value)
    {
        var paramName = name.StartsWith('@') ? name : "@" + name;
        if (value is null)
        {
            return new SqlParameter(paramName, DBNull.Value);
        }

        return value switch
        {
            bool b => new SqlParameter(paramName, System.Data.SqlDbType.Bit) { Value = b },
            int i => new SqlParameter(paramName, System.Data.SqlDbType.Int) { Value = i },
            long l => new SqlParameter(paramName, System.Data.SqlDbType.BigInt) { Value = l },
            decimal d => new SqlParameter(paramName, System.Data.SqlDbType.Decimal) { Value = d },
            double dbl => new SqlParameter(paramName, System.Data.SqlDbType.Float) { Value = dbl },
            DateTime dt => new SqlParameter(paramName, System.Data.SqlDbType.DateTime) { Value = dt },
            _ => new SqlParameter(paramName, System.Data.SqlDbType.NVarChar, -1) { Value = value.ToString() }
        };
    }

    private static async Task<IReadOnlyList<Dictionary<string, object?>>> ReadResultSetAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}

public sealed class InformesGatewayRunner
{
    private static readonly HashSet<string> BitParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "ignorar_saldo_cero"
    };

    private readonly IInformesSpExecutor informesSpExecutor;

    public InformesGatewayRunner(IInformesSpExecutor informesSpExecutor)
    {
        this.informesSpExecutor = informesSpExecutor;
    }

    public async Task<InformesOutcome> RunAsync(
        InformesOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        if (string.IsNullOrWhiteSpace(database))
        {
            return Fail("INVALID_PARAMETERS", "El parametro _database es obligatorio para ops company (informes.*/robinet.*)");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new InformesOutcome
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

            var spParams = MapSpParameters(definition, parameters);
            var resultSets = await informesSpExecutor
                .ExecuteAsync(
                    connectionString,
                    definition.StoredProcedure,
                    spParams,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            var totalesRow = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
            var totalFilas = ReadInt(totalesRow, "total_filas") ?? 0;
            var totalGeneral = ReadDecimal(totalesRow, "total_general");
            var filas = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
            var page = ReadIntFromParams(parameters, "page") ?? 1;
            var pageSize = ReadIntFromParams(parameters, "page_size") ?? 200;

            var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["total_filas"] = totalFilas,
                ["page"] = page,
                ["page_size"] = pageSize,
                ["filas"] = filas
            };
            if (totalGeneral is not null)
            {
                data["total_general"] = totalGeneral;
            }

            return new InformesOutcome
            {
                Status = JobStatuses.Success,
                Data = data
            };
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Dictionary<string, object?> MapSpParameters(
        InformesOperationDefinition definition,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in definition.Parameters)
        {
            if (!parameters.TryGetValue(name, out var raw))
            {
                continue;
            }

            result[name] = BitParameters.Contains(name)
                ? ConvertBit(raw)
                : ConvertValue(raw);
        }

        return result;
    }

    private static object? ConvertValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number => element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        return value;
    }

    private static object ConvertBit(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => element.TryGetInt32(out var n) && n != 0,
                JsonValueKind.String when bool.TryParse(element.GetString(), out var b) => b,
                JsonValueKind.String when int.TryParse(element.GetString(), out var i) => i != 0,
                _ => false
            };
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return Convert.ToInt32(value) != 0;
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

    private static int? ReadIntFromParams(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return ConvertValue(raw) switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static int? ReadInt(Dictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToInt32(value);
    }

    private static decimal? ReadDecimal(Dictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToDecimal(value);
    }

    private static InformesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
