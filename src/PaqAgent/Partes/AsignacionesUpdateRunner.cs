using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.9.4 — Update cabecera Asignaciones Draft orquestado (espejo PHP AsignacionesService::updateLocal).
/// Sin SP monolítico.
/// </summary>
public sealed class AsignacionesUpdateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idAsignacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_asignacion");
        var fechaAsignacion = AsignacionesRunnerHelpers.NormalizeDate(
            AsignacionesRunnerHelpers.ExtractString(parameters, "fecha_asignacion"));
        var idTipoTarea = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_tipo_tarea");

        if (string.IsNullOrWhiteSpace(database)
            || idAsignacion is null or <= 0
            || fechaAsignacion is null
            || idTipoTarea is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "id_asignacion, fecha_asignacion, id_tipo_tarea y _database son obligatorios.");
        }

        if (!AsignacionesRunnerHelpers.TryParseDateOnly(fechaAsignacion, out var fechaOnly))
        {
            return Fail("VALIDATION", "La fecha de asignación debe tener formato AAAA-MM-DD.");
        }

        if (fechaOnly < DateOnly.FromDateTime(DateTime.Today))
        {
            return Fail("VALIDATION", "La fecha de asignación no puede ser anterior al día actual.");
        }

        var idTurno = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_turno");
        if (idTurno is <= 0)
        {
            idTurno = null;
        }

        var observaciones = AsignacionesRunnerHelpers.ExtractString(parameters, "observaciones")?.Trim();
        if (observaciones is { Length: > 2000 })
        {
            return Fail("VALIDATION", "observaciones no puede superar 2000 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(observaciones))
        {
            observaciones = null;
        }

        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

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
                if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException(
                        "No existe la tabla PQ_PRD_ASIGNACIONES en la base de datos de la empresa.");
                }

                if (!await AsignacionesRunnerHelpers.ColumnExistsAsync(
                        connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES", "ID_TIPO_TAREA", cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException(
                        "Falta la columna ID_TIPO_TAREA en PQ_PRD_ASIGNACIONES.");
                }

                var (estado, tipoActual, _) = await AsignacionesRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (estado != 0)
                {
                    throw new AsignacionesRunnerHelpers.ConflictException(
                        "Solo asignaciones en estado Borrador pueden editarse");
                }

                if (tipoActual.HasValue
                    && tipoActual.Value != idTipoTarea.Value
                    && await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                        .ConfigureAwait(false)
                    && await AsignacionesRunnerHelpers.HasItemsAsync(
                            connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException(
                        "No se puede cambiar el tipo de tarea de la cabecera mientras existan tareas planificadas.");
                }

                if (await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_TIPOS_TAREA", cancellationToken)
                        .ConfigureAwait(false)
                    && !await AsignacionesRunnerHelpers.TipoTareaExistsAsync(
                        connection, transaction, timeoutSeconds, idTipoTarea.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException(
                        $"El tipo de tarea {idTipoTarea.Value} no existe.");
                }

                if (idTurno is not null
                    && await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_TURNOS", cancellationToken)
                        .ConfigureAwait(false)
                    && !await AsignacionesRunnerHelpers.TurnoExistsAsync(
                        connection, transaction, timeoutSeconds, idTurno.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException($"El turno {idTurno.Value} no existe.");
                }

                const string updateSql = @"
UPDATE dbo.PQ_PRD_ASIGNACIONES
SET FECHA_ASIGNACION = CONVERT(date, @fecha, 23),
    ID_TURNO = @idTurno,
    ID_TIPO_TAREA = @idTipoTarea,
    OBSERVACIONES = @obs,
    FECHA_MODIF = CONVERT(datetime, @fechaModif, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_ASIGNACION = @id;";

                await using (var cmd = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 10).Value = fechaAsignacion;
                    cmd.Parameters.Add("@idTurno", SqlDbType.Int).Value = idTurno.HasValue ? idTurno.Value : DBNull.Value;
                    cmd.Parameters.Add("@idTipoTarea", SqlDbType.Int).Value = idTipoTarea.Value;
                    cmd.Parameters.Add("@obs", SqlDbType.NVarChar, 2000).Value =
                        observaciones is null ? DBNull.Value : observaciones;
                    cmd.Parameters.Add("@fechaModif", SqlDbType.NVarChar, 19).Value =
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var payload = await AsignacionesRunnerHelpers.LoadCabeceraAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        idAsignacion.Value,
                        includeObservaciones: true,
                        cancellationToken)
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
