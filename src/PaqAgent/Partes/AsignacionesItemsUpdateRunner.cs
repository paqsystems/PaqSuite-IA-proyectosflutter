using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>D6.9.10 — Update ítem de asignación orquestado.</summary>
public sealed class AsignacionesItemsUpdateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idAsignacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_asignacion");
        var idItem = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_item");
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

        if (string.IsNullOrWhiteSpace(database) || idAsignacion is null or <= 0 || idItem is null or <= 0)
        {
            return Fail("INVALID_PARAMETERS", "id_asignacion, id_item y _database son obligatorios.");
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
                agentOptions.Sql, connectTimeoutSeconds: 15, databaseOverride: database);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await AsignacionesItemsOperariosRunnerHelpers.AssertDraftAndItemAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        idAsignacion.Value,
                        idItem.Value,
                        "Solo asignaciones en estado Borrador permiten modificar tareas planificadas",
                        cancellationToken)
                    .ConfigureAwait(false);

                var current = await AsignacionesItemsOperariosRunnerHelpers.LoadItemAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, idItem.Value, cancellationToken)
                    .ConfigureAwait(false);

                var idOrdenTrabajo = parameters.ContainsKey("id_orden_trabajo")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo")
                    : (int?)current["id_orden_trabajo"];
                var idOperacion = parameters.ContainsKey("id_operacion")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "id_operacion")
                    : (int?)current["id_operacion"];
                var idArticulo = parameters.ContainsKey("id_articulo")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "id_articulo")
                    : (int?)current["id_articulo"];
                var idMaquina = parameters.ContainsKey("id_maquina")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "id_maquina")
                    : (int?)current["id_maquina"];
                var notasPlan = parameters.ContainsKey("notas_plan")
                    ? AsignacionesRunnerHelpers.ExtractString(parameters, "notas_plan")
                    : current["notas_plan"] as string;
                var prioridad = parameters.ContainsKey("prioridad")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "prioridad")
                    : (int?)current["prioridad"];
                var idTipoTarea = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_tipo_tarea")
                    ?? (int?)current["id_tipo_tarea"]
                    ?? 0;

                double? unidadesHoraStd = parameters.ContainsKey("unidades_hora_std")
                    ? ExtractDouble(parameters, "unidades_hora_std")
                    : current["unidades_hora_std"] as double?;
                double? unidadesPlan = parameters.ContainsKey("unidades_plan")
                    ? ExtractDouble(parameters, "unidades_plan")
                    : current["unidades_plan"] as double?;
                var minutosPlan = parameters.ContainsKey("minutos_plan")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "minutos_plan")
                    : (int?)current["minutos_plan"];

                if (idOrdenTrabajo is > 0)
                {
                    await AssertOtAbiertaAsync(
                            connection, transaction, timeoutSeconds, idOrdenTrabajo.Value, idOperacion, cancellationToken)
                        .ConfigureAwait(false);
                }

                var fechaModif = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                const string updateSql = @"
UPDATE dbo.PQ_PRD_ASIGNACIONES_ITEMS
SET ID_ORDEN_TRABAJO = @idOt,
    ID_ARTICULO = @idArt,
    ID_OPERACION = @idOp,
    ID_TIPO_TAREA = @idTipo,
    ID_MAQUINA = @idMaq,
    UNIDADES_HORA_STD = @uh,
    UNIDADES_PLAN = @up,
    MINUTOS_PLAN = @mp,
    NOTAS_PLAN = @notas,
    PRIORIDAD = @prio,
    FECHA_MODIF = CONVERT(datetime, @fecha, 120),
    USUARIO_MODIF = @usuario
WHERE ID_ASIGNACION = @idAsig AND ID_ASIGNACION_ITEM = @idItem;";

                await using (var cmd = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@idOt", SqlDbType.Int).Value = (object?)idOrdenTrabajo ?? DBNull.Value;
                    cmd.Parameters.Add("@idArt", SqlDbType.Int).Value = (object?)idArticulo ?? DBNull.Value;
                    cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = (object?)idOperacion ?? DBNull.Value;
                    cmd.Parameters.Add("@idTipo", SqlDbType.Int).Value = idTipoTarea;
                    cmd.Parameters.Add("@idMaq", SqlDbType.Int).Value = (object?)idMaquina ?? DBNull.Value;
                    cmd.Parameters.Add("@uh", SqlDbType.Float).Value = (object?)unidadesHoraStd ?? DBNull.Value;
                    cmd.Parameters.Add("@up", SqlDbType.Float).Value = (object?)unidadesPlan ?? DBNull.Value;
                    cmd.Parameters.Add("@mp", SqlDbType.Int).Value = (object?)minutosPlan ?? DBNull.Value;
                    cmd.Parameters.Add("@notas", SqlDbType.NVarChar, 2000).Value = (object?)notasPlan ?? DBNull.Value;
                    cmd.Parameters.Add("@prio", SqlDbType.Int).Value = (object?)prioridad ?? DBNull.Value;
                    cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 19).Value = fechaModif;
                    cmd.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@idAsig", SqlDbType.Int).Value = idAsignacion.Value;
                    cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var payload = await AsignacionesItemsOperariosRunnerHelpers.LoadItemAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, idItem.Value, cancellationToken)
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
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task AssertOtAbiertaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrdenTrabajo,
        int? idOperacion,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1 CAST(ESTADO AS INT) AS estado, CAST(ID_OPERACION AS INT) AS id_operacion
FROM dbo.PQ_PRD_ORDENES_TRABAJO WHERE ID_ORDEN_TRABAJO = @id;";
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
        if (idOperacion is > 0 && otOp is > 0 && idOperacion.Value != otOp.Value)
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "FIELD:id_operacion|La operación no coincide con la definida en la orden de trabajo.");
        }
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
}
