using System.Text;
using System.Text.Json;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.MovimientosTesoreria;

public sealed class MovimientosTesoreriaOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class MovimientosTesoreriaGatewayRunner
{
    private readonly IInformesSpExecutor companySpExecutor;

    public MovimientosTesoreriaGatewayRunner(IInformesSpExecutor companySpExecutor)
    {
        this.companySpExecutor = companySpExecutor;
    }

    public async Task<MovimientosTesoreriaOutcome> RunAsync(
        MovimientosTesoreriaOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var codComp = ExtractString(parameters, "cod_comp");
        var nComp = ExtractString(parameters, "n_comp");
        var barra = ExtractInt(parameters, "barra") ?? 0;

        if (string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(codComp)
            || string.IsNullOrWhiteSpace(nComp))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros cod_comp, n_comp y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new MovimientosTesoreriaOutcome
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

            var spParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cod_comp"] = codComp.Trim().ToUpperInvariant(),
                ["n_comp"] = nComp.Trim(),
                ["barra"] = barra
            };

            var resultSets = await companySpExecutor
                .ExecuteAsync(
                    connectionString,
                    definition.StoredProcedure,
                    spParams,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            var cabecera = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
            if (cabecera is null)
            {
                return Fail("NOT_FOUND", "Movimiento de tesoreria no encontrado.");
            }

            return Ok(BuildDetalle(cabecera, resultSets));
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static Dictionary<string, object?> BuildDetalle(
        Dictionary<string, object?> cabeceraRow,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var keyed = Keyed(cabeceraRow);
        var idSba04 = GetInt(keyed, "idSba04") ?? 0;
        var situacion = (GetString(keyed, "situacion") ?? string.Empty).Trim();
        var nInterno = GetInt(keyed, "nInterno") ?? 0;
        var fechaUlt = GetString(keyed, "fechaUltimaModificacion") ?? string.Empty;
        var horaUlt = (GetString(keyed, "horaUltimaModificacion") ?? string.Empty).Trim();

        var detalle = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["codComp"] = GetString(keyed, "codComp") ?? string.Empty,
            ["nComp"] = GetString(keyed, "nComp") ?? string.Empty,
            ["barra"] = GetInt(keyed, "barra") ?? 0,
            ["nInterno"] = nInterno,
            ["idSba04"] = idSba04,
            ["clase"] = GetInt(keyed, "clase") ?? 0,
            ["situacion"] = situacion,
            ["externo"] = GetBool(keyed, "externo") ?? false,
            ["rowVersion"] = EncodeRowVersion(idSba04, situacion, fechaUlt, horaUlt, nInterno),
            ["fecha"] = GetString(keyed, "fecha"),
            ["concepto"] = GetString(keyed, "concepto"),
            ["cotizacion"] = GetDouble(keyed, "cotizacion"),
            ["codClient"] = GetString(keyed, "codClient"),
            ["codProvee"] = GetString(keyed, "codProvee"),
            ["observaciones"] = GetString(keyed, "observaciones"),
            ["renglones"] = (resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>())
                .Select(MapRenglon)
                .ToList()
        };

        var reversionRow = resultSets.ElementAtOrDefault(2)?.FirstOrDefault();
        if (reversionRow is not null)
        {
            var reversion = MapReversion(reversionRow);
            if (reversion is not null)
            {
                detalle["reversion"] = reversion;
            }
        }

        return detalle;
    }

    internal static string EncodeRowVersion(
        int idSba04,
        string situacionTrim,
        string fechaUltMod,
        string horaUltMod,
        int nInterno)
    {
        var payload = string.Join(
            "|",
            idSba04.ToString(),
            situacionTrim.Trim(),
            fechaUltMod ?? string.Empty,
            (horaUltMod ?? string.Empty).Trim(),
            nInterno.ToString());

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static Dictionary<string, object?> MapRenglon(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["renglon"] = GetInt(keyed, "renglon") ?? 0,
            ["codCta"] = GetInt(keyed, "codCta") ?? 0,
            ["dH"] = GetString(keyed, "dH") ?? string.Empty,
            ["monto"] = GetDouble(keyed, "monto") ?? 0d,
            ["leyenda"] = GetString(keyed, "leyenda"),
            ["idSba05"] = GetInt(keyed, "idSba05") ?? 0
        };
    }

    private static Dictionary<string, object?>? MapReversion(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        var rol = GetString(keyed, "rol");
        if (string.IsNullOrWhiteSpace(rol))
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["rol"] = rol.Trim()
        };

        if (string.Equals(rol, "origen", StringComparison.OrdinalIgnoreCase))
        {
            result["codCompRev"] = GetString(keyed, "codCompRev") ?? string.Empty;
            result["nCompRev"] = GetString(keyed, "nCompRev") ?? string.Empty;
            result["barraRev"] = GetInt(keyed, "barraRev") ?? 0;
        }
        else
        {
            result["codCompOri"] = GetString(keyed, "codCompOri") ?? string.Empty;
            result["nCompOri"] = GetString(keyed, "nCompOri") ?? string.Empty;
            result["barraOri"] = GetInt(keyed, "barraOri") ?? 0;
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

    private static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return ConvertValue(raw)?.ToString();
    }

    private static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, string key)
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
            decimal d => (int)d,
            _ => null
        };
    }

    private static Dictionary<string, object?> Keyed(Dictionary<string, object?> row) =>
        new(row, StringComparer.OrdinalIgnoreCase);

    private static object? GetRaw(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not DBNull ? value : null;

    private static string? GetString(Dictionary<string, object?> row, string key)
    {
        var value = GetRaw(row, key);
        return value?.ToString();
    }

    private static int? GetInt(Dictionary<string, object?> row, string key) =>
        GetRaw(row, key) switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            decimal d => (int)d,
            double dbl => (int)dbl,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };

    private static double? GetDouble(Dictionary<string, object?> row, string key) =>
        GetRaw(row, key) switch
        {
            null => null,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => null
        };

    private static bool? GetBool(Dictionary<string, object?> row, string key) =>
        GetRaw(row, key) switch
        {
            null => null,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null
        };

    private static MovimientosTesoreriaOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static MovimientosTesoreriaOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
