using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.9.9 — Create ítem de asignación orquestado (espejo PHP AsignacionesItemsService::storeLocal).
/// </summary>
public sealed class AsignacionesItemsCreateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idAsignacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_asignacion");
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

        if (string.IsNullOrWhiteSpace(database) || idAsignacion is null or <= 0)
        {
            return Fail("INVALID_PARAMETERS", "id_asignacion y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return Degraded();
        }

        try
        {
            var connectionString = SqlConnectionStringFactory.Build(
                agentOptions.Sql, connectTimeoutSeconds: 15, databaseOverride: database);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES", cancellationToken)
                        .ConfigureAwait(false)
                    || !await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException("Tablas no disponibles");
                }

                var (estado, idTipoTareaCab, _) = await AsignacionesRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (estado != 0)
                {
                    throw new AsignacionesRunnerHelpers.ConflictException(
                        "Solo asignaciones en estado Borrador permiten modificar tareas planificadas");
                }

                var idTipoTarea = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_tipo_tarea")
                    ?? idTipoTareaCab
                    ?? 0;
                var idOrdenTrabajo = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo");
                var idOperacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_operacion");
                var idArticulo = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_articulo");
                var idMaquina = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_maquina");
                var notasPlan = AsignacionesRunnerHelpers.ExtractString(parameters, "notas_plan");
                var prioridad = AsignacionesRunnerHelpers.ExtractInt(parameters, "prioridad");
                var nroOrdenOperacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "nro_orden_operacion");
                double? unidadesHoraStd = ExtractDouble(parameters, "unidades_hora_std");
                double? unidadesPlan = ExtractDouble(parameters, "unidades_plan");
                var minutosPlan = AsignacionesRunnerHelpers.ExtractInt(parameters, "minutos_plan");

                if (idOrdenTrabajo is > 0)
                {
                    var resolvedOt = await AssertOtAbiertaYResolverAsync(
                            connection, transaction, timeoutSeconds, idOrdenTrabajo.Value,
                            idOperacion, idArticulo, unidadesPlan, nroOrdenOperacion, cancellationToken)
                        .ConfigureAwait(false);
                    idOperacion = resolvedOt.IdOperacion;
                    idArticulo = resolvedOt.IdArticulo;
                    unidadesPlan = resolvedOt.UnidadesPlan;
                    nroOrdenOperacion = resolvedOt.NroOrdenOperacion;
                }
                else if (idOperacion is > 0)
                {
                    throw new AsignacionesRunnerHelpers.ValidationException(
                        "FIELD:id_operacion|La operación se define en la orden de trabajo: seleccione una OT o no envíe id_operacion.");
                }

                var fechaAlta = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var hasNro = await AsignacionesRunnerHelpers.ColumnExistsAsync(
                        connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", "NRO_ORDEN_OPERACION", cancellationToken)
                    .ConfigureAwait(false);
                var hasHora = await AsignacionesRunnerHelpers.ColumnExistsAsync(
                        connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", "HORA_INICIO_PLAN", cancellationToken)
                    .ConfigureAwait(false);

                var horaIni = AsignacionesRunnerHelpers.ExtractString(parameters, "hora_inicio_plan");
                var horaFin = AsignacionesRunnerHelpers.ExtractString(parameters, "hora_fin_plan");

                var cols = "[ID_ASIGNACION], [ID_ORDEN_TRABAJO], [ID_ARTICULO], [ID_OPERACION]";
                var vals = "@idAsig, @idOt, @idArt, @idOp";
                if (hasNro)
                {
                    cols += ", [NRO_ORDEN_OPERACION]";
                    vals += ", @nroOp";
                }

                cols += ", [ID_TIPO_TAREA], [ID_MAQUINA], [UNIDADES_HORA_STD], [UNIDADES_PLAN], [MINUTOS_PLAN]";
                vals += ", @idTipo, @idMaq, @uh, @up, @mp";
                if (hasHora)
                {
                    cols += ", [HORA_INICIO_PLAN], [HORA_FIN_PLAN]";
                    vals += ", @hi, @hf";
                }

                cols += ", [NOTAS_PLAN], [PRIORIDAD], [ACTIVO], [FECHA_ALTA], [USUARIO_ALTA]";
                vals += ", @notas, @prio, 1, CONVERT(datetime, @fecha, 120), @usuario";

                var insertSql = $@"
INSERT INTO dbo.PQ_PRD_ASIGNACIONES_ITEMS ({cols})
OUTPUT INSERTED.ID_ASIGNACION_ITEM AS id
VALUES ({vals});";

                int newId;
                await using (var cmd = new SqlCommand(insertSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@idAsig", SqlDbType.Int).Value = idAsignacion.Value;
                    cmd.Parameters.Add("@idOt", SqlDbType.Int).Value = (object?)idOrdenTrabajo ?? DBNull.Value;
                    cmd.Parameters.Add("@idArt", SqlDbType.Int).Value = (object?)idArticulo ?? DBNull.Value;
                    cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = (object?)idOperacion ?? DBNull.Value;
                    if (hasNro)
                    {
                        cmd.Parameters.Add("@nroOp", SqlDbType.Int).Value = (object?)nroOrdenOperacion ?? DBNull.Value;
                    }

                    cmd.Parameters.Add("@idTipo", SqlDbType.Int).Value = idTipoTarea;
                    cmd.Parameters.Add("@idMaq", SqlDbType.Int).Value = (object?)idMaquina ?? DBNull.Value;
                    cmd.Parameters.Add("@uh", SqlDbType.Float).Value = (object?)unidadesHoraStd ?? DBNull.Value;
                    cmd.Parameters.Add("@up", SqlDbType.Float).Value = (object?)unidadesPlan ?? DBNull.Value;
                    cmd.Parameters.Add("@mp", SqlDbType.Int).Value = (object?)minutosPlan ?? DBNull.Value;
                    if (hasHora)
                    {
                        cmd.Parameters.Add("@hi", SqlDbType.NVarChar, 5).Value = (object?)horaIni ?? DBNull.Value;
                        cmd.Parameters.Add("@hf", SqlDbType.NVarChar, 5).Value = (object?)horaFin ?? DBNull.Value;
                    }

                    cmd.Parameters.Add("@notas", SqlDbType.NVarChar, 2000).Value = (object?)notasPlan ?? DBNull.Value;
                    cmd.Parameters.Add("@prio", SqlDbType.Int).Value = (object?)prioridad ?? DBNull.Value;
                    cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 19).Value = fechaAlta;
                    cmd.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    newId = scalar is null or DBNull ? 0 : Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
                }

                var payload = await AsignacionesItemsOperariosRunnerHelpers.LoadItemAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, newId, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(payload);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (AsignacionesRunnerHelpers.NotFoundException nex)
        {
            return Fail("NOT_FOUND", nex.Message);
        }
        catch (AsignacionesRunnerHelpers.ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
        }
        catch (AsignacionesRunnerHelpers.ValidationException vex)
        {
            return Fail(vex.Message.StartsWith("FIELD:", StringComparison.Ordinal) ? "VALIDATION" : "VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private readonly record struct OtResolveResult(
        int? IdOperacion,
        int? IdArticulo,
        double? UnidadesPlan,
        int? NroOrdenOperacion);

    private static async Task<OtResolveResult> AssertOtAbiertaYResolverAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrdenTrabajo,
        int? idOperacion,
        int? idArticulo,
        double? unidadesPlan,
        int? nroOrdenOperacion,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", cancellationToken)
                .ConfigureAwait(false))
        {
            return new OtResolveResult(idOperacion, idArticulo, unidadesPlan, nroOrdenOperacion);
        }

        const string sql = @"
SELECT TOP 1
    CAST(ESTADO AS INT) AS estado,
    CAST(ID_OPERACION AS INT) AS id_operacion,
    CAST(ID_ARTICULO AS INT) AS id_articulo,
    CAST(CANTIDAD_A_PRODUCIR AS FLOAT) AS cantidad,
    CAST(NRO_ORDEN AS INT) AS nro_orden
FROM dbo.PQ_PRD_ORDENES_TRABAJO
WHERE ID_ORDEN_TRABAJO = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idOrdenTrabajo;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "FIELD:id_orden_trabajo|La orden de trabajo no existe.");
        }

        var estado = reader["estado"] is DBNull ? -1 : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
        if (estado != 0)
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "FIELD:id_orden_trabajo|Solo se pueden asociar órdenes en estado Abierta.");
        }

        var otOp = reader["id_operacion"] is DBNull
            ? (int?)null
            : Convert.ToInt32(reader["id_operacion"], CultureInfo.InvariantCulture);
        if (otOp is null or <= 0)
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "FIELD:id_orden_trabajo|La orden de trabajo no tiene operación definida. Asigne operación en la OT antes de planificar la tarea.");
        }

        if (idOperacion is > 0 && idOperacion.Value != otOp.Value)
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "FIELD:id_operacion|La operación no coincide con la definida en la orden de trabajo.");
        }

        idOperacion = otOp;
        if (idArticulo is null or <= 0 && reader["id_articulo"] is not DBNull)
        {
            idArticulo = Convert.ToInt32(reader["id_articulo"], CultureInfo.InvariantCulture);
        }

        if (unidadesPlan is null && reader["cantidad"] is not DBNull)
        {
            unidadesPlan = Convert.ToDouble(reader["cantidad"], CultureInfo.InvariantCulture);
        }

        var nroOt = reader["nro_orden"] is DBNull
            ? 0
            : Convert.ToInt32(reader["nro_orden"], CultureInfo.InvariantCulture);
        if (nroOt > 0)
        {
            if (nroOrdenOperacion is > 0 && nroOrdenOperacion.Value != nroOt)
            {
                throw new AsignacionesRunnerHelpers.ValidationException(
                    "FIELD:nro_orden_operacion|El número de orden de operación no coincide con la fila de OT seleccionada.");
            }

            nroOrdenOperacion = nroOt;
        }

        return new OtResolveResult(idOperacion, idArticulo, unidadesPlan, nroOrdenOperacion);
    }

    private static double? ExtractDouble(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => null
        };
    }

    private static PartesOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static PartesOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };

    private static PartesOutcome Degraded() =>
        new()
        {
            Status = JobStatuses.Degraded,
            ErrorCode = "SQL_NOT_CONFIGURED",
            ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
        };
}
