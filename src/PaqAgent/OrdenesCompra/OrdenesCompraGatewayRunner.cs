using System.Text.Json;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.OrdenesCompra;

public sealed class OrdenesCompraOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class OrdenesCompraGatewayRunner
{
    private readonly IInformesSpExecutor companySpExecutor;

    public OrdenesCompraGatewayRunner(IInformesSpExecutor companySpExecutor)
    {
        this.companySpExecutor = companySpExecutor;
    }

    public async Task<OrdenesCompraOutcome> RunAsync(
        OrdenesCompraOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonOc = ExtractInt(parameters, "talon_oc");
        var nOrdenCo = ExtractString(parameters, "n_orden_co");

        if (string.IsNullOrWhiteSpace(database) || talonOc is null || string.IsNullOrWhiteSpace(nOrdenCo))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros talon_oc, n_orden_co y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new OrdenesCompraOutcome
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
                ["talon_oc"] = talonOc.Value,
                ["n_orden_co"] = nOrdenCo.Trim()
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
                return Fail("NOT_FOUND", "Orden de compra no encontrada.");
            }

            return Ok(BuildOrden(cabecera, resultSets));
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Dictionary<string, object?> BuildOrden(
        Dictionary<string, object?> cabeceraRow,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var keyed = Keyed(cabeceraRow);
        var orden = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idCpa35"] = GetInt(keyed, "idCpa35"),
            ["talonOc"] = GetInt(keyed, "talonOc") ?? 0,
            ["nOrdenCo"] = GetString(keyed, "nOrdenCo") ?? string.Empty,
            ["estado"] = GetInt(keyed, "estado"),
            ["totalCte"] = GetDouble(keyed, "totalCte") ?? 0d,
            ["totalExt"] = GetDouble(keyed, "totalExt") ?? 0d,
            ["rowVersion"] = EncodeRowVersion(GetRaw(keyed, "rowVersion")),
            ["codProvee"] = GetString(keyed, "codProvee"),
            ["fechaEmisio"] = NormalizeDate(GetString(keyed, "fechaEmisio")),
            ["fechaVigenc"] = NormalizeDate(GetString(keyed, "fechaVigenc")),
            ["fechaGener"] = NormalizeDate(GetString(keyed, "fechaGener")),
            ["monCte"] = GetBool(keyed, "monCte") ?? true,
            ["cotiz"] = GetDouble(keyed, "cotiz") ?? 1d,
            ["codLista"] = GetInt(keyed, "codLista") ?? 0,
            ["condCompr"] = GetInt(keyed, "condCompr") ?? 0,
            ["codCompra"] = GetString(keyed, "codCompra"),
            ["nroSucurs"] = GetInt(keyed, "nroSucurs") ?? 0,
            ["porcBonif"] = GetDouble(keyed, "porcBonif") ?? 0d,
            ["observaciones"] = GetString(keyed, "observaciones") ?? string.Empty,
            ["leyendas"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["leyenda1"] = GetString(keyed, "leyenda1"),
                ["leyenda2"] = GetString(keyed, "leyenda2"),
                ["leyenda3"] = GetString(keyed, "leyenda3"),
                ["leyenda4"] = GetString(keyed, "leyenda4"),
                ["leyenda5"] = GetString(keyed, "leyenda5")
            }
        };

        var textosByRenglon = (resultSets.ElementAtOrDefault(3) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(Keyed)
            .Where(t => GetInt(t, "nRenglonOc") is > 0)
            .GroupBy(t => GetInt(t, "nRenglonOc")!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.First());

        var planes = (resultSets.ElementAtOrDefault(2) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(MapPlan)
            .ToList();

        orden["renglones"] = (resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(row => MapRenglon(row, textosByRenglon, planes))
            .ToList();

        return orden;
    }

    private static Dictionary<string, object?> MapRenglon(
        Dictionary<string, object?> row,
        IReadOnlyDictionary<int, Dictionary<string, object?>> textosByRenglon,
        IReadOnlyList<Dictionary<string, object?>> planes)
    {
        var keyed = Keyed(row);
        var nRenglonOc = GetInt(keyed, "nRenglonOc") ?? 0;
        var idCpa36 = GetInt(keyed, "idCpa36");

        string? descripcion = null;
        string? descAdicional = null;
        if (nRenglonOc > 0 && textosByRenglon.TryGetValue(nRenglonOc, out var texto))
        {
            descripcion = GetString(texto, "descripcion");
            descAdicional = GetString(texto, "descAdicional");
        }

        descripcion ??= GetString(keyed, "descripcionCpa36");
        descAdicional ??= GetString(keyed, "descAdicionalCpa36");

        var item = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idCpa36"] = idCpa36,
            ["nRenglonOc"] = nRenglonOc,
            ["codArticu"] = GetString(keyed, "codArticu"),
            ["codDeposi"] = GetString(keyed, "codDeposi"),
            ["cantPedida"] = GetDouble(keyed, "cantPedida") ?? 0d,
            ["cantPendie"] = GetDouble(keyed, "cantPendie") ?? 0d,
            ["cantRecibi"] = GetDouble(keyed, "cantRecibi") ?? 0d,
            ["cantPedida2"] = GetDouble(keyed, "cantPedida2") ?? 0d,
            ["precio"] = GetDouble(keyed, "precio") ?? 0d,
            ["porcDcto"] = GetDouble(keyed, "porcDcto") ?? 0d,
            ["estado"] = GetInt(keyed, "estado") ?? 0,
            ["unidadMedidaSeleccionada"] = GetString(keyed, "unidadMedidaSeleccionada"),
            ["codSector"] = GetString(keyed, "codSector"),
            ["codSolic"] = GetString(keyed, "codSolic"),
            ["observaciones"] = GetString(keyed, "observaciones"),
            ["pendienteFacturar"] = GetDouble(keyed, "pendienteFacturar") ?? 0d,
            ["cantidadFacturada"] = GetDouble(keyed, "cantidadFacturada") ?? 0d,
            ["descripcion"] = descripcion,
            ["descAdicional"] = descAdicional,
            ["planesEntrega"] = planes
                .Where(p => MatchesPlan(p, idCpa36, nRenglonOc))
                .Select(p => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["nPlaneOc"] = p["nPlaneOc"],
                    ["fechaRecepc"] = p["fechaRecepc"],
                    ["cantidad"] = p["cantidad"],
                    ["cantidad2"] = p["cantidad2"]
                })
                .ToList()
        };

        return item;
    }

    private static bool MatchesPlan(Dictionary<string, object?> plan, int? idCpa36, int nRenglonOc)
    {
        if (idCpa36 is > 0 && plan.TryGetValue("idCpa36", out var planId) && planId is int pid && pid > 0)
        {
            return pid == idCpa36;
        }

        return plan.TryGetValue("nRenglonOc", out var nr) && nr is int n && n == nRenglonOc && nRenglonOc > 0;
    }

    private static Dictionary<string, object?> MapPlan(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idCpa36"] = GetInt(keyed, "idCpa36"),
            ["nRenglonOc"] = GetInt(keyed, "nRenglonOc") ?? 0,
            ["nPlaneOc"] = GetInt(keyed, "nPlaneOc"),
            ["fechaRecepc"] = NormalizeDate(GetString(keyed, "fechaRecepc")),
            ["cantidad"] = GetDouble(keyed, "cantidad") ?? 0d,
            ["cantidad2"] = GetDouble(keyed, "cantidad2") ?? 0d
        };
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("1800", StringComparison.Ordinal))
        {
            return null;
        }

        return value.Length >= 10 ? value[..10] : value;
    }

    private static string? EncodeRowVersion(object? value)
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
        var s = value?.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
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

    private static OrdenesCompraOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static OrdenesCompraOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
