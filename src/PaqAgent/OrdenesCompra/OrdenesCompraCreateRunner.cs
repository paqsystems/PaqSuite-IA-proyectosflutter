using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqAgent.PedidosVenta;
using PaqContracts;

namespace PaqAgent.OrdenesCompra;

/// <summary>
/// D6.4.2 MVP — alta Órdenes de compra en TX company (espejo de OrdenesCompraAltaService::store).
/// Orquestación en agente: correlativo CPA56.PROXIMO + INSERT CPA35/CPA36 (+ CPA41/CPA37) + STA19.
/// <para>
/// Fuera de alcance MVP: precio lista CPA44, renglones solo-texto, perfil CPA104,
/// resolución completa de FK opcionales (pueden quedar null).
/// </para>
/// </summary>
public sealed class OrdenesCompraCreateRunner
{
    private const string TerminalIngreso = "Api-PaqSuiteWeb";

    public async Task<OrdenesCompraOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonOc = ExtractInt(parameters, "talon_oc");
        var codProvee = ExtractString(parameters, "cod_provee")?.Trim();
        var codLista = ExtractInt(parameters, "cod_lista");
        var renglonesJson = ExtractString(parameters, "renglones_json");

        if (string.IsNullOrWhiteSpace(database)
            || talonOc is null or <= 0
            || string.IsNullOrWhiteSpace(codProvee)
            || codLista is null or <= 0
            || string.IsNullOrWhiteSpace(renglonesJson))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "talon_oc, cod_provee, cod_lista, renglones_json y _database son obligatorios.");
        }

        if (string.Equals(codProvee, "000000", StringComparison.Ordinal)
            || string.Equals(codProvee, "0", StringComparison.Ordinal))
        {
            return Fail("OCASIONAL_NO_SOPORTADO", "Proveedor ocasional 000000/0 fuera de alcance D6.4.2 MVP.");
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

        List<RenglonInput> renglones;
        try
        {
            renglones = ParseRenglones(renglonesJson);
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", "renglones_json invalido: " + ex.Message);
        }

        if (renglones.Count == 0)
        {
            return Fail("INVALID_PARAMETERS", "Debe informar al menos un renglon con articulo.");
        }

        var codSucursDefault = ExtractString(parameters, "cod_sucurs")?.Trim() ?? string.Empty;
        foreach (var (renglon, index) in renglones.Select((r, i) => (r, i)))
        {
            if (renglon.CantPedida <= 0m)
            {
                return Fail("INVALID_PARAMETERS", $"renglones[{index}].cantPedida debe ser > 0.");
            }

            if (renglon.Precio < 0m)
            {
                return Fail("INVALID_PARAMETERS", $"renglones[{index}].precio debe ser >= 0.");
            }

            var codDeposi = string.IsNullOrWhiteSpace(renglon.CodDeposi) ? codSucursDefault : renglon.CodDeposi!;
            if (string.IsNullOrWhiteSpace(codDeposi))
            {
                return Fail(
                    "INVALID_PARAMETERS",
                    $"renglones[{index}].codDeposi es obligatorio (o cod_sucurs de cabecera).");
            }

            if (renglon.Planes is { Count: > 0 })
            {
                var sumaPlanes = renglon.Planes.Sum(p => p.Cantidad);
                if (Math.Abs(sumaPlanes - renglon.CantPedida) > 0.0001m)
                {
                    return Fail(
                        "INVALID_PARAMETERS",
                        $"renglones[{index}].planesEntrega: la suma de cantidad debe igualar cantPedida.");
                }
            }
        }

        try
        {
            var connectionString = SqlConnectionStringFactory.Build(
                agentOptions.Sql,
                connectTimeoutSeconds: 15,
                databaseOverride: database);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var outcome = await ExecuteCreateAsync(
                        connection,
                        transaction,
                        parameters,
                        talonOc.Value,
                        codProvee,
                        codLista.Value,
                        renglones,
                        codSucursDefault,
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.Status == JobStatuses.Success)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }

                return outcome;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<OrdenesCompraOutcome> ExecuteCreateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonOc,
        string codProvee,
        int codLista,
        IReadOnlyList<RenglonInput> renglones,
        string codSucursDefault,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TalonarioOcOkAsync(connection, transaction, talonOc, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return Fail("TALONARIO_INVALIDO", "Talonario inexistente o TIPO_COMP distinto de O.");
        }

        var proveedor = await ResolveProveedorAsync(
                connection, transaction, codProvee, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (proveedor is null)
        {
            return Fail("PROVEEDOR_INEXISTENTE", "Proveedor inexistente.");
        }

        var idCpa43 = await LookupIdAsync(
                connection, transaction,
                "SELECT TOP 1 ID_CPA43 FROM dbo.CPA43 WHERE COD_LISTA=@n OR ID_CPA43=@n",
                ("@n", codLista), timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (idCpa43 is null)
        {
            return Fail("LISTA_INEXISTENTE", "Lista de precios inexistente.");
        }

        var nOrdenCoInformado = ExtractString(parameters, "n_orden_co")?.Trim();
        string nOrdenCo;
        if (!string.IsNullOrWhiteSpace(nOrdenCoInformado))
        {
            if (!await TalonarioPermiteEditarNroAsync(
                        connection, transaction, talonOc, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Fail("EDITA_NRO_REQUERIDO", "CPA56.EDITA_NRO debe ser true para informar n_orden_co.");
            }

            nOrdenCo = nOrdenCoInformado;
            if (await OrdenExisteAsync(
                        connection, transaction, talonOc, nOrdenCo, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Fail("N_ORDEN_DUPLICADO", "Ya existe una orden con talonOc y nOrdenCo.");
            }
        }
        else
        {
            var correlativo = await AsignarCorrelativoAsync(
                    connection, transaction, talonOc, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (correlativo.ErrorCode is not null)
            {
                return Fail(correlativo.ErrorCode, correlativo.ErrorMessage ?? correlativo.ErrorCode);
            }

            nOrdenCo = correlativo.NOrdenCo!;
            if (await OrdenExisteAsync(
                        connection, transaction, talonOc, nOrdenCo, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Fail("N_ORDEN_DUPLICADO", "Ya existe una orden con talonOc y nOrdenCo.");
            }
        }

        var estado = await ReadEstadoInicialAsync(
                connection, transaction, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var monCte = ExtractBool(parameters, "mon_cte") ?? true;
        var cotiz = ExtractDecimal(parameters, "cotiz") ?? 1m;
        if (cotiz <= 0m && monCte)
        {
            cotiz = 1m;
        }

        if (!monCte && cotiz <= 0m)
        {
            return Fail("INVALID_PARAMETERS", "cotiz debe ser mayor a 0 cuando mon_cte es false.");
        }

        var porcBonif = ExtractDecimal(parameters, "porc_bonif") ?? 0m;
        var subtotalCte = Math.Round(
            renglones.Sum(r => r.CantPedida * Math.Round(r.Precio * (1m - (r.PorcDcto / 100m)), 4)),
            4);
        var totalBoni = Math.Round(subtotalCte * (porcBonif / 100m), 4);
        var totalCte = Math.Round(subtotalCte - totalBoni, 4);
        var totalExt = cotiz > 0m ? Math.Round(totalCte / cotiz, 4) : 0m;

        var idCpa35 = await NextIdAsync(
                connection, transaction, "SEQUENCE_CPA35", "CPA35", "ID_CPA35", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.Now;
        var hora = now.ToString("HHmmss");
        var usuario = ExtractString(parameters, "usuario_ingreso") ?? "api";
        var observaciones = ExtractString(parameters, "observaciones") ?? string.Empty;
        var condCompr = ExtractInt(parameters, "cond_compr") ?? proveedor.CondCompr ?? 0;
        var codCompra = ExtractString(parameters, "cod_compra") ?? string.Empty;
        var nroSucurs = ExtractInt(parameters, "nro_sucurs") ?? 0;
        var fechaEmisio = ExtractDate(parameters, "fecha_emisio") ?? DateTime.Today;
        var fechaVigenc = ExtractDate(parameters, "fecha_vigenc");
        var fechaGener = ExtractDate(parameters, "fecha_gener") ?? DateTime.Today;
        var leyendas = ParseLeyendas(ExtractString(parameters, "leyendas_json"));

        await InsertCabeceraAsync(
                connection,
                transaction,
                idCpa35,
                talonOc,
                nOrdenCo,
                codProvee,
                proveedor,
                idCpa43,
                estado,
                monCte,
                cotiz,
                codLista,
                condCompr,
                codCompra,
                nroSucurs,
                porcBonif,
                observaciones,
                leyendas,
                subtotalCte,
                totalBoni,
                totalCte,
                totalExt,
                fechaEmisio,
                fechaVigenc,
                fechaGener,
                usuario,
                hora,
                now,
                timeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var (renglon, index) in renglones.Select((r, i) => (r, i)))
        {
            var nRenglonOc = renglon.NRenglonOc ?? (index + 1);
            var idCpa36 = await NextIdAsync(
                    connection, transaction, "SEQUENCE_CPA36", "CPA36", "ID_CPA36", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            var codDeposi = string.IsNullOrWhiteSpace(renglon.CodDeposi) ? codSucursDefault : renglon.CodDeposi!;
            var precioNeto = Math.Round(renglon.Precio * (1m - (renglon.PorcDcto / 100m)), 4);
            var totalLinea = Math.Round(renglon.CantPedida * precioNeto, 4);

            var idSta11 = await LookupIdAsync(
                    connection, transaction,
                    "SELECT TOP 1 ID_STA11 FROM dbo.STA11 WHERE LTRIM(RTRIM(COD_ARTICU))=@a",
                    ("@a", renglon.CodArticu), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            var idSta22 = await LookupIdAsync(
                    connection, transaction,
                    """
                    SELECT TOP 1 ID_STA22 FROM dbo.STA22
                    WHERE LTRIM(RTRIM(COD_STA22))=LTRIM(RTRIM(@d))
                       OR LTRIM(RTRIM(COD_SUCURS))=LTRIM(RTRIM(@d))
                    """,
                    ("@d", codDeposi), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (idSta22 is null)
            {
                return Fail("DEPOSITO_INEXISTENTE", $"Deposito inexistente: {codDeposi}");
            }

            var canEqui = await LookupDecimalAsync(
                    connection, transaction,
                    "SELECT TOP 1 CAN_EQUI FROM dbo.STA11 WHERE LTRIM(RTRIM(COD_ARTICU))=@a",
                    ("@a", renglon.CodArticu), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false) ?? 0m;
            var cantPedida2 = canEqui > 0m ? Math.Round(renglon.CantPedida * canEqui, 4) : 0m;

            await InsertRenglonAsync(
                    connection,
                    transaction,
                    idCpa36,
                    idCpa35,
                    talonOc,
                    nOrdenCo,
                    nRenglonOc,
                    renglon,
                    codDeposi,
                    cantPedida2,
                    canEqui,
                    totalLinea,
                    estado,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            await InsertCpa41IfNeededAsync(
                    connection, transaction, talonOc, nOrdenCo, nRenglonOc, renglon,
                    timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            await InsertPlanesAsync(
                    connection, transaction, idCpa35, idCpa36, talonOc, nOrdenCo, nRenglonOc, renglon,
                    timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (estado <= 3 && idSta11 is not null)
            {
                await IncrementarSta19Async(
                        connection,
                        transaction,
                        idSta11.Value,
                        idSta22.Value,
                        renglon.CantPedida,
                        cantPedida2,
                        renglon.CodArticu,
                        codDeposi,
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        byte[]? rowVersion = null;
        if (await ColumnExistsAsync(connection, transaction, "CPA35", "ROW_VERSION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            rowVersion = await ScalarBytesAsync(
                    connection, transaction,
                    "SELECT ROW_VERSION FROM dbo.CPA35 WHERE ID_CPA35=@id",
                    ("@id", idCpa35), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        return Ok(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idCpa35"] = idCpa35,
            ["talonOc"] = talonOc,
            ["nOrdenCo"] = nOrdenCo,
            ["estado"] = estado,
            ["totalCte"] = (double)totalCte,
            ["totalExt"] = (double)totalExt,
            ["rowVersion"] = rowVersion is null ? null : Convert.ToBase64String(rowVersion),
            ["creado"] = true
        });
    }

    private sealed record ProveedorInfo(
        int? IdCpa01,
        string CodCpa01,
        int? CondCompr,
        string IncIva,
        string IncIi);

    private sealed record PlanEntregaInput(
        int? NPlaneOc,
        DateTime? FechaRecepc,
        decimal Cantidad,
        decimal Cantidad2);

    private sealed record RenglonInput(
        int? NRenglonOc,
        string CodArticu,
        decimal CantPedida,
        decimal Precio,
        decimal PorcDcto,
        string? CodDeposi,
        string? Descripcion,
        string? DescAdicional,
        string? Observaciones,
        string UnidadMedida,
        IReadOnlyList<PlanEntregaInput>? Planes);

    private sealed record LeyendasInput(
        string Leyenda1,
        string Leyenda2,
        string Leyenda3,
        string Leyenda4,
        string Leyenda5);

    private static List<RenglonInput> ParseRenglones(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("se esperaba un array JSON");
        }

        var list = new List<RenglonInput>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var cod = GetJsonString(el, "codArticu") ?? GetJsonString(el, "cod_articu");
            if (string.IsNullOrWhiteSpace(cod))
            {
                // MVP: renglones solo-texto fuera de alcance
                continue;
            }

            IReadOnlyList<PlanEntregaInput>? planes = null;
            if (el.TryGetProperty("planesEntrega", out var planesEl)
                || el.TryGetProperty("planes_entrega", out planesEl))
            {
                if (planesEl.ValueKind == JsonValueKind.Array && planesEl.GetArrayLength() > 0)
                {
                    var parsedPlanes = new List<PlanEntregaInput>();
                    var pIdx = 0;
                    foreach (var planEl in planesEl.EnumerateArray())
                    {
                        parsedPlanes.Add(new PlanEntregaInput(
                            GetJsonInt(planEl, "nPlaneOc") ?? GetJsonInt(planEl, "n_plane_oc") ?? (pIdx + 1),
                            GetJsonDate(planEl, "fechaRecepc") ?? GetJsonDate(planEl, "fecha_recepc"),
                            GetJsonDecimal(planEl, "cantidad") ?? 0m,
                            GetJsonDecimal(planEl, "cantidad2") ?? GetJsonDecimal(planEl, "cantidad_2") ?? 0m));
                        pIdx++;
                    }

                    planes = parsedPlanes;
                }
            }

            var unidad = GetJsonString(el, "unidadMedidaSeleccionada")
                ?? GetJsonString(el, "unidad_medida_seleccionada")
                ?? "C";
            unidad = string.IsNullOrWhiteSpace(unidad) ? "C" : unidad.Trim().ToUpperInvariant()[..1];

            list.Add(new RenglonInput(
                GetJsonInt(el, "nRenglonOc") ?? GetJsonInt(el, "n_renglon_oc"),
                cod.Trim(),
                GetJsonDecimal(el, "cantPedida") ?? GetJsonDecimal(el, "cant_pedida") ?? 0m,
                GetJsonDecimal(el, "precio") ?? 0m,
                GetJsonDecimal(el, "porcDcto") ?? GetJsonDecimal(el, "porc_dcto") ?? 0m,
                GetJsonString(el, "codDeposi") ?? GetJsonString(el, "cod_deposi"),
                GetJsonString(el, "descripcion"),
                GetJsonString(el, "descAdicional")
                    ?? GetJsonString(el, "desc_adicional")
                    ?? GetJsonString(el, "descripcionAdicional"),
                GetJsonString(el, "observaciones"),
                unidad,
                planes));
        }

        return list;
    }

    private static LeyendasInput ParseLeyendas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LeyendasInput(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            return new LeyendasInput(
                GetJsonString(el, "leyenda1") ?? GetJsonString(el, "LEYENDA_1") ?? string.Empty,
                GetJsonString(el, "leyenda2") ?? GetJsonString(el, "LEYENDA_2") ?? string.Empty,
                GetJsonString(el, "leyenda3") ?? GetJsonString(el, "LEYENDA_3") ?? string.Empty,
                GetJsonString(el, "leyenda4") ?? GetJsonString(el, "LEYENDA_4") ?? string.Empty,
                GetJsonString(el, "leyenda5") ?? GetJsonString(el, "LEYENDA_5") ?? string.Empty);
        }
        catch
        {
            return new LeyendasInput(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private static async Task<(string? NOrdenCo, string? ErrorCode, string? ErrorMessage)> AsignarCorrelativoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int talonOc,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1
                LTRIM(RTRIM(CAST(PROXIMO AS NVARCHAR(32)))) AS proximo,
                CAST(ISNULL(SUCURSAL, N'00000') AS NVARCHAR(10)) AS sucursal
            FROM dbo.CPA56
            WHERE TALONARIO = @t
              AND UPPER(LTRIM(RTRIM(CAST(TIPO_COMP AS NVARCHAR(10))))) = N'O'
            """);
        cmd.Parameters.AddWithValue("@t", talonOc);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (null, "TALONARIO_INVALIDO", "No se pudo leer CPA56.PROXIMO.");
        }

        var proximoRaw = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).TrimEnd();
        var sucursal = reader.IsDBNull(1) ? "00000" : reader.GetString(1);
        await reader.CloseAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(proximoRaw))
        {
            return (null, "PROXIMO_INVALIDO", "CPA56.PROXIMO vacio.");
        }

        int proximoNumerico;
        try
        {
            proximoNumerico = proximoRaw.Length >= 16
                ? EncriptacionTalonarios.UnCrypNro(proximoRaw)
                : ParseProximoNumerico(proximoRaw);
        }
        catch (Exception ex)
        {
            return (null, "PROXIMO_INVALIDO", ex.Message);
        }

        string nOrdenCo;
        try
        {
            nOrdenCo = EncriptacionTalonarios.CrearNumero(" ", sucursal, proximoNumerico);
        }
        catch (Exception ex)
        {
            return (null, "PROXIMO_INVALIDO", ex.Message);
        }

        var siguiente = proximoNumerico + 1;
        if (siguiente > 99_999_999)
        {
            return (null, "PROXIMO_INVALIDO", "Se agoto el correlativo del talonario (max. 8 digitos).");
        }

        var proximoSiguiente = siguiente.ToString().PadLeft(8, '0');

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            UPDATE dbo.CPA56
            SET PROXIMO = @next
            WHERE TALONARIO = @t
              AND LTRIM(RTRIM(CAST(PROXIMO AS NVARCHAR(32)))) = @prev
            """);
        upd.Parameters.AddWithValue("@next", proximoSiguiente);
        upd.Parameters.AddWithValue("@t", talonOc);
        upd.Parameters.AddWithValue("@prev", proximoRaw);
        var updated = await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            return (null, "PROXIMO_CONFLICT", "Conflicto al actualizar correlativo del talonario (PROXIMO).");
        }

        return (nOrdenCo, null, null);
    }

    private static int ParseProximoNumerico(string proximoRaw)
    {
        var trimmed = proximoRaw.TrimEnd();
        if (!trimmed.All(char.IsDigit))
        {
            throw new InvalidOperationException("PROXIMO debe ser numerico de hasta 8 digitos.");
        }

        return int.Parse(trimmed, CultureInfo.InvariantCulture);
    }

    private static async Task InsertCabeceraAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        string codProvee,
        ProveedorInfo proveedor,
        int? idCpa43,
        int estado,
        bool monCte,
        decimal cotiz,
        int codLista,
        int condCompr,
        string codCompra,
        int nroSucurs,
        decimal porcBonif,
        string observaciones,
        LeyendasInput leyendas,
        decimal subtotalCte,
        decimal totalBoni,
        decimal totalCte,
        decimal totalExt,
        DateTime fechaEmisio,
        DateTime? fechaVigenc,
        DateTime fechaGener,
        string usuario,
        string hora,
        DateTime now,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var cols = new List<string>
        {
            "ID_CPA35", "TALONARIO", "N_ORDEN_CO", "COD_PROVEE",
            "ESTADO", "MON_CTE", "COTIZ", "COD_LISTA", "COND_COMPR",
            "COD_COMPRA", "NRO_SUCURS", "PORC_BONIF",
            "TOTAL_BONI", "TOTAL_CTE", "TOTAL_EXT",
            "FECHA_INGRESO", "HORA_INGRESO", "USUARIO_INGRESO", "TERMINAL_INGRESO"
        };
        var vals = new List<string>
        {
            "@id", "@talon", "@nOrden", "@provee",
            "@estado", "@monCte", "@cotiz", "@lista", "@cond",
            "@compra", "@nroSuc", "@porcBonif",
            "@totalBoni", "@totalCte", "@totalExt",
            "@now", "@hora", "@usuario", "@terminal"
        };

        async Task AddIfExistsAsync(string column, string paramName)
        {
            if (await ColumnExistsAsync(connection, transaction, "CPA35", column, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                cols.Add(column);
                vals.Add(paramName);
            }
        }

        await AddIfExistsAsync("COD_CPA01", "@codCpa01").ConfigureAwait(false);
        await AddIfExistsAsync("FEC_EMISIO", "@fecEmisio").ConfigureAwait(false);
        await AddIfExistsAsync("FEC_VIGENC", "@fecVigenc").ConfigureAwait(false);
        await AddIfExistsAsync("FEC_GENER", "@fecGener").ConfigureAwait(false);
        await AddIfExistsAsync("OBSERVACIONES", "@obs").ConfigureAwait(false);
        await AddIfExistsAsync("OBSERVACIO", "@obsCorto").ConfigureAwait(false);
        await AddIfExistsAsync("LEYENDA_1", "@ley1").ConfigureAwait(false);
        await AddIfExistsAsync("LEYENDA_2", "@ley2").ConfigureAwait(false);
        await AddIfExistsAsync("LEYENDA_3", "@ley3").ConfigureAwait(false);
        await AddIfExistsAsync("LEYENDA_4", "@ley4").ConfigureAwait(false);
        await AddIfExistsAsync("LEYENDA_5", "@ley5").ConfigureAwait(false);
        await AddIfExistsAsync("INC_IVA", "@incIva").ConfigureAwait(false);
        await AddIfExistsAsync("INC_II", "@incIi").ConfigureAwait(false);
        await AddIfExistsAsync("SUBTOTAL_CTE", "@subCte").ConfigureAwait(false);
        await AddIfExistsAsync("SUBTOTAL_EXT", "@subExt").ConfigureAwait(false);
        await AddIfExistsAsync("TOTAL_IVA", "@totalIva").ConfigureAwait(false);
        await AddIfExistsAsync("TOTAL_II", "@totalIi").ConfigureAwait(false);
        await AddIfExistsAsync("IMPORTE_GRAVADO", "@impGrav").ConfigureAwait(false);
        await AddIfExistsAsync("IMPORTE_EXENTO", "@impExen").ConfigureAwait(false);
        await AddIfExistsAsync("EXPORTADO", "@exportado").ConfigureAwait(false);
        await AddIfExistsAsync("CONGELA", "@congela").ConfigureAwait(false);
        await AddIfExistsAsync("METODO_EXPORTACION_OC", "@metodoExp").ConfigureAwait(false);
        await AddIfExistsAsync("ID_CPA01", "@idCpa01").ConfigureAwait(false);
        await AddIfExistsAsync("ID_CPA43", "@idCpa43").ConfigureAwait(false);
        await AddIfExistsAsync("ID_CPA56", "@idCpa56").ConfigureAwait(false);

        var idCpa56 = await LookupIdAsync(
                connection, transaction,
                "SELECT TOP 1 ID_CPA56 FROM dbo.CPA56 WHERE TALONARIO=@t",
                ("@t", talonOc), timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var sql = $"""
            INSERT INTO dbo.CPA35 (
                {string.Join(", ", cols)}
            ) VALUES (
                {string.Join(", ", vals)}
            )
            """;

        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id", idCpa35);
        cmd.Parameters.AddWithValue("@talon", talonOc);
        cmd.Parameters.AddWithValue("@nOrden", nOrdenCo);
        cmd.Parameters.AddWithValue("@provee", codProvee);
        cmd.Parameters.AddWithValue("@estado", estado);
        cmd.Parameters.AddWithValue("@monCte", monCte ? 1 : 0);
        cmd.Parameters.AddWithValue("@cotiz", cotiz);
        cmd.Parameters.AddWithValue("@lista", codLista);
        cmd.Parameters.AddWithValue("@cond", condCompr);
        cmd.Parameters.AddWithValue("@compra", codCompra);
        cmd.Parameters.AddWithValue("@nroSuc", nroSucurs);
        cmd.Parameters.AddWithValue("@porcBonif", porcBonif);
        cmd.Parameters.AddWithValue("@totalBoni", totalBoni);
        cmd.Parameters.AddWithValue("@totalCte", totalCte);
        cmd.Parameters.AddWithValue("@totalExt", totalExt);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@hora", hora);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@terminal", TerminalIngreso);

        void AddOptional(string name, object? value)
        {
            if (vals.Contains(name))
            {
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        AddOptional("@codCpa01", proveedor.CodCpa01);
        AddOptional("@fecEmisio", fechaEmisio.Date);
        AddOptional("@fecVigenc", fechaVigenc?.Date);
        AddOptional("@fecGener", fechaGener.Date);
        AddOptional("@obs", observaciones);
        AddOptional("@obsCorto", observaciones.Length > 30 ? observaciones[..30] : observaciones);
        AddOptional("@ley1", leyendas.Leyenda1);
        AddOptional("@ley2", leyendas.Leyenda2);
        AddOptional("@ley3", leyendas.Leyenda3);
        AddOptional("@ley4", leyendas.Leyenda4);
        AddOptional("@ley5", leyendas.Leyenda5);
        AddOptional("@incIva", proveedor.IncIva);
        AddOptional("@incIi", proveedor.IncIi);
        AddOptional("@subCte", subtotalCte);
        AddOptional("@subExt", totalExt);
        AddOptional("@totalIva", 0m);
        AddOptional("@totalIi", 0m);
        AddOptional("@impGrav", subtotalCte);
        AddOptional("@impExen", 0m);
        AddOptional("@exportado", 0);
        AddOptional("@congela", 0);
        AddOptional("@metodoExp", "NO_CONTROLA");
        AddOptional("@idCpa01", proveedor.IdCpa01);
        AddOptional("@idCpa43", idCpa43);
        AddOptional("@idCpa56", idCpa56);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRenglonAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idCpa36,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        int nRenglonOc,
        RenglonInput renglon,
        string codDeposi,
        decimal cantPedida2,
        decimal canEqui,
        decimal totalLinea,
        int estado,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var cols = new List<string>
        {
            "ID_CPA36", "ID_CPA35", "TALONARIO", "N_ORDEN_CO", "N_RENGL_OC",
            "COD_ARTICU", "COD_DEPOSI", "CAN_PEDIDA", "CAN_PENDIE", "CAN_RECIBI",
            "PRECIO", "PORC_DCTO", "ESTADO"
        };
        var vals = new List<string>
        {
            "@id36", "@id35", "@talon", "@nOrden", "@nReng",
            "@art", "@dep", "@cant", "@cant", "@cero",
            "@precio", "@dto", "@estado"
        };

        async Task AddIfExistsAsync(string column, string paramName)
        {
            if (await ColumnExistsAsync(connection, transaction, "CPA36", column, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                cols.Add(column);
                vals.Add(paramName);
            }
        }

        await AddIfExistsAsync("CAN_PEDIDA_2", "@cant2").ConfigureAwait(false);
        await AddIfExistsAsync("CAN_PENDIE_2", "@cant2").ConfigureAwait(false);
        await AddIfExistsAsync("CAN_RECIBI_2", "@cero").ConfigureAwait(false);
        await AddIfExistsAsync("CAN_EQUIVA", "@canEqui").ConfigureAwait(false);
        await AddIfExistsAsync("CAN_PEDIDA_PANTALLA", "@cant").ConfigureAwait(false);
        await AddIfExistsAsync("PRECIO_PAN", "@precio").ConfigureAwait(false);
        await AddIfExistsAsync("TOTAL", "@total").ConfigureAwait(false);
        await AddIfExistsAsync("UNIDAD_MEDIDA_SELECCIONADA", "@unidad").ConfigureAwait(false);
        await AddIfExistsAsync("OBSERVACIONES", "@obs").ConfigureAwait(false);
        await AddIfExistsAsync("DESCRIPCION_ARTICULO", "@desc").ConfigureAwait(false);
        await AddIfExistsAsync("DESC_ADICIONAL_ARTICULO", "@descAd").ConfigureAwait(false);
        await AddIfExistsAsync("CANTIDAD_FACTURADA", "@cero").ConfigureAwait(false);
        await AddIfExistsAsync("PENDIENTE_FACTURAR", "@cant").ConfigureAwait(false);
        await AddIfExistsAsync("CIERRE", "@cero").ConfigureAwait(false);

        var sql = $"""
            INSERT INTO dbo.CPA36 (
                {string.Join(", ", cols)}
            ) VALUES (
                {string.Join(", ", vals)}
            )
            """;

        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id36", idCpa36);
        cmd.Parameters.AddWithValue("@id35", idCpa35);
        cmd.Parameters.AddWithValue("@talon", talonOc);
        cmd.Parameters.AddWithValue("@nOrden", nOrdenCo);
        cmd.Parameters.AddWithValue("@nReng", nRenglonOc);
        cmd.Parameters.AddWithValue("@art", renglon.CodArticu);
        cmd.Parameters.AddWithValue("@dep", codDeposi);
        cmd.Parameters.AddWithValue("@cant", renglon.CantPedida);
        cmd.Parameters.AddWithValue("@cero", 0m);
        cmd.Parameters.AddWithValue("@precio", renglon.Precio);
        cmd.Parameters.AddWithValue("@dto", renglon.PorcDcto);
        cmd.Parameters.AddWithValue("@estado", estado);

        void AddOptional(string name, object? value)
        {
            if (vals.Contains(name) && !cmd.Parameters.Contains(name))
            {
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
        }

        AddOptional("@cant2", cantPedida2);
        AddOptional("@canEqui", canEqui);
        AddOptional("@total", totalLinea);
        AddOptional("@unidad", renglon.UnidadMedida);
        AddOptional("@obs", renglon.Observaciones ?? string.Empty);
        var desc = renglon.Descripcion ?? string.Empty;
        var descAd = renglon.DescAdicional ?? string.Empty;
        AddOptional("@desc", desc.Length > 50 ? desc[..50] : desc);
        AddOptional("@descAd", descAd.Length > 20 ? descAd[..20] : descAd);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCpa41IfNeededAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int talonOc,
        string nOrdenCo,
        int nRenglonOc,
        RenglonInput renglon,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var desc = (renglon.Descripcion ?? string.Empty).Trim();
        var descAd = (renglon.DescAdicional ?? string.Empty).Trim();
        if (desc.Length == 0 && descAd.Length == 0)
        {
            return;
        }

        if (!await TableExistsAsync(connection, transaction, "CPA41", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasId = await ColumnExistsAsync(connection, transaction, "CPA41", "ID_CPA41", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        int? idCpa41 = null;
        if (hasId)
        {
            idCpa41 = await NextIdAsync(
                    connection, transaction, "SEQUENCE_CPA41", "CPA41", "ID_CPA41", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        var sql = hasId
            ? """
              INSERT INTO dbo.CPA41 (ID_CPA41, TALONARIO, N_ORDEN_CO, N_RENGL_OC, DESCRIP, DESC_ADICI)
              VALUES (@id, @talon, @nOrden, @nReng, @desc, @descAd)
              """
            : """
              INSERT INTO dbo.CPA41 (TALONARIO, N_ORDEN_CO, N_RENGL_OC, DESCRIP, DESC_ADICI)
              VALUES (@talon, @nOrden, @nReng, @desc, @descAd)
              """;

        // Column names may vary slightly; prefer DESCRIP/DESC_ADICI when present
        var hasDescrip = await ColumnExistsAsync(connection, transaction, "CPA41", "DESCRIP", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasDescAdici = await ColumnExistsAsync(connection, transaction, "CPA41", "DESC_ADICI", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (!hasDescrip || !hasDescAdici)
        {
            return;
        }

        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        if (hasId)
        {
            cmd.Parameters.AddWithValue("@id", idCpa41!.Value);
        }

        cmd.Parameters.AddWithValue("@talon", talonOc);
        cmd.Parameters.AddWithValue("@nOrden", nOrdenCo);
        cmd.Parameters.AddWithValue("@nReng", nRenglonOc);
        cmd.Parameters.AddWithValue("@desc", desc.Length > 30 ? desc[..30] : desc);
        cmd.Parameters.AddWithValue("@descAd", descAd.Length > 20 ? descAd[..20] : descAd);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertPlanesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idCpa35,
        int idCpa36,
        int talonOc,
        string nOrdenCo,
        int nRenglonOc,
        RenglonInput renglon,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (renglon.Planes is not { Count: > 0 })
        {
            return;
        }

        if (!await TableExistsAsync(connection, transaction, "CPA37", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasId = await ColumnExistsAsync(connection, transaction, "CPA37", "ID_CPA37", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        foreach (var (plan, pIdx) in renglon.Planes.Select((p, i) => (p, i)))
        {
            var cols = new List<string>
            {
                "TALONARIO", "N_ORDEN_CO", "N_RENGL_OC", "N_PLANE_OC", "CANTIDAD"
            };
            var vals = new List<string>
            {
                "@talon", "@nOrden", "@nReng", "@nPlane", "@cant"
            };

            async Task AddIfExistsAsync(string column, string paramName)
            {
                if (await ColumnExistsAsync(connection, transaction, "CPA37", column, timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    cols.Add(column);
                    vals.Add(paramName);
                }
            }

            if (hasId)
            {
                cols.Insert(0, "ID_CPA37");
                vals.Insert(0, "@id37");
            }

            await AddIfExistsAsync("ID_CPA35", "@id35").ConfigureAwait(false);
            await AddIfExistsAsync("ID_CPA36", "@id36").ConfigureAwait(false);
            await AddIfExistsAsync("FEC_RECEPC", "@fec").ConfigureAwait(false);
            await AddIfExistsAsync("CANTIDAD_2", "@cant2").ConfigureAwait(false);
            await AddIfExistsAsync("CANTIDAD_PANTALLA", "@cant").ConfigureAwait(false);

            var sql = $"""
                INSERT INTO dbo.CPA37 (
                    {string.Join(", ", cols)}
                ) VALUES (
                    {string.Join(", ", vals)}
                )
                """;

            await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
            if (hasId)
            {
                var idCpa37 = await NextIdAsync(
                        connection, transaction, "SEQUENCE_CPA37", "CPA37", "ID_CPA37",
                        timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
                cmd.Parameters.AddWithValue("@id37", idCpa37);
            }

            cmd.Parameters.AddWithValue("@talon", talonOc);
            cmd.Parameters.AddWithValue("@nOrden", nOrdenCo);
            cmd.Parameters.AddWithValue("@nReng", nRenglonOc);
            cmd.Parameters.AddWithValue("@nPlane", plan.NPlaneOc ?? (pIdx + 1));
            cmd.Parameters.AddWithValue("@cant", plan.Cantidad);

            void AddOptional(string name, object? value)
            {
                if (vals.Contains(name) && !cmd.Parameters.Contains(name))
                {
                    cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }
            }

            AddOptional("@id35", idCpa35);
            AddOptional("@id36", idCpa36);
            AddOptional("@fec", plan.FechaRecepc?.Date);
            AddOptional("@cant2", plan.Cantidad2);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task IncrementarSta19Async(
        SqlConnection connection,
        SqlTransaction transaction,
        int idSta11,
        int idSta22,
        decimal cantidad,
        decimal cantidad2,
        string codArticu,
        string codDeposi,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "STA19", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        if (!await ColumnExistsAsync(connection, transaction, "STA19", "CANT_PEND", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasCantPend2 = await ColumnExistsAsync(
                connection, transaction, "STA19", "CANT_PEND_2", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCodArt = await ColumnExistsAsync(
                connection, transaction, "STA19", "COD_ARTICU", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCodDep = await ColumnExistsAsync(
                connection, transaction, "STA19", "COD_DEPOSI", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var setPend2 = hasCantPend2
            ? ", CANT_PEND_2 = CASE WHEN ISNULL(CANT_PEND_2, 0) + @cant2 < 0 THEN 0 ELSE ISNULL(CANT_PEND_2, 0) + @cant2 END"
            : string.Empty;

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            $"""
            UPDATE dbo.STA19
            SET CANT_PEND = CASE WHEN ISNULL(CANT_PEND, 0) + @cant < 0 THEN 0 ELSE ISNULL(CANT_PEND, 0) + @cant END
                {setPend2}
            WHERE ID_STA11 = @a AND ID_STA22 = @d
            """);
        upd.Parameters.AddWithValue("@cant", cantidad);
        upd.Parameters.AddWithValue("@cant2", cantidad2);
        upd.Parameters.AddWithValue("@a", idSta11);
        upd.Parameters.AddWithValue("@d", idSta22);
        var n = await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (n > 0)
        {
            return;
        }

        var insertCols = new List<string> { "ID_STA11", "ID_STA22", "CANT_PEND" };
        var insertVals = new List<string> { "@a", "@d", "@cant" };
        if (hasCantPend2)
        {
            insertCols.Add("CANT_PEND_2");
            insertVals.Add("@cant2");
        }

        if (hasCodArt && !string.IsNullOrWhiteSpace(codArticu))
        {
            insertCols.Add("COD_ARTICU");
            insertVals.Add("@art");
        }

        if (hasCodDep && !string.IsNullOrWhiteSpace(codDeposi))
        {
            insertCols.Add("COD_DEPOSI");
            insertVals.Add("@dep");
        }

        await using var ins = CreateCommand(
            connection, transaction, timeoutSeconds,
            $"""
            INSERT INTO dbo.STA19 ({string.Join(", ", insertCols)})
            VALUES ({string.Join(", ", insertVals)})
            """);
        ins.Parameters.AddWithValue("@a", idSta11);
        ins.Parameters.AddWithValue("@d", idSta22);
        ins.Parameters.AddWithValue("@cant", cantidad);
        if (hasCantPend2)
        {
            ins.Parameters.AddWithValue("@cant2", Math.Max(0m, cantidad2));
        }

        if (hasCodArt && !string.IsNullOrWhiteSpace(codArticu))
        {
            ins.Parameters.AddWithValue("@art", codArticu);
        }

        if (hasCodDep && !string.IsNullOrWhiteSpace(codDeposi))
        {
            ins.Parameters.AddWithValue("@dep", codDeposi);
        }

        await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TalonarioOcOkAsync(
        SqlConnection connection, SqlTransaction transaction, int talonOc, int timeoutSeconds, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1 1 FROM dbo.CPA56
            WHERE TALONARIO = @t
              AND UPPER(LTRIM(RTRIM(CAST(TIPO_COMP AS NVARCHAR(10))))) = N'O'
            """);
        cmd.Parameters.AddWithValue("@t", talonOc);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<bool> TalonarioPermiteEditarNroAsync(
        SqlConnection connection, SqlTransaction transaction, int talonOc, int timeoutSeconds, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(connection, transaction, "CPA56", "EDITA_NRO", timeoutSeconds, ct)
                .ConfigureAwait(false))
        {
            return false;
        }

        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            "SELECT TOP 1 EDITA_NRO FROM dbo.CPA56 WHERE TALONARIO=@t");
        cmd.Parameters.AddWithValue("@t", talonOc);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o switch
        {
            null or DBNull => false,
            bool b => b,
            byte by => by != 0,
            short s => s != 0,
            int i => i != 0,
            string s => s.Trim() is "1" or "S" or "s" or "true" or "True",
            _ => Convert.ToInt32(o) != 0
        };
    }

    private static async Task<ProveedorInfo?> ResolveProveedorAsync(
        SqlConnection connection, SqlTransaction transaction, string codProvee, int timeoutSeconds, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1 *
            FROM dbo.CPA01
            WHERE LTRIM(RTRIM(CAST(COD_PROVEE AS NVARCHAR(20)))) = @c
            """);
        cmd.Parameters.AddWithValue("@c", codProvee);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        int? idCpa01 = null;
        try
        {
            var ord = reader.GetOrdinal("ID_CPA01");
            if (!reader.IsDBNull(ord))
            {
                idCpa01 = Convert.ToInt32(reader.GetValue(ord));
            }
        }
        catch
        {
            // optional
        }

        string codCpa01 = codProvee;
        try
        {
            var ord = reader.GetOrdinal("COD_CPA01");
            if (!reader.IsDBNull(ord))
            {
                var s = reader.GetValue(ord)?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    codCpa01 = s;
                }
            }
        }
        catch
        {
            // optional
        }

        int? condCompr = null;
        try
        {
            var ord = reader.GetOrdinal("COND_COMPR");
            if (!reader.IsDBNull(ord))
            {
                condCompr = Convert.ToInt32(reader.GetValue(ord));
            }
        }
        catch
        {
            // optional
        }

        var incIva = ReadStringLoose(reader, "INC_IVA_LI") ?? ReadStringLoose(reader, "INC_IVA") ?? "N";
        var incIi = ReadStringLoose(reader, "INC_II_LIS") ?? ReadStringLoose(reader, "INC_II") ?? "N";
        await reader.CloseAsync().ConfigureAwait(false);

        return new ProveedorInfo(idCpa01, codCpa01, condCompr, incIva, incIi);
    }

    private static string? ReadStringLoose(SqlDataReader reader, string column)
    {
        try
        {
            var ord = reader.GetOrdinal(column);
            if (reader.IsDBNull(ord))
            {
                return null;
            }

            var s = reader.GetValue(ord)?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> OrdenExisteAsync(
        SqlConnection c, SqlTransaction t, int talon, string nOrden, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            """
            SELECT TOP 1 1 FROM dbo.CPA35
            WHERE TALONARIO=@t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50))))=@n
            """);
        cmd.Parameters.AddWithValue("@t", talon);
        cmd.Parameters.AddWithValue("@n", nOrden);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<int> ReadEstadoInicialAsync(
        SqlConnection c, SqlTransaction t, int timeout, CancellationToken ct)
    {
        if (!await TableExistsAsync(c, t, "CPA10", timeout, ct).ConfigureAwait(false))
        {
            return 1;
        }

        if (!await ColumnExistsAsync(c, t, "CPA10", "AUTORIZ_OC", timeout, ct).ConfigureAwait(false))
        {
            return 1;
        }

        await using var cmd = CreateCommand(c, t, timeout, "SELECT TOP 1 AUTORIZ_OC FROM dbo.CPA10");
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var autoriz = (o?.ToString() ?? "S").Trim().ToUpperInvariant();
        return autoriz == "N" ? 2 : 1;
    }

    private static async Task<int> NextIdAsync(
        SqlConnection c, SqlTransaction t, string sequence, string table, string idCol, int timeout, CancellationToken ct)
    {
        try
        {
            await using var cmd = CreateCommand(c, t, timeout, $"SELECT NEXT VALUE FOR dbo.{sequence}");
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(o);
        }
        catch
        {
            await using var cmd = CreateCommand(c, t, timeout, $"SELECT ISNULL(MAX({idCol}),0)+1 FROM dbo.{table}");
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(o);
        }
    }

    private static async Task<int?> LookupIdAsync(
        SqlConnection c, SqlTransaction t, string sql, (string name, object value) param, int timeout, CancellationToken ct)
    {
        try
        {
            await using var cmd = CreateCommand(c, t, timeout, sql);
            cmd.Parameters.AddWithValue(param.name, param.value);
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return o is null or DBNull ? null : Convert.ToInt32(o);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<decimal?> LookupDecimalAsync(
        SqlConnection c, SqlTransaction t, string sql, (string name, object value) param, int timeout, CancellationToken ct)
    {
        try
        {
            await using var cmd = CreateCommand(c, t, timeout, sql);
            cmd.Parameters.AddWithValue(param.name, param.value);
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return o is null or DBNull ? null : Convert.ToDecimal(o);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT 1 FROM sys.columns c INNER JOIN sys.tables tb ON tb.object_id=c.object_id WHERE tb.name=@t AND c.name=@c");
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static async Task<byte[]?> ScalarBytesAsync(
        SqlConnection c, SqlTransaction t, string sql, (string name, object value) param, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue(param.name, param.value);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o as byte[];
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection, SqlTransaction transaction, int timeoutSeconds, string sql) =>
        new(sql, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };

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
            JsonElement je => je.ToString(),
            _ => raw.ToString()
        };
    }

    private static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            int i => i,
            long l => (int)l,
            decimal d => (int)d,
            string s when int.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) => i,
            JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var p) => p,
            _ => null
        };
    }

    private static decimal? ExtractDecimal(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDecimal(),
            JsonElement je when je.ValueKind == JsonValueKind.String
                && decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => null
        };
    }

    private static bool? ExtractBool(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            bool b => b,
            int i => i != 0,
            string s when bool.TryParse(s, out var p) => p,
            string s when s is "1" or "S" or "s" => true,
            string s when s is "0" or "N" or "n" => false,
            JsonElement je when je.ValueKind is JsonValueKind.True or JsonValueKind.False => je.GetBoolean(),
            _ => null
        };
    }

    private static DateTime? ExtractDate(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        var s = ExtractString(parameters, key);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt
            : null;
    }

    private static string? GetJsonString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetJsonInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(p.GetString(), out var i) => i,
            _ => null
        };
    }

    private static decimal? GetJsonDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static DateTime? GetJsonDate(JsonElement el, string name)
    {
        var s = GetJsonString(el, name);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt
            : null;
    }

    private static OrdenesCompraOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static OrdenesCompraOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
