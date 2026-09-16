using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.OrdenesCompra;

/// <summary>
/// D6.4.3 MVP — PATCH Órdenes de compra (espejo simplificado de OrdenesCompraPatchService).
/// Tras commit de TX, respuesta = orden completa vía Get.
/// <para>
/// Fuera de alcance MVP: cambio de nOrdenCo, alta/baja completa de renglones,
/// replace de planes de entrega, perfil CPA104.
/// </para>
/// </summary>
public sealed class OrdenesCompraUpdateRunner
{
    private readonly OrdenesCompraGatewayRunner getRunner;

    public OrdenesCompraUpdateRunner(OrdenesCompraGatewayRunner getRunner)
    {
        this.getRunner = getRunner;
    }

    public async Task<OrdenesCompraOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonOc = ExtractInt(parameters, "talon_oc");
        var nOrdenCo = ExtractString(parameters, "n_orden_co")?.Trim();

        if (string.IsNullOrWhiteSpace(database) || talonOc is null or <= 0 || string.IsNullOrWhiteSpace(nOrdenCo))
        {
            return Fail("INVALID_PARAMETERS", "talon_oc, n_orden_co y _database son obligatorios.");
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

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var patchOutcome = await ExecutePatchAsync(
                        connection,
                        transaction,
                        parameters,
                        talonOc.Value,
                        nOrdenCo,
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (patchOutcome.Status != JobStatuses.Success)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return patchOutcome;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            if (!OrdenesCompraCatalog.TryGet("OrdenesCompra.Get", out var getDef))
            {
                return Fail("INTERNAL_ERROR", "OrdenesCompra.Get no registrado en catalogo.");
            }

            return await getRunner
                .RunAsync(
                    getDef,
                    agentOptions,
                    new Dictionary<string, object?>
                    {
                        ["_database"] = database,
                        ["talon_oc"] = talonOc.Value,
                        ["n_orden_co"] = nOrdenCo
                    },
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<OrdenesCompraOutcome> ExecutePatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonOc,
        string nOrdenCo,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var hasRowVersion = await ColumnExistsAsync(
                connection, transaction, "CPA35", "ROW_VERSION", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasRowVersion
                ? """
                  SELECT TOP 1
                      CAST(ID_CPA35 AS INT) AS idCpa35,
                      CAST(ESTADO AS INT) AS estado,
                      ROW_VERSION AS rowVersion
                  FROM dbo.CPA35
                  WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
                  """
                : """
                  SELECT TOP 1
                      CAST(ID_CPA35 AS INT) AS idCpa35,
                      CAST(ESTADO AS INT) AS estado
                  FROM dbo.CPA35
                  WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
                  """);
        load.Parameters.AddWithValue("@t", talonOc);
        load.Parameters.AddWithValue("@n", nOrdenCo);

        int idCpa35;
        int estado;
        byte[]? rowVersionBytes = null;

        await using (var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return Fail("NOT_FOUND", "Orden de compra no encontrada.");
            }

            idCpa35 = reader.GetInt32(0);
            estado = reader.GetInt32(1);
            if (hasRowVersion && !reader.IsDBNull(2))
            {
                rowVersionBytes = (byte[])reader.GetValue(2);
            }
        }

        if (estado >= 4)
        {
            return Fail("ESTADO_NO_EDITABLE", "Estado de la orden no permite modificacion.");
        }

        var modiOc = await ReadCpa10BoolAsync(
                connection, transaction, "MODI_OC", defaultValue: true, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (!modiOc)
        {
            return Fail("MODI_OC_BLOQUEADO", "El parametro MODI_OC no permite modificar la orden.");
        }

        if (hasRowVersion)
        {
            var clientRv = ExtractString(parameters, "row_version");
            if (string.IsNullOrWhiteSpace(clientRv)
                || rowVersionBytes is null
                || !string.Equals(Convert.ToBase64String(rowVersionBytes), clientRv.Trim(), StringComparison.Ordinal))
            {
                return Fail("ROW_VERSION_CONFLICT", "Conflicto de concurrencia (rowVersion).");
            }
        }

        if (estado == 3)
        {
            return await PatchSoloPreciosAsync(
                    connection, transaction, parameters, idCpa35, talonOc, nOrdenCo, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        await PatchCabeceraAsync(
                connection, transaction, parameters, talonOc, nOrdenCo, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var renglonesJson = ExtractString(parameters, "renglones_json");
        if (!string.IsNullOrWhiteSpace(renglonesJson))
        {
            List<RenglonPatch> renglones;
            try
            {
                renglones = ParseRenglonesPatch(renglonesJson);
            }
            catch (Exception ex)
            {
                return Fail("INVALID_PARAMETERS", "renglones_json invalido: " + ex.Message);
            }

            foreach (var line in renglones)
            {
                var lineOutcome = await PatchRenglonAsync(
                        connection, transaction, line, idCpa35, talonOc, nOrdenCo, estado,
                        timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
                if (lineOutcome is not null)
                {
                    return lineOutcome;
                }
            }
        }

        return Ok(new Dictionary<string, object?> { ["patched"] = true });
    }

    private static async Task PatchCabeceraAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonOc,
        string nOrdenCo,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var setParts = new List<string>();
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, "SELECT 1");

        if (parameters.ContainsKey("observaciones")
            && await ColumnExistsAsync(connection, transaction, "CPA35", "OBSERVACIONES", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("OBSERVACIONES = @obs");
            cmd.Parameters.AddWithValue("@obs", (object?)ExtractString(parameters, "observaciones") ?? DBNull.Value);
        }

        if (parameters.ContainsKey("fecha_vigenc")
            && await ColumnExistsAsync(connection, transaction, "CPA35", "FEC_VIGENC", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("FEC_VIGENC = @fecVigenc");
            var fecha = ExtractDate(parameters, "fecha_vigenc");
            cmd.Parameters.AddWithValue("@fecVigenc", (object?)fecha?.Date ?? DBNull.Value);
        }

        if (parameters.ContainsKey("porc_bonif")
            && await ColumnExistsAsync(connection, transaction, "CPA35", "PORC_BONIF", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("PORC_BONIF = @porcBonif");
            cmd.Parameters.AddWithValue("@porcBonif", (object?)ExtractDecimal(parameters, "porc_bonif") ?? 0m);
        }

        var now = DateTime.Now;
        if (await ColumnExistsAsync(
                    connection, transaction, "CPA35", "HORA_ULTIMA_MODIFICACION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("HORA_ULTIMA_MODIFICACION = @horaUlt");
            cmd.Parameters.AddWithValue("@horaUlt", now.ToString("HHmmss"));
        }

        if (await ColumnExistsAsync(
                    connection, transaction, "CPA35", "FECHA_ULTIMA_MODIFICACION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("FECHA_ULTIMA_MODIFICACION = @fechaUlt");
            cmd.Parameters.AddWithValue("@fechaUlt", now);
        }

        if (setParts.Count == 0)
        {
            return;
        }

        cmd.CommandText = $"""
            UPDATE dbo.CPA35
            SET {string.Join(", ", setParts)}
            WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
            """;
        cmd.Parameters.AddWithValue("@t", talonOc);
        cmd.Parameters.AddWithValue("@n", nOrdenCo);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OrdenesCompraOutcome> PatchSoloPreciosAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var modificaPrecio = await ReadCpa10BoolAsync(
                connection, transaction, "MODIFICA_PRECIO_OC", defaultValue: true, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (!modificaPrecio)
        {
            return Fail("PRECIO_NO_MODIFICABLE", "El parametro no permite modificar precios.");
        }

        var renglonesJson = ExtractString(parameters, "renglones_json");
        if (string.IsNullOrWhiteSpace(renglonesJson))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "En estado Emitida solo se pueden modificar precios de renglones (renglones_json).");
        }

        List<RenglonPatch> renglones;
        try
        {
            renglones = ParseRenglonesPatch(renglonesJson);
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", "renglones_json invalido: " + ex.Message);
        }

        var conPrecio = renglones.Where(r => r.Precio is not null && (r.IdCpa36 is > 0 || r.NRenglonOc is > 0)).ToList();
        if (conPrecio.Count == 0)
        {
            return Fail("INVALID_PARAMETERS", "Informe renglones con idCpa36/nRenglonOc y precio.");
        }

        var hasPendFac = await ColumnExistsAsync(
                connection, transaction, "CPA36", "PENDIENTE_FACTURAR", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        foreach (var line in conPrecio)
        {
            var dbRow = await FindRenglonAsync(
                    connection, transaction, idCpa35, talonOc, nOrdenCo, line, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (dbRow is null)
            {
                return Fail("RENGLON_INEXISTENTE", "No se encontro el renglon.");
            }

            if (hasPendFac && dbRow.PendienteFacturar <= 0m)
            {
                return Fail("RENGLON_FACTURADO", "No se puede modificar precio de renglon 100% facturado.");
            }

            await using var upd = CreateCommand(
                connection, transaction, timeoutSeconds,
                "UPDATE dbo.CPA36 SET PRECIO = @precio WHERE ID_CPA36 = @id");
            upd.Parameters.AddWithValue("@precio", line.Precio!.Value);
            upd.Parameters.AddWithValue("@id", dbRow.IdCpa36);
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return Ok(new Dictionary<string, object?> { ["patched"] = true });
    }

    private static async Task<OrdenesCompraOutcome?> PatchRenglonAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        RenglonPatch line,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        int estadoCabecera,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (line.IdCpa36 is not > 0 && line.NRenglonOc is not > 0)
        {
            return null;
        }

        var dbRow = await FindRenglonAsync(
                connection, transaction, idCpa35, talonOc, nOrdenCo, line, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (dbRow is null)
        {
            return null;
        }

        var cantAnterior = dbRow.CanPedida;
        var cantRecibi = dbRow.CanRecibi;
        var nuevaCant = line.CantPedida ?? cantAnterior;

        if (line.CantPedida is not null && nuevaCant + 0.0001m < cantRecibi)
        {
            return Fail("INVALID_PARAMETERS", "cantPedida no puede ser menor a cantRecibi.");
        }

        var setParts = new List<string>();
        await using var upd = CreateCommand(connection, transaction, timeoutSeconds, "SELECT 1");

        if (line.CantPedida is not null && Math.Abs(nuevaCant - cantAnterior) >= 0.0001m)
        {
            setParts.Add("CAN_PEDIDA = @cant");
            upd.Parameters.AddWithValue("@cant", nuevaCant);

            if (await ColumnExistsAsync(connection, transaction, "CPA36", "CAN_PENDIE", timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                setParts.Add("CAN_PENDIE = @pend");
                upd.Parameters.AddWithValue("@pend", Math.Max(0m, nuevaCant - cantRecibi));
            }

            if (await ColumnExistsAsync(
                        connection, transaction, "CPA36", "PENDIENTE_FACTURAR", timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                setParts.Add("PENDIENTE_FACTURAR = @pendFac");
                upd.Parameters.AddWithValue("@pendFac", Math.Max(0m, nuevaCant - dbRow.CantidadFacturada));
            }
        }

        if (line.Precio is not null)
        {
            var modificaPrecio = await ReadCpa10BoolAsync(
                    connection, transaction, "MODIFICA_PRECIO_OC", defaultValue: true, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (!modificaPrecio)
            {
                return Fail("PRECIO_NO_MODIFICABLE", "El parametro no permite modificar precios.");
            }

            setParts.Add("PRECIO = @precio");
            upd.Parameters.AddWithValue("@precio", line.Precio.Value);
        }

        if (line.PorcDcto is not null)
        {
            setParts.Add("PORC_DCTO = @dto");
            upd.Parameters.AddWithValue("@dto", line.PorcDcto.Value);
        }

        if (setParts.Count > 0)
        {
            upd.CommandText = $"""
                UPDATE dbo.CPA36
                SET {string.Join(", ", setParts)}
                WHERE ID_CPA36 = @id
                """;
            upd.Parameters.AddWithValue("@id", dbRow.IdCpa36);
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var delta = nuevaCant - cantAnterior;
        var estadoLinea = dbRow.Estado ?? estadoCabecera;
        if (Math.Abs(delta) >= 0.0001m
            && estadoLinea <= 3
            && !string.IsNullOrWhiteSpace(dbRow.CodArticu))
        {
            var idSta11 = await LookupIdAsync(
                    connection, transaction,
                    "SELECT TOP 1 ID_STA11 FROM dbo.STA11 WHERE LTRIM(RTRIM(COD_ARTICU))=@a",
                    ("@a", dbRow.CodArticu), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            int? idSta22 = null;
            if (!string.IsNullOrWhiteSpace(dbRow.CodDeposi))
            {
                idSta22 = await LookupIdAsync(
                        connection, transaction,
                        """
                        SELECT TOP 1 ID_STA22 FROM dbo.STA22
                        WHERE LTRIM(RTRIM(COD_STA22))=LTRIM(RTRIM(@d))
                           OR LTRIM(RTRIM(COD_SUCURS))=LTRIM(RTRIM(@d))
                        """,
                        ("@d", dbRow.CodDeposi), timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (idSta11 is not null && idSta22 is not null)
            {
                var delta2 = 0m;
                if (cantAnterior > 0m && dbRow.CanPedida2 != 0m)
                {
                    delta2 = delta * (dbRow.CanPedida2 / cantAnterior);
                }

                await AjustarSta19Async(
                        connection, transaction,
                        idSta11.Value, idSta22.Value, delta, delta2,
                        dbRow.CodArticu, dbRow.CodDeposi ?? string.Empty,
                        timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return null;
    }

    private sealed record RenglonDb(
        int IdCpa36,
        decimal CanPedida,
        decimal CanPedida2,
        decimal CanRecibi,
        decimal PendienteFacturar,
        decimal CantidadFacturada,
        int? Estado,
        string? CodArticu,
        string? CodDeposi);

    private static async Task<RenglonDb?> FindRenglonAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        RenglonPatch line,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var hasPendFac = await ColumnExistsAsync(
                connection, transaction, "CPA36", "PENDIENTE_FACTURAR", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCantFac = await ColumnExistsAsync(
                connection, transaction, "CPA36", "CANTIDAD_FACTURADA", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCant2 = await ColumnExistsAsync(
                connection, transaction, "CPA36", "CAN_PEDIDA_2", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasEstado = await ColumnExistsAsync(
                connection, transaction, "CPA36", "ESTADO", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var pendFacExpr = hasPendFac ? "CAST(ISNULL(PENDIENTE_FACTURAR, 0) AS FLOAT)" : "1";
        var cantFacExpr = hasCantFac ? "CAST(ISNULL(CANTIDAD_FACTURADA, 0) AS FLOAT)" : "0";
        var cant2Expr = hasCant2 ? "CAST(ISNULL(CAN_PEDIDA_2, 0) AS FLOAT)" : "0";
        var estadoExpr = hasEstado ? "CAST(ESTADO AS INT)" : "CAST(NULL AS INT)";

        string sql;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, "SELECT 1");

        if (line.IdCpa36 is > 0)
        {
            sql = $"""
                SELECT TOP 1
                    CAST(ID_CPA36 AS INT),
                    CAST(ISNULL(CAN_PEDIDA, 0) AS FLOAT),
                    {cant2Expr},
                    CAST(ISNULL(CAN_RECIBI, 0) AS FLOAT),
                    {pendFacExpr},
                    {cantFacExpr},
                    {estadoExpr},
                    LTRIM(RTRIM(CAST(COD_ARTICU AS NVARCHAR(50)))),
                    LTRIM(RTRIM(CAST(COD_DEPOSI AS NVARCHAR(50))))
                FROM dbo.CPA36
                WHERE ID_CPA36 = @id
                """;
            cmd.Parameters.AddWithValue("@id", line.IdCpa36.Value);
        }
        else if (line.NRenglonOc is > 0)
        {
            sql = $"""
                SELECT TOP 1
                    CAST(ID_CPA36 AS INT),
                    CAST(ISNULL(CAN_PEDIDA, 0) AS FLOAT),
                    {cant2Expr},
                    CAST(ISNULL(CAN_RECIBI, 0) AS FLOAT),
                    {pendFacExpr},
                    {cantFacExpr},
                    {estadoExpr},
                    LTRIM(RTRIM(CAST(COD_ARTICU AS NVARCHAR(50)))),
                    LTRIM(RTRIM(CAST(COD_DEPOSI AS NVARCHAR(50))))
                FROM dbo.CPA36
                WHERE N_RENGL_OC = @nReng
                  AND (
                        ID_CPA35 = @id35
                     OR (TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n)
                  )
                """;
            cmd.Parameters.AddWithValue("@nReng", line.NRenglonOc.Value);
            cmd.Parameters.AddWithValue("@id35", idCpa35);
            cmd.Parameters.AddWithValue("@t", talonOc);
            cmd.Parameters.AddWithValue("@n", nOrdenCo);
        }
        else
        {
            return null;
        }

        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RenglonDb(
            reader.GetInt32(0),
            Convert.ToDecimal(reader.GetDouble(1)),
            Convert.ToDecimal(reader.GetDouble(2)),
            Convert.ToDecimal(reader.GetDouble(3)),
            Convert.ToDecimal(reader.GetDouble(4)),
            Convert.ToDecimal(reader.GetDouble(5)),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static async Task AjustarSta19Async(
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
        if (n > 0 || cantidad <= 0m)
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
        ins.Parameters.AddWithValue("@cant", Math.Max(0m, cantidad));
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

    private sealed record RenglonPatch(
        int? IdCpa36,
        int? NRenglonOc,
        decimal? CantPedida,
        decimal? Precio,
        decimal? PorcDcto);

    private static List<RenglonPatch> ParseRenglonesPatch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("se esperaba un array JSON");
        }

        var list = new List<RenglonPatch>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new RenglonPatch(
                GetJsonInt(el, "idCpa36") ?? GetJsonInt(el, "id_cpa36"),
                GetJsonInt(el, "nRenglonOc") ?? GetJsonInt(el, "n_renglon_oc") ?? GetJsonInt(el, "n_rengl_oc"),
                GetJsonDecimal(el, "cantPedida") ?? GetJsonDecimal(el, "cant_pedida"),
                GetJsonDecimal(el, "precio"),
                GetJsonDecimal(el, "porcDcto") ?? GetJsonDecimal(el, "porc_dcto")));
        }

        return list;
    }

    private static async Task<bool> ReadCpa10BoolAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string column,
        bool defaultValue,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "CPA10", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return defaultValue;
        }

        if (!await ColumnExistsAsync(connection, transaction, "CPA10", column, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return defaultValue;
        }

        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, $"SELECT TOP 1 {column} FROM dbo.CPA10");
        var o = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return o switch
        {
            null or DBNull => defaultValue,
            bool b => b,
            byte by => by != 0,
            short s => s != 0,
            int i => i != 0,
            string s => s.Trim() is not ("0" or "N" or "n" or "false" or "False"),
            _ => Convert.ToInt32(o) != 0
        };
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

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT 1 FROM sys.columns sc INNER JOIN sys.tables st ON st.object_id=sc.object_id WHERE st.name=@t AND sc.name=@c");
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static SqlCommand CreateCommand(SqlConnection c, SqlTransaction t, int timeout, string sql) =>
        new(sql, c, t) { CommandType = CommandType.Text, CommandTimeout = Math.Max(1, timeout) };

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

    private static DateTime? ExtractDate(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        var s = ExtractString(parameters, key);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt
            : null;
    }

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

    private static OrdenesCompraOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static OrdenesCompraOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
