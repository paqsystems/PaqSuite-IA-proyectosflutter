using System.Text.Json;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Acopios;

public sealed class AcopiosOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AcopiosGatewayRunner
{
    private readonly IInformesSpExecutor companySpExecutor;

    public AcopiosGatewayRunner(IInformesSpExecutor companySpExecutor)
    {
        this.companySpExecutor = companySpExecutor;
    }

    public async Task<AcopiosOutcome> RunAsync(
        AcopiosOperationDefinition definition,
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        string? databaseOverride = null;
        if (definition.UseCompanyDatabaseOverride)
        {
            databaseOverride = ExtractString(parameters, "_database");
            if (string.IsNullOrWhiteSpace(databaseOverride))
            {
                return Fail("INVALID_PARAMETERS", "El parametro _database es obligatorio para esta operacion Acopios.");
            }
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new AcopiosOutcome
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
                databaseOverride: databaseOverride);

            var spParams = MapSpParameters(definition, parameters, databaseOverride);
            var resultSets = await companySpExecutor
                .ExecuteAsync(
                    connectionString,
                    definition.StoredProcedure,
                    spParams,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            return BuildSuccess(definition, resultSets);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static AcopiosOutcome BuildSuccess(
        AcopiosOperationDefinition definition,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        return definition.Shape switch
        {
            AcopiosResponseShape.Parametros => Ok(BuildParametros(resultSets)),
            AcopiosResponseShape.FacturasList => Ok(BuildFacturasList(resultSets)),
            AcopiosResponseShape.PedidosList => Ok(BuildPedidosList(resultSets)),
            AcopiosResponseShape.SaldosList => Ok(BuildSaldosList(resultSets)),
            AcopiosResponseShape.ListaPrecios => Ok(BuildListaPrecios(resultSets)),
            AcopiosResponseShape.FacturaGet => BuildFacturaGet(resultSets),
            AcopiosResponseShape.FacturaDetalleGet => BuildNullableObject(BuildFacturaDetalle(resultSets)),
            AcopiosResponseShape.PedidoDetalleGet => BuildNullableObject(BuildPedidoDetalle(resultSets)),
            AcopiosResponseShape.PedidoBatchGet => Ok(BuildPedidoBatch(resultSets)),
            AcopiosResponseShape.ResultCodeWithId => BuildResultCode(definition.Operation, resultSets, includeAsociacionExtras: true),
            AcopiosResponseShape.ResultCodeOnly => BuildResultCode(definition.Operation, resultSets, includeAsociacionExtras: false),
            _ => Fail("INTERNAL_ERROR", $"Shape Acopios no soportado: {definition.Shape}")
        };
    }

    private static object BuildParametros(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var filas = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
        return new Dictionary<string, object?>
        {
            ["parametros"] = filas.Select(MapParametro).ToList()
        };
    }

    private static object BuildFacturasList(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var total = ReadTotal(resultSets);
        var filas = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
        return new Dictionary<string, object?>
        {
            ["facturas"] = filas.Select(MapFacturaElegible).ToList(),
            ["total"] = total
        };
    }

    private static object BuildPedidosList(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var total = ReadTotal(resultSets);
        var filas = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
        return new Dictionary<string, object?>
        {
            ["items"] = filas.Select(MapPedidoDisponible).ToList(),
            ["total"] = total
        };
    }

    private static object BuildSaldosList(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var total = ReadTotal(resultSets);
        var filas = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
        return new Dictionary<string, object?>
        {
            ["items"] = filas.Select(MapSaldo).ToList(),
            ["total"] = total
        };
    }

    private static object BuildListaPrecios(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var total = ReadTotal(resultSets);
        var filas = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
        return new Dictionary<string, object?>
        {
            ["total_filas"] = total,
            ["filas"] = filas.Select(MapListaPrecio).ToList()
        };
    }

    private static AcopiosOutcome BuildFacturaGet(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var row = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
        return row is null ? Ok(null) : Ok(MapFacturaAcopio(row));
    }

    private static AcopiosOutcome BuildNullableObject(object? data) => Ok(data);

    private static object? BuildFacturaDetalle(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var cabeceraRow = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
        if (cabeceraRow is null)
        {
            return null;
        }

        var keyed = Keyed(cabeceraRow);
        var importeGravado = GetDecimal(keyed, "importeGravado") ?? 0m;
        var importeExento = GetDecimal(keyed, "importeExento") ?? 0m;
        var importeImpuestos = GetDecimal(keyed, "importeImpuestos") ?? 0m;
        var importeTotal = GetDecimal(keyed, "importeTotal") ?? 0m;
        var importeNeto = importeGravado + importeExento;
        if (importeNeto <= 0m)
        {
            importeNeto = Math.Max(0m, importeTotal - importeImpuestos);
        }

        var detalle = (resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(MapFacturaDetalleRenglon)
            .ToList();

        return new Dictionary<string, object?>
        {
            ["EmpresaBd"] = GetString(keyed, "empresaBd") ?? string.Empty,
            ["EmpresaOrigen"] = GetString(keyed, "empresaOrigen"),
            ["TComp"] = GetString(keyed, "tComp") ?? string.Empty,
            ["NComp"] = GetString(keyed, "nComp") ?? string.Empty,
            ["CodClient"] = GetString(keyed, "codClient") ?? string.Empty,
            ["RazonSocial"] = GetString(keyed, "razonSocial"),
            ["FechaEmision"] = GetDateTime(keyed, "fechaEmision"),
            ["ImporteGravado"] = importeGravado,
            ["ImporteExento"] = importeExento,
            ["ImporteImpuestos"] = importeImpuestos,
            ["ImporteTotal"] = importeTotal,
            ["ImporteNeto"] = importeNeto,
            ["Estado"] = GetString(keyed, "estado"),
            ["Detalle"] = detalle
        };
    }

    private static object? BuildPedidoDetalle(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var cabeceraRow = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
        if (cabeceraRow is null)
        {
            return null;
        }

        var keyed = Keyed(cabeceraRow);
        var detalle = (resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>())
            .Select(MapPedidoRenglon)
            .ToList();

        return new Dictionary<string, object?>
        {
            ["EmpresaId"] = GetInt(keyed, "empresaId"),
            ["EmpresaBd"] = GetString(keyed, "empresaBd") ?? string.Empty,
            ["EmpresaOrigen"] = GetString(keyed, "empresaOrigen"),
            ["TalonPed"] = GetInt(keyed, "talonPed"),
            ["NroPedido"] = GetString(keyed, "nroPedido"),
            ["CodClient"] = GetString(keyed, "codClient"),
            ["RazonSocial"] = GetString(keyed, "razonSocial"),
            ["FechaPedido"] = GetDateTime(keyed, "fechaPedido"),
            ["FechaEntrega"] = GetDateTime(keyed, "fechaEntrega"),
            ["TotalPedido"] = GetDecimal(keyed, "totalPedido"),
            ["Estado"] = GetInt(keyed, "estado"),
            ["Detalle"] = detalle
        };
    }

    private static object BuildPedidoBatch(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var cabecerasRs = resultSets.ElementAtOrDefault(0) ?? Array.Empty<Dictionary<string, object?>>();
        if (cabecerasRs.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var renglonesRs = resultSets.ElementAtOrDefault(1) ?? Array.Empty<Dictionary<string, object?>>();
        var renglonesByKey = renglonesRs
            .GroupBy(BuildPedidoKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var result = new List<Dictionary<string, object?>>(cabecerasRs.Count);
        foreach (var cabeceraRow in cabecerasRs)
        {
            var keyed = Keyed(cabeceraRow);
            var key = BuildPedidoKey(keyed);
            var detalleRows = renglonesByKey.TryGetValue(key, out var matched)
                ? matched
                : new List<Dictionary<string, object?>>();

            result.Add(new Dictionary<string, object?>
            {
                ["EmpresaId"] = GetInt(keyed, "empresaId"),
                ["EmpresaBd"] = GetString(keyed, "empresaBd") ?? string.Empty,
                ["EmpresaOrigen"] = GetString(keyed, "empresaOrigen"),
                ["TalonPed"] = GetInt(keyed, "talonPed"),
                ["NroPedido"] = GetString(keyed, "nroPedido"),
                ["CodClient"] = GetString(keyed, "codClient"),
                ["RazonSocial"] = GetString(keyed, "razonSocial"),
                ["FechaPedido"] = GetDateTime(keyed, "fechaPedido"),
                ["FechaEntrega"] = GetDateTime(keyed, "fechaEntrega"),
                ["TotalPedido"] = GetDecimal(keyed, "totalPedido"),
                ["Estado"] = GetInt(keyed, "estado"),
                ["Detalle"] = detalleRows.Select(MapPedidoRenglon).ToList()
            });
        }

        return result;
    }

    private static AcopiosOutcome BuildResultCode(
        string operation,
        IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets,
        bool includeAsociacionExtras)
    {
        var row = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
        if (row is null)
        {
            return Fail("respuestaVacia", "respuestaVacia");
        }

        var keyed = Keyed(row);
        var resultCode = GetString(keyed, "resultCode") ?? string.Empty;
        if (!string.Equals(resultCode, "OK", StringComparison.OrdinalIgnoreCase))
        {
            var detail = resultCode;
            if (string.Equals(operation, "Acopios.Asociacion.Create", StringComparison.Ordinal)
                && string.Equals(resultCode, "clienteIncompatible", StringComparison.OrdinalIgnoreCase))
            {
                detail =
                    $"{resultCode}|pedido={GetString(keyed, "pedidoCodClient")}|acopio={GetString(keyed, "acopioCodClient")}";
            }
            else if (string.Equals(operation, "Acopios.Asociacion.Create", StringComparison.Ordinal)
                     && string.Equals(resultCode, "saldoInsuficiente", StringComparison.OrdinalIgnoreCase))
            {
                detail =
                    $"{resultCode}|saldo={GetDecimal(keyed, "saldoDisponible")}|importe={GetDecimal(keyed, "importeValorizado")}";
            }

            return Fail(resultCode, detail);
        }

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["resultCode"] = "OK",
            ["id"] = GetInt(keyed, "id")
        };

        if (string.Equals(operation, "Acopios.Asociacion.Create", StringComparison.Ordinal) && includeAsociacionExtras)
        {
            data["importeValorizado"] = GetDecimal(keyed, "importeValorizado");
            data["saldoDisponible"] = GetDecimal(keyed, "saldoDisponible");
            data["saldoRestante"] = GetDecimal(keyed, "saldoRestante");
            data["pedidoCodClient"] = GetString(keyed, "pedidoCodClient");
            data["acopioCodClient"] = GetString(keyed, "acopioCodClient");
        }

        if (string.Equals(operation, "Acopios.Asociacion.Delete", StringComparison.Ordinal))
        {
            data["tComp"] = GetString(keyed, "tComp");
            data["nComp"] = GetString(keyed, "nComp");
        }

        return Ok(data);
    }

    private static Dictionary<string, object?> MapParametro(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["Clave"] = GetString(keyed, "Clave") ?? string.Empty,
            ["TipoValor"] = GetString(keyed, "Tipo_Valor") ?? GetString(keyed, "TipoValor"),
            ["ValorBool"] = GetBool(keyed, "Valor_Bool") ?? GetBool(keyed, "ValorBool"),
            ["ValorInt"] = GetInt(keyed, "Valor_Int") ?? GetInt(keyed, "ValorInt"),
            ["ValorDecimal"] = GetDecimal(keyed, "Valor_Decimal") ?? GetDecimal(keyed, "ValorDecimal"),
            ["ValorString"] = GetString(keyed, "Valor_String") ?? GetString(keyed, "ValorString"),
            ["ValorDateTime"] = GetDateTime(keyed, "Valor_DateTime") ?? GetDateTime(keyed, "ValorDateTime"),
            ["ValorText"] = GetString(keyed, "Valor_Text") ?? GetString(keyed, "ValorText")
        };
    }

    private static Dictionary<string, object?> MapFacturaElegible(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["EmpresaBd"] = GetString(keyed, "empresaBd") ?? string.Empty,
            ["TComp"] = GetString(keyed, "tComp") ?? string.Empty,
            ["NComp"] = GetString(keyed, "nComp") ?? string.Empty,
            ["CodClient"] = GetString(keyed, "codClient") ?? string.Empty,
            ["RazonSocial"] = GetString(keyed, "razonSocial"),
            ["FechaEmision"] = GetDateTime(keyed, "fechaEmision"),
            ["ImporteTotal"] = GetDecimal(keyed, "importeTotal") ?? 0m,
            ["Estado"] = GetString(keyed, "estado") ?? string.Empty,
            ["Configurada"] = GetBool(keyed, "configurada") ?? false,
            ["AcopioId"] = GetInt(keyed, "acopioId"),
            ["ListaPreciosId"] = GetInt(keyed, "listaPreciosId"),
            ["FechaVigencia"] = GetDateTime(keyed, "fechaVigencia"),
            ["Descuento"] = GetDecimal(keyed, "descuento")
        };
    }

    private static Dictionary<string, object?> MapPedidoDisponible(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["EmpresaBd"] = GetString(keyed, "empresaBd") ?? string.Empty,
            ["EmpresaOrigen"] = GetString(keyed, "empresaOrigen"),
            ["TalonPed"] = GetInt(keyed, "talonPed"),
            ["NroPedido"] = GetString(keyed, "nroPedido"),
            ["CodClient"] = GetString(keyed, "codClient"),
            ["RazonSocial"] = GetString(keyed, "razonSocial"),
            ["FechaPedido"] = GetDateTime(keyed, "fechaPedido"),
            ["FechaEntrega"] = GetDateTime(keyed, "fechaEntrega"),
            ["TotalPedido"] = GetDecimal(keyed, "totalPedido"),
            ["Estado"] = GetInt(keyed, "estado")
        };
    }

    private static Dictionary<string, object?> MapSaldo(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["Id"] = GetInt(keyed, "id") ?? 0,
            ["TComp"] = GetString(keyed, "tComp") ?? string.Empty,
            ["NComp"] = GetString(keyed, "nComp") ?? string.Empty,
            ["CodClient"] = GetString(keyed, "codClient") ?? string.Empty,
            ["RazonSocial"] = GetString(keyed, "razonSocial"),
            ["FechaVigencia"] = GetDateTime(keyed, "fechaVigencia"),
            ["ListaPreciosId"] = GetInt(keyed, "listaPreciosId"),
            ["ListaPreciosNumero"] = GetString(keyed, "listaPreciosNumero"),
            ["ListaPreciosNombre"] = GetString(keyed, "listaPreciosNombre"),
            ["Descuento"] = GetDecimal(keyed, "descuento") ?? 0m,
            ["ImporteNeto"] = GetDecimal(keyed, "importeNeto") ?? 0m,
            ["ImporteImpuestos"] = GetDecimal(keyed, "importeImpuestos") ?? 0m,
            ["ImporteTotal"] = GetDecimal(keyed, "importeTotal") ?? 0m,
            ["FechaUmoAcopio"] = GetDateTime(keyed, "fechaUmoAcopio"),
            ["SaldoAnterior"] = GetDecimal(keyed, "saldoAnterior") ?? 0m,
            ["Estado"] = GetInt(keyed, "estado") ?? 0
        };
    }

    private static Dictionary<string, object?> MapListaPrecio(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["Id"] = GetInt(keyed, "id") ?? 0,
            ["Numero"] = GetString(keyed, "numero") ?? string.Empty,
            ["Nombre"] = GetString(keyed, "nombre") ?? string.Empty,
            ["Label"] = GetString(keyed, "label") ?? string.Empty
        };
    }

    private static Dictionary<string, object?> MapFacturaAcopio(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["Id"] = GetInt(keyed, "id") ?? 0,
            ["TComp"] = GetString(keyed, "tComp") ?? string.Empty,
            ["NComp"] = GetString(keyed, "nComp") ?? string.Empty,
            ["CodClient"] = GetString(keyed, "codClient") ?? string.Empty,
            ["RazonSocial"] = GetString(keyed, "razonSocial"),
            ["FechaVigencia"] = FormatDateYmd(GetDateTime(keyed, "fechaVigencia")),
            ["ListaPreciosId"] = GetInt(keyed, "listaPreciosId") ?? 0,
            ["ListaPreciosNumero"] = GetString(keyed, "listaPreciosNumero"),
            ["ListaPreciosNombre"] = GetString(keyed, "listaPreciosNombre"),
            ["Descuento"] = GetDecimal(keyed, "descuento") ?? 0m,
            ["ImporteNeto"] = GetDecimal(keyed, "importeNeto") ?? 0m,
            ["ImporteImpuestos"] = GetDecimal(keyed, "importeImpuestos") ?? 0m,
            ["ImporteTotal"] = GetDecimal(keyed, "importeTotal") ?? 0m,
            ["FechaUmoAcopio"] = FormatDateIso(GetDateTime(keyed, "fechaUmoAcopio")),
            ["SaldoAnterior"] = GetDecimal(keyed, "saldoAnterior") ?? 0m,
            ["Estado"] = GetInt(keyed, "estado") ?? 0
        };
    }

    private static Dictionary<string, object?> MapFacturaDetalleRenglon(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["CodArticu"] = GetString(keyed, "codArticu") ?? string.Empty,
            ["Cantidad"] = GetDecimal(keyed, "cantidad") ?? 0m,
            ["PrecioNeto"] = GetDecimal(keyed, "precioNeto"),
            ["ImporteNeto"] = GetDecimal(keyed, "importeNeto") ?? 0m,
            ["Descuento"] = GetDecimal(keyed, "descuento"),
            ["PorcIva"] = GetDecimal(keyed, "porcIva")
        };
    }

    private static Dictionary<string, object?> MapPedidoRenglon(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        return new Dictionary<string, object?>
        {
            ["CodArticu"] = GetString(keyed, "codArticu") ?? string.Empty,
            ["Cantidad"] = GetDecimal(keyed, "cantidad") ?? 0m,
            ["PrecioPedido"] = GetDecimal(keyed, "precioPedido"),
            ["DescuentoPedido"] = GetDecimal(keyed, "descuentoPedido")
        };
    }

    private static Dictionary<string, object?> MapSpParameters(
        AcopiosOperationDefinition definition,
        IReadOnlyDictionary<string, object?> parameters,
        string? databaseOverride)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in definition.Parameters)
        {
            if (!parameters.TryGetValue(name, out var raw))
            {
                continue;
            }

            result[name] = ConvertValue(raw);
        }

        if (definition.PassDatabaseAsSpParam && !string.IsNullOrWhiteSpace(databaseOverride))
        {
            result["_database"] = databaseOverride;
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

        return raw switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => raw.ToString()
        };
    }

    private static int ReadTotal(IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>> resultSets)
    {
        var totalesRow = resultSets.ElementAtOrDefault(0)?.FirstOrDefault();
        return totalesRow is null ? 0 : GetInt(Keyed(totalesRow), "total_filas") ?? 0;
    }

    private static string BuildPedidoKey(Dictionary<string, object?> row)
    {
        var keyed = Keyed(row);
        var talon = GetInt(keyed, "talonPed")?.ToString() ?? string.Empty;
        var nro = (GetString(keyed, "nroPedido") ?? string.Empty).Trim();
        return $"{talon}|{nro}";
    }

    private static Dictionary<string, object?> Keyed(Dictionary<string, object?> row) =>
        new(row, StringComparer.OrdinalIgnoreCase);

    private static string? GetString(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        return value.ToString();
    }

    private static int? GetInt(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        if (value is int i)
        {
            return i;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static decimal? GetDecimal(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        if (value is decimal d)
        {
            return d;
        }

        return decimal.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        if (value is bool b)
        {
            return b;
        }

        if (bool.TryParse(value.ToString(), out var parsed))
        {
            return parsed;
        }

        return int.TryParse(value.ToString(), out var asInt) ? asInt != 0 : null;
    }

    private static DateTime? GetDateTime(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt;
        }

        return DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static string? FormatDateYmd(DateTime? value) => value?.ToString("yyyy-MM-dd");

    private static string? FormatDateIso(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ss");

    private static AcopiosOutcome Ok(object? data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static AcopiosOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
