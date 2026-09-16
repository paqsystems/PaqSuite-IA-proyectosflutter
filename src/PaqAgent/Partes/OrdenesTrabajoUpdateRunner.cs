using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.8.4 — update Órdenes de trabajo orquestado (espejo PHP update / updateByCodigo).
/// Modo por id_orden_trabajo o por codigo_ot (sync multi-op). Sin numeración. Sin SP monolítico.
/// </summary>
public sealed class OrdenesTrabajoUpdateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "_database");
        var idOrden = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo");
        var codigoOt = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "codigo_ot")?.Trim();

        if (string.IsNullOrWhiteSpace(database))
        {
            return Fail("INVALID_PARAMETERS", "_database es obligatorio.");
        }

        var hasId = idOrden is > 0;
        var hasCodigo = !string.IsNullOrWhiteSpace(codigoOt);
        if (!hasId && !hasCodigo)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "Debe indicar id_orden_trabajo o codigo_ot.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new PartesOutcome
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
                if (!await OrdenesTrabajoRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new OrdenesTrabajoRunnerHelpers.ValidationException("Tabla PQ_PRD_ORDENES_TRABAJO no disponible.");
                }

                if (!await OrdenesTrabajoRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_OPERACIONES", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new OrdenesTrabajoRunnerHelpers.ValidationException("Catálogo de operaciones no disponible.");
                }

                PartesOutcome outcome;
                if (hasCodigo)
                {
                    outcome = await UpdateByCodigoAsync(
                            connection, transaction, parameters, codigoOt!, timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    outcome = await UpdateByIdAsync(
                            connection, transaction, parameters, idOrden!.Value, timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (outcome.Status != JobStatuses.Success)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return outcome;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return outcome;
            }
            catch (OrdenesTrabajoRunnerHelpers.ConflictException cex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Fail("CONFLICT", cex.Message);
            }
            catch (OrdenesTrabajoRunnerHelpers.ValidationException vex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Fail("VALIDATION", vex.Message);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (OrdenesTrabajoRunnerHelpers.ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
        }
        catch (OrdenesTrabajoRunnerHelpers.ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<PartesOutcome> UpdateByIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int idOrden,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var idArticulo = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "id_articulo");
        var idOperacion = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "id_operacion");
        if (idArticulo is null or <= 0 || idOperacion is null or <= 0)
        {
            return Fail("INVALID_PARAMETERS", "id_articulo e id_operacion son obligatorios.");
        }

        const string loadSql = """
            SELECT CAST(ESTADO AS INT) AS estado,
                   FECHA_INICIO_PLAN AS fecha_inicio,
                   FECHA_FIN_PLAN AS fecha_fin
            FROM dbo.PQ_PRD_ORDENES_TRABAJO
            WHERE ID_ORDEN_TRABAJO = @id
            """;
        int estado;
        string? fechaIniActual;
        string? fechaFinActual;
        await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, loadSql))
        {
            cmd.Parameters.AddWithValue("@id", idOrden);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return Fail("NOT_FOUND", "Orden de trabajo no encontrada");
            }

            estado = Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
            fechaIniActual = OrdenesTrabajoRunnerHelpers.FormatDateOnly(reader["fecha_inicio"]);
            fechaFinActual = OrdenesTrabajoRunnerHelpers.FormatDateOnly(reader["fecha_fin"]);
        }

        if (estado is not (0 or 1))
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "Solo se pueden editar OT en estado Borrador o Abierta");
        }

        if (!await OrdenesTrabajoRunnerHelpers.OperacionActivaExistsAsync(
                connection, transaction, timeoutSeconds, idOperacion.Value, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                $"La operación {idOperacion.Value} no existe o no está activa.");
        }

        if (!await OrdenesTrabajoRunnerHelpers.ArticuloExistsAsync(
                connection, transaction, timeoutSeconds, idArticulo.Value, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "El artículo seleccionado no existe en el catálogo.");
        }

        var fechaInicio = parameters.ContainsKey("fecha_inicio_plan")
            ? OrdenesTrabajoRunnerHelpers.NormalizeDate(
                OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "fecha_inicio_plan"))
            : fechaIniActual;
        var fechaFin = parameters.ContainsKey("fecha_fin_plan")
            ? OrdenesTrabajoRunnerHelpers.NormalizeDate(
                OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "fecha_fin_plan"))
            : fechaFinActual;
        if (fechaInicio is not null && fechaFin is not null
            && string.CompareOrdinal(fechaFin, fechaInicio) < 0)
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "La fecha fin plan debe ser mayor o igual a la fecha inicio plan.");
        }

        var fechaRef = fechaInicio ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!await OrdenesTrabajoRunnerHelpers.ParStdVigenteAsync(
                connection, transaction, timeoutSeconds, idArticulo.Value, idOperacion.Value, fechaRef, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "La operación no está habilitada para el artículo seleccionado (estándar vigente).");
        }

        var descripcion = OrdenesTrabajoRunnerHelpers.Truncate(
            OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "descripcion")?.Trim(), 200);
        var observaciones = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "observaciones")?.Trim();
        var cantidad = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "cantidad_a_producir");
        var usuarioId = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;
        var hasNroOrden = await OrdenesTrabajoRunnerHelpers.ColumnExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", "NRO_ORDEN", cancellationToken)
            .ConfigureAwait(false);

        var setParts = new List<string>
        {
            "ID_ARTICULO = @idArticulo",
            "ID_OPERACION = @idOperacion",
            "FECHA_MODIF = GETDATE()",
            "USUARIO_MODIF = @usuario"
        };
        if (parameters.ContainsKey("descripcion"))
        {
            setParts.Add("DESCRIPCION = @descripcion");
        }

        if (cantidad is > 0)
        {
            setParts.Add("CANTIDAD_A_PRODUCIR = @cantidad");
        }

        if (parameters.ContainsKey("fecha_inicio_plan"))
        {
            setParts.Add("FECHA_INICIO_PLAN = @fechaInicio");
        }

        if (parameters.ContainsKey("fecha_fin_plan"))
        {
            setParts.Add("FECHA_FIN_PLAN = @fechaFin");
        }

        if (parameters.ContainsKey("observaciones"))
        {
            setParts.Add("OBSERVACIONES = @observaciones");
        }

        var updateSql = $"UPDATE dbo.PQ_PRD_ORDENES_TRABAJO SET {string.Join(", ", setParts)} WHERE ID_ORDEN_TRABAJO = @id";
        await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, updateSql))
        {
            cmd.Parameters.AddWithValue("@id", idOrden);
            cmd.Parameters.AddWithValue("@idArticulo", idArticulo.Value);
            cmd.Parameters.AddWithValue("@idOperacion", idOperacion.Value);
            cmd.Parameters.AddWithValue("@usuario", usuarioId);
            if (parameters.ContainsKey("descripcion"))
            {
                cmd.Parameters.AddWithValue("@descripcion", (object?)descripcion ?? DBNull.Value);
            }

            if (cantidad is > 0)
            {
                cmd.Parameters.AddWithValue("@cantidad", cantidad.Value);
            }

            if (parameters.ContainsKey("fecha_inicio_plan"))
            {
                cmd.Parameters.AddWithValue("@fechaInicio", (object?)fechaInicio ?? DBNull.Value);
            }

            if (parameters.ContainsKey("fecha_fin_plan"))
            {
                cmd.Parameters.AddWithValue("@fechaFin", (object?)fechaFin ?? DBNull.Value);
            }

            if (parameters.ContainsKey("observaciones"))
            {
                cmd.Parameters.AddWithValue(
                    "@observaciones",
                    string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones);
            }

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Commit happens in caller; reload after commit uses same connection still in TX — fine.
        var items = await OrdenesTrabajoRunnerHelpers.LoadItemsByIdsAsync(
                connection, transaction, timeoutSeconds, [idOrden], hasNroOrden, cancellationToken)
            .ConfigureAwait(false);
        if (items.Count == 0)
        {
            return Fail("SQL_ERROR", "No se pudo recuperar la orden de trabajo actualizada.");
        }

        return Ok(items[0]);
    }

    private static async Task<PartesOutcome> UpdateByCodigoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        string codigoOt,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var idArticulo = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "id_articulo");
        var cantidad = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "cantidad_a_producir");
        if (idArticulo is null or <= 0 || cantidad is null or <= 0)
        {
            return Fail("INVALID_PARAMETERS", "id_articulo y cantidad_a_producir son obligatorios.");
        }

        var filas = new List<(int Id, int IdOperacion, int Estado, int IdArticulo, int NroOrden)>();
        var hasNroOrden = await OrdenesTrabajoRunnerHelpers.ColumnExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", "NRO_ORDEN", cancellationToken)
            .ConfigureAwait(false);
        var nroSelect = hasNroOrden
            ? "CAST(ISNULL(NRO_ORDEN, 0) AS INT) AS nro"
            : "CAST(0 AS INT) AS nro";
        var loadSql = $"""
            SELECT CAST(ID_ORDEN_TRABAJO AS INT) AS id,
                   CAST(ISNULL(ID_OPERACION, 0) AS INT) AS id_op,
                   CAST(ESTADO AS INT) AS estado,
                   CAST(ISNULL(ID_ARTICULO, 0) AS INT) AS id_art,
                   {nroSelect}
            FROM dbo.PQ_PRD_ORDENES_TRABAJO
            WHERE CODIGO_OT = @codigo
            ORDER BY ID_ORDEN_TRABAJO
            """;
        await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, loadSql))
        {
            cmd.Parameters.AddWithValue("@codigo", codigoOt);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                filas.Add((
                    Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader["id_op"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader["id_art"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader["nro"], CultureInfo.InvariantCulture)));
            }
        }

        if (filas.Count == 0)
        {
            return Fail("NOT_FOUND", "Orden de trabajo no encontrada");
        }

        if (filas.Any(f => f.Estado == 2))
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "Solo se pueden editar OT en estado Borrador o Abierta");
        }

        var modoIndividual = OrdenesTrabajoRunnerHelpers.ExtractBool(parameters, "modo_individual");
        List<int> idOperaciones;
        try
        {
            idOperaciones = OrdenesTrabajoRunnerHelpers.ResolveOperaciones(parameters, modoIndividual);
        }
        catch (OrdenesTrabajoRunnerHelpers.ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", ex.Message);
        }

        if (idOperaciones.Count == 0)
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException("Indique al menos una operación a incluir.");
        }

        foreach (var idOp in idOperaciones)
        {
            if (!await OrdenesTrabajoRunnerHelpers.OperacionActivaExistsAsync(
                    connection, transaction, timeoutSeconds, idOp, cancellationToken)
                .ConfigureAwait(false))
            {
                throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                    $"La operación {idOp} no existe o no está activa.");
            }
        }

        if (!await OrdenesTrabajoRunnerHelpers.ArticuloExistsAsync(
                connection, transaction, timeoutSeconds, idArticulo.Value, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "El artículo seleccionado no existe en el catálogo.");
        }

        var idArticuloAnterior = filas[0].IdArticulo;
        if (idArticulo.Value != idArticuloAnterior)
        {
            foreach (var fila in filas)
            {
                if (await OrdenesTrabajoRunnerHelpers.OrdenTieneAsignacionesAsync(
                        connection, transaction, timeoutSeconds, fila.Id, cancellationToken)
                    .ConfigureAwait(false)
                    || await OrdenesTrabajoRunnerHelpers.OrdenTienePartesEntradasAsync(
                        connection, transaction, timeoutSeconds, fila.Id, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                        "No se puede cambiar el artículo: la OT tiene asignaciones o partes vinculados.");
                }
            }
        }

        var fechaInicio = OrdenesTrabajoRunnerHelpers.NormalizeDate(
            OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "fecha_inicio_plan"));
        var fechaFin = OrdenesTrabajoRunnerHelpers.NormalizeDate(
            OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "fecha_fin_plan"));
        if (fechaInicio is not null && fechaFin is not null
            && string.CompareOrdinal(fechaFin, fechaInicio) < 0)
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "La fecha fin plan debe ser mayor o igual a la fecha inicio plan.");
        }

        var fechaRef = fechaInicio ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var idOp in idOperaciones)
        {
            if (!await OrdenesTrabajoRunnerHelpers.ParStdVigenteAsync(
                    connection, transaction, timeoutSeconds, idArticulo.Value, idOp, fechaRef, cancellationToken)
                .ConfigureAwait(false))
            {
                throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                    "La operación no está habilitada para el artículo seleccionado (estándar vigente).");
            }
        }

        var incremento = await OrdenesTrabajoRunnerHelpers.LoadOtIncrementoAsync(
                connection, transaction, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var operaciones = await OrdenesTrabajoRunnerHelpers.ResolverOperacionesConNroOrdenAsync(
                connection, transaction, timeoutSeconds, idArticulo.Value, idOperaciones, fechaRef, incremento, cancellationToken)
            .ConfigureAwait(false);
        if (operaciones.Count == 0)
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                "No se pudo resolver el orden de las operaciones seleccionadas.");
        }

        var estadoActual = filas.Max(f => f.Estado);
        var estadoParam = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "estado");
        var estadoNuevo = estadoParam ?? estadoActual;
        var msgEstado = await OrdenesTrabajoRunnerHelpers.ValidateEstadoTransitionAsync(
                connection, transaction, timeoutSeconds, filas.OrderBy(f => f.Id).First().Id, estadoActual, estadoNuevo, cancellationToken)
            .ConfigureAwait(false);
        if (msgEstado is not null)
        {
            throw new OrdenesTrabajoRunnerHelpers.ValidationException(msgEstado);
        }

        if (estadoNuevo == 0)
        {
            foreach (var fila in filas)
            {
                if (await OrdenesTrabajoRunnerHelpers.OrdenTieneAsignacionesAsync(
                        connection, transaction, timeoutSeconds, fila.Id, cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                        "No se puede pasar a Borrador: la OT está referenciada en asignaciones.");
                }
            }
        }

        var descripcion = OrdenesTrabajoRunnerHelpers.Truncate(
            OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "descripcion")?.Trim(), 200);
        var observaciones = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "observaciones")?.Trim();
        var usuarioId = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

        var targetOps = operaciones.Select(o => o.IdOperacion).ToHashSet();
        var byOp = filas.ToDictionary(f => f.IdOperacion, f => f);
        var toDelete = filas.Where(f => !targetOps.Contains(f.IdOperacion)).ToList();
        var toInsert = operaciones.Where(o => !byOp.ContainsKey(o.IdOperacion)).ToList();
        var toKeep = filas.Where(f => targetOps.Contains(f.IdOperacion)).ToList();

        foreach (var fila in toDelete)
        {
            var msg = await OrdenesTrabajoRunnerHelpers.OrdenNoEliminablePorVinculosAsync(
                    connection, transaction, timeoutSeconds, fila.Id, cancellationToken)
                .ConfigureAwait(false);
            if (msg is not null)
            {
                throw new OrdenesTrabajoRunnerHelpers.ValidationException(msg);
            }
        }

        // Update comunes en todas las filas actuales (antes de delete/insert).
        const string updateAllSql = """
            UPDATE dbo.PQ_PRD_ORDENES_TRABAJO
            SET DESCRIPCION = @descripcion,
                ID_ARTICULO = @idArticulo,
                CANTIDAD_A_PRODUCIR = @cantidad,
                FECHA_INICIO_PLAN = @fechaInicio,
                FECHA_FIN_PLAN = @fechaFin,
                OBSERVACIONES = @observaciones,
                ESTADO = @estado,
                FECHA_MODIF = GETDATE(),
                USUARIO_MODIF = @usuario
            WHERE CODIGO_OT = @codigo
            """;
        await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, updateAllSql))
        {
            cmd.Parameters.AddWithValue("@codigo", codigoOt);
            cmd.Parameters.AddWithValue("@descripcion", (object?)descripcion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@idArticulo", idArticulo.Value);
            cmd.Parameters.AddWithValue("@cantidad", cantidad.Value);
            cmd.Parameters.AddWithValue("@fechaInicio", (object?)fechaInicio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fechaFin", (object?)fechaFin ?? DBNull.Value);
            cmd.Parameters.AddWithValue(
                "@observaciones",
                string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones);
            cmd.Parameters.AddWithValue("@estado", estadoNuevo);
            cmd.Parameters.AddWithValue("@usuario", usuarioId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var fila in toDelete)
        {
            const string delSql = "DELETE FROM dbo.PQ_PRD_ORDENES_TRABAJO WHERE ID_ORDEN_TRABAJO = @id";
            await using var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, delSql);
            cmd.Parameters.AddWithValue("@id", fila.Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var resultIds = new List<int>();
        foreach (var op in toInsert)
        {
            var cols = "CODIGO_OT, DESCRIPCION, ID_ARTICULO, ID_OPERACION, ";
            var vals = "@codigo, @descripcion, @idArticulo, @idOperacion, ";
            if (hasNroOrden)
            {
                cols += "NRO_ORDEN, ";
                vals += "@nro, ";
            }

            cols += "CANTIDAD_A_PRODUCIR, FECHA_INICIO_PLAN, FECHA_FIN_PLAN, ESTADO, OBSERVACIONES, FECHA_ALTA, USUARIO_ALTA, FECHA_MODIF, USUARIO_MODIF";
            vals += "@cantidad, @fechaInicio, @fechaFin, @estado, @observaciones, GETDATE(), @usuario, GETDATE(), @usuario";

            var insertSql = $"""
                INSERT INTO dbo.PQ_PRD_ORDENES_TRABAJO ({cols})
                OUTPUT INSERTED.ID_ORDEN_TRABAJO
                VALUES ({vals});
                """;
            await using var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, insertSql);
            cmd.Parameters.AddWithValue("@codigo", codigoOt);
            cmd.Parameters.AddWithValue("@descripcion", (object?)descripcion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@idArticulo", idArticulo.Value);
            cmd.Parameters.AddWithValue("@idOperacion", op.IdOperacion);
            if (hasNroOrden)
            {
                cmd.Parameters.AddWithValue("@nro", op.NroOrden);
            }

            cmd.Parameters.AddWithValue("@cantidad", cantidad.Value);
            cmd.Parameters.AddWithValue("@fechaInicio", (object?)fechaInicio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fechaFin", (object?)fechaFin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@estado", estadoNuevo);
            cmd.Parameters.AddWithValue(
                "@observaciones",
                string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones);
            cmd.Parameters.AddWithValue("@usuario", usuarioId);
            var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            resultIds.Add(newId);
        }

        var nroPorOp = operaciones.ToDictionary(o => o.IdOperacion, o => o.NroOrden);
        foreach (var fila in toKeep)
        {
            if (hasNroOrden && nroPorOp.TryGetValue(fila.IdOperacion, out var nro) && nro != fila.NroOrden)
            {
                const string nroSql = """
                    UPDATE dbo.PQ_PRD_ORDENES_TRABAJO
                    SET NRO_ORDEN = @nro, FECHA_MODIF = GETDATE(), USUARIO_MODIF = @usuario
                    WHERE ID_ORDEN_TRABAJO = @id
                    """;
                await using var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, nroSql);
                cmd.Parameters.AddWithValue("@id", fila.Id);
                cmd.Parameters.AddWithValue("@nro", nro);
                cmd.Parameters.AddWithValue("@usuario", usuarioId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            resultIds.Add(fila.Id);
        }

        resultIds = resultIds.Distinct().OrderBy(x => x).ToList();
        var items = await OrdenesTrabajoRunnerHelpers.LoadItemsByIdsAsync(
                connection, transaction, timeoutSeconds, resultIds, hasNroOrden, cancellationToken)
            .ConfigureAwait(false);
        if (items.Count == 0)
        {
            return Fail("SQL_ERROR", "No se pudo recuperar la orden de trabajo actualizada.");
        }

        // Ordenar por nro_orden si existe.
        items = items
            .OrderBy(i => i.TryGetValue("nro_orden", out var n) && n is int ni ? ni : 0)
            .ThenBy(i => i.TryGetValue("id", out var id) && id is int idi ? idi : 0)
            .ToList();

        var payload = new Dictionary<string, object?>(items[0], StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = items
        };
        return Ok(payload);
    }

    private static PartesOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static PartesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
