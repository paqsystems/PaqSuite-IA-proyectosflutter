using System.Text.Json;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.AsientosContables;

public sealed class AsientosContablesOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AsientosContablesGatewayRunner
{
    private readonly IInformesSpExecutor companySpExecutor;

    public AsientosContablesGatewayRunner(IInformesSpExecutor companySpExecutor)
    {
        this.companySpExecutor = companySpExecutor;
    }

    public async Task<AsientosContablesOutcome> RunAsync(
        AsientosContablesOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var nroInterno = ExtractInt(parameters, "nro_interno_analitico");

        if (string.IsNullOrWhiteSpace(database) || nroInterno is null)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros nro_interno_analitico y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new AsientosContablesOutcome
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
                ["nro_interno_analitico"] = nroInterno.Value
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
                return Fail("NOT_FOUND", "Asiento contable no encontrado.");
            }

            return Ok(BuildDetalle(cabecera, resultSets));
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>
    /// Espejo de AsientosContablesMapper::mapCabecera + Query::mapFull / loadTiposAuxiliar.
    /// </summary>
    internal static Dictionary<string, object?> BuildDetalle(
        Dictionary<string, object?> cabeceraRow,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var keyed = Keyed(cabeceraRow);
        var auxiliares = resultSets.ElementAtOrDefault(2) ?? Array.Empty<Dictionary<string, object?>>();
        var subauxiliares = resultSets.ElementAtOrDefault(3) ?? Array.Empty<Dictionary<string, object?>>();
        var tiposPorImporte = BuildTiposAuxiliarPorImporte(auxiliares, subauxiliares);

        var renglones = (resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(row => MapRenglon(row, tiposPorImporte))
            .ToList();

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idAsientoAnaliticoCn"] = GetInt(keyed, "idAsientoAnaliticoCn") ?? 0,
            ["nroInternoAnalitico"] = GetInt(keyed, "nroInternoAnalitico") ?? 0,
            ["nroAsiento"] = GetDouble(keyed, "nroAsiento") ?? 0d,
            ["fecha"] = GetString(keyed, "fecha"),
            ["codTipoAsiento"] = GetString(keyed, "codTipoAsiento"),
            ["codMoneda"] = GetString(keyed, "codMoneda"),
            ["leyenda"] = GetString(keyed, "leyenda"),
            ["observaciones"] = GetString(keyed, "observaciones"),
            ["estadoAsientoAnalitico"] = GetString(keyed, "estadoAsientoAnalitico") ?? string.Empty,
            ["estadoResumen"] = GetString(keyed, "estadoResumen") ?? string.Empty,
            ["rowVersion"] = EncodeRowVersion(GetRaw(keyed, "rowVersion")),
            ["renglones"] = renglones
        };
    }

    private static Dictionary<string, object?> MapRenglon(
        Dictionary<string, object?> row,
        IReadOnlyDictionary<int, List<Dictionary<string, object?>>> tiposPorImporte)
    {
        var keyed = Keyed(row);
        var idImporte = GetInt(keyed, "idRenglonImporteAnaliticoCn") ?? 0;
        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["renglon"] = GetInt(keyed, "renglon") ?? 0,
            ["codCuenta"] = GetString(keyed, "codCuenta"),
            ["dH"] = GetString(keyed, "dH") ?? string.Empty,
            ["importe"] = GetDouble(keyed, "importe") ?? 0d,
            ["leyenda"] = GetString(keyed, "leyenda")
        };

        if (idImporte > 0
            && tiposPorImporte.TryGetValue(idImporte, out var tipos)
            && tipos.Count > 0)
        {
            item["tiposAuxiliar"] = tipos;
        }

        return item;
    }

    private static Dictionary<int, List<Dictionary<string, object?>>> BuildTiposAuxiliarPorImporte(
        IReadOnlyList<Dictionary<string, object?>> auxiliares,
        IReadOnlyList<Dictionary<string, object?>> subauxiliares)
    {
        var subsByAux = new Dictionary<int, List<Dictionary<string, object?>>>();
        foreach (var subRow in subauxiliares)
        {
            var keyed = Keyed(subRow);
            var idAux = GetInt(keyed, "idAuxiliarAnaliticoCn") ?? 0;
            if (idAux <= 0)
            {
                continue;
            }

            if (!subsByAux.TryGetValue(idAux, out var list))
            {
                list = new List<Dictionary<string, object?>>();
                subsByAux[idAux] = list;
            }

            list.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["codSubauxiliar"] = (GetString(keyed, "codSubauxiliar") ?? string.Empty).Trim(),
                ["importe"] = GetDouble(keyed, "importe") ?? 0d,
                ["porcentaje"] = GetDouble(keyed, "porcentaje") ?? 0d
            });
        }

        var byImporte = new Dictionary<int, Dictionary<string, Dictionary<string, object?>>>();
        foreach (var auxRow in auxiliares)
        {
            var keyed = Keyed(auxRow);
            var idImporte = GetInt(keyed, "idRenglonImporteAnaliticoCn") ?? 0;
            var idAux = GetInt(keyed, "idAuxiliarAnaliticoCn") ?? 0;
            if (idImporte <= 0)
            {
                continue;
            }

            var codTipo = (GetString(keyed, "codTipoAuxiliar") ?? string.Empty).Trim();
            if (!byImporte.TryGetValue(idImporte, out var byTipo))
            {
                byTipo = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
                byImporte[idImporte] = byTipo;
            }

            if (!byTipo.TryGetValue(codTipo, out var tipoNode))
            {
                tipoNode = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["codTipoAuxiliar"] = codTipo,
                    ["auxiliares"] = new List<Dictionary<string, object?>>()
                };
                byTipo[codTipo] = tipoNode;
            }

            var auxiliaresList = (List<Dictionary<string, object?>>)tipoNode["auxiliares"]!;
            var auxItem = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["codAuxiliar"] = (GetString(keyed, "codAuxiliar") ?? string.Empty).Trim(),
                ["importe"] = GetDouble(keyed, "importe") ?? 0d,
                ["porcentaje"] = GetDouble(keyed, "porcentaje") ?? 0d
            };

            if (idAux > 0 && subsByAux.TryGetValue(idAux, out var subs) && subs.Count > 0)
            {
                auxItem["subauxiliares"] = subs;
            }

            auxiliaresList.Add(auxItem);
        }

        return byImporte.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Values.ToList());
    }

    /// <summary>Espejo de PedidosVentaRowVersion::encode (base64 del TIMESTAMP SQL).</summary>
    internal static string? EncodeRowVersion(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            return bytes.Length == 0 ? null : Convert.ToBase64String(bytes);
        }

        if (value is string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        return null;
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

    private static AsientosContablesOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static AsientosContablesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
