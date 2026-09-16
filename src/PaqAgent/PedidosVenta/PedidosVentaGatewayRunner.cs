using System.Text.Json;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.PedidosVenta;

public sealed class PedidosVentaOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class PedidosVentaGatewayRunner
{
    private readonly IInformesSpExecutor companySpExecutor;

    public PedidosVentaGatewayRunner(IInformesSpExecutor companySpExecutor)
    {
        this.companySpExecutor = companySpExecutor;
    }

    public async Task<PedidosVentaOutcome> RunAsync(
        PedidosVentaOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonPed = ExtractInt(parameters, "talon_ped");
        var nroPedido = ExtractString(parameters, "nro_pedido");

        if (string.IsNullOrWhiteSpace(database) || talonPed is null || string.IsNullOrWhiteSpace(nroPedido))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Los parametros talon_ped, nro_pedido y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new PedidosVentaOutcome
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
                ["talon_ped"] = talonPed.Value,
                ["nro_pedido"] = nroPedido.Trim()
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
                return Fail("NOT_FOUND", "Pedido no encontrado.");
            }

            return Ok(BuildPedido(cabecera, resultSets));
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Dictionary<string, object?> BuildPedido(
        Dictionary<string, object?> cabeceraRow,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var keyed = Keyed(cabeceraRow);
        var pedido = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idGva21"] = GetInt(keyed, "idGva21"),
            ["talonPed"] = GetInt(keyed, "talonPed") ?? 0,
            ["nroPedido"] = GetString(keyed, "nroPedido") ?? string.Empty,
            ["estado"] = GetInt(keyed, "estado"),
            ["totalPedi"] = GetDouble(keyed, "totalPedi") ?? 0d,
            ["totalPediConImpuestos"] = GetDouble(keyed, "totalPediConImpuestos") ?? 0d,
            ["rowVersion"] = EncodeRowVersion(GetRaw(keyed, "rowVersion")),
            ["codClient"] = GetString(keyed, "codClient"),
            ["fechaPedi"] = GetString(keyed, "fechaPedi"),
            ["fechaEntr"] = GetString(keyed, "fechaEntr"),
            ["compStk"] = GetBool(keyed, "compStk") ?? false,
            ["monCte"] = GetBool(keyed, "monCte") ?? true,
            ["cotiz"] = GetDouble(keyed, "cotiz") ?? 1d,
            ["nLista"] = GetInt(keyed, "nLista") ?? 0,
            ["condVta"] = GetInt(keyed, "condVta") ?? 0,
            ["codVended"] = GetString(keyed, "codVended"),
            ["codTransp"] = GetString(keyed, "codTransp"),
            ["observaciones"] = GetString(keyed, "observaciones")
        };

        var idExterno = GetString(keyed, "idExterno");
        if (!string.IsNullOrWhiteSpace(idExterno))
        {
            pedido["idExterno"] = idExterno;
        }

        var totalPerc = GetDouble(keyed, "totalPercepciones");
        if (totalPerc is not null)
        {
            pedido["totalPercepciones"] = totalPerc;
        }

        var datosClienteRow = resultSets.ElementAtOrDefault(2)?.FirstOrDefault();
        pedido["datosCliente"] = datosClienteRow is null ? null : MapDatosCliente(datosClienteRow);

        pedido["renglones"] = (resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(MapRenglon)
            .ToList();

        pedido["impuestos"] = (resultSets.ElementAtOrDefault(3) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(MapImpuesto)
            .ToList();

        pedido["cuotas"] = (resultSets.ElementAtOrDefault(4) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(MapCuota)
            .ToList();

        return pedido;
    }

    private static Dictionary<string, object?> MapDatosCliente(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        PutIfNotNull(result, "razonSoci", GetString(keyed, "razonSoci"));
        PutIfNotNull(result, "tipoDoc", GetString(keyed, "tipoDoc"));
        PutIfNotNull(result, "nCuit", GetString(keyed, "nCuit"));
        PutIfNotNull(result, "codProvin", GetString(keyed, "codProvin"));
        var cat = GetInt(keyed, "categoriaIva");
        if (cat is not null)
        {
            result["categoriaIva"] = cat;
        }

        return result;
    }

    private static Dictionary<string, object?> MapRenglon(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        var item = new Dictionary<string, object?>(StringComparer.Ordinal);
        PutIfNotNull(item, "idGva03", GetInt(keyed, "idGva03"));
        PutIfNotNull(item, "nRenglon", GetInt(keyed, "nRenglon"));
        PutIfNotNull(item, "codArticu", GetString(keyed, "codArticu"));
        PutIfNotNull(item, "codDeposi", GetString(keyed, "codDeposi"));
        PutIfNotNull(item, "cantPedid", GetDouble(keyed, "cantPedid"));
        PutIfNotNull(item, "cantPenD", GetDouble(keyed, "cantPenD"));
        PutIfNotNull(item, "cantPenF", GetDouble(keyed, "cantPenF"));
        PutIfNotNull(item, "cantADes", GetDouble(keyed, "cantADes"));
        PutIfNotNull(item, "cantAFac", GetDouble(keyed, "cantAFac"));
        PutIfNotNull(item, "precio", GetDouble(keyed, "precio"));
        return item;
    }

    private static Dictionary<string, object?> MapImpuesto(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["codigoImpuesto"] = GetString(keyed, "codigoImpuesto") ?? string.Empty,
            ["descripcion"] = GetString(keyed, "descripcion") ?? string.Empty,
            ["tipo"] = GetInt(keyed, "tipo") ?? 0,
            ["descripcionTipo"] = GetString(keyed, "descripcionTipo") ?? string.Empty,
            ["codigoAlicuota"] = GetInt(keyed, "codigoAlicuota") ?? 0,
            ["porcentaje"] = GetDouble(keyed, "porcentaje") ?? 0d,
            ["baseImponible"] = GetDouble(keyed, "baseImponible") ?? 0d,
            ["importe"] = GetDouble(keyed, "importe") ?? 0d
        };
    }

    private static Dictionary<string, object?> MapCuota(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nroCuota"] = GetInt(keyed, "nroCuota") ?? 0,
            ["cantidadCuotas"] = GetInt(keyed, "cantidadCuotas") ?? 0,
            ["fechaVto"] = GetString(keyed, "fechaVto"),
            ["importeVto"] = GetDouble(keyed, "importeVto") ?? 0d,
            ["importeVtoExtranjera"] = GetDouble(keyed, "importeVtoExtranjera") ?? 0d,
            ["porcentaje"] = GetDouble(keyed, "porcentaje") ?? 0d
        };
    }

    private static void PutIfNotNull(Dictionary<string, object?> target, string key, object? value)
    {
        if (value is not null)
        {
            target[key] = value;
        }
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

    private static PedidosVentaOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static PedidosVentaOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
