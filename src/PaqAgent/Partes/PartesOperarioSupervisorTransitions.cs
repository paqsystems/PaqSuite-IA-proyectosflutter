using System.Data;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

internal enum PartesOperarioSupervisorAccion
{
    Aprobar,
    Devolver,
    Cerrar
}

/// <summary>
/// D6.10.5–7 — transiciones supervisor (sin filtro de propietario). Espejo PHP aprobarLocal/devolverLocal/cerrarLocal.
/// </summary>
internal static class PartesOperarioSupervisorTransitions
{
    internal static async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        PartesOperarioSupervisorAccion accion)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idParteOperario = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_parte_operario");
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id");

        if (string.IsNullOrWhiteSpace(database)
            || idParteOperario is null or <= 0
            || usuarioId is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "id_parte_operario, usuario_id y _database son obligatorios.");
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
                if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_OPERARIO", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesOperarioRunnerHelpers.NotFoundMessage);
                }

                if (!await PartesOperarioRunnerHelpers.CircuitoAutorizacionActivoAsync(
                            connection, transaction, timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(
                        PartesOperarioRunnerHelpers.ConflictCircuitoSupervisorMessage);
                }

                var (estado, _) = await PartesOperarioRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
                    .ConfigureAwait(false);

                var (expectedEstado, newEstado, conflictMessage) = accion switch
                {
                    PartesOperarioSupervisorAccion.Aprobar => (
                        PartesOperarioRunnerHelpers.EstadoSubmitted,
                        PartesOperarioRunnerHelpers.EstadoReviewed,
                        PartesOperarioRunnerHelpers.ConflictAprobarMessage),
                    PartesOperarioSupervisorAccion.Devolver => (
                        PartesOperarioRunnerHelpers.EstadoSubmitted,
                        PartesOperarioRunnerHelpers.EstadoOpen,
                        PartesOperarioRunnerHelpers.ConflictDevolverMessage),
                    PartesOperarioSupervisorAccion.Cerrar => (
                        PartesOperarioRunnerHelpers.EstadoReviewed,
                        PartesOperarioRunnerHelpers.EstadoLocked,
                        PartesOperarioRunnerHelpers.ConflictCerrarMessage),
                    _ => throw new InvalidOperationException("Acción supervisor no soportada.")
                };

                if (estado != expectedEstado)
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(conflictMessage);
                }

                await using (var cmd = new SqlCommand(
                                 BuildUpdateSql(accion), connection, transaction)
                             {
                                 CommandTimeout = timeoutSeconds
                             })
                {
                    cmd.Parameters.Add("@estado", SqlDbType.Int).Value = newEstado;
                    cmd.Parameters.Add("@fechaModif", SqlDbType.NVarChar, 19).Value =
                        PartesOperarioRunnerHelpers.NowSql();
                    cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var payload = await PartesOperarioRunnerHelpers.LoadIdEstadoPayloadAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
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
        catch (PartesOperarioRunnerHelpers.NotFoundException nfex)
        {
            return Fail("NOT_FOUND", nfex.Message);
        }
        catch (PartesOperarioRunnerHelpers.ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
        }
        catch (PartesOperarioRunnerHelpers.ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string BuildUpdateSql(PartesOperarioSupervisorAccion accion) =>
        accion switch
        {
            PartesOperarioSupervisorAccion.Aprobar => @"
UPDATE dbo.PQ_PRD_PARTES_OPERARIO
SET ESTADO = @estado,
    FECHA_REVISION = CONVERT(datetime, @fechaModif, 120),
    ID_USUARIO_REVISION = @usuarioId,
    FECHA_MODIF = CONVERT(datetime, @fechaModif, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_PARTE_OPERARIO = @id;",
            PartesOperarioSupervisorAccion.Devolver => @"
UPDATE dbo.PQ_PRD_PARTES_OPERARIO
SET ESTADO = @estado,
    FECHA_REVISION = NULL,
    ID_USUARIO_REVISION = NULL,
    FECHA_MODIF = CONVERT(datetime, @fechaModif, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_PARTE_OPERARIO = @id;",
            PartesOperarioSupervisorAccion.Cerrar => @"
UPDATE dbo.PQ_PRD_PARTES_OPERARIO
SET ESTADO = @estado,
    FECHA_MODIF = CONVERT(datetime, @fechaModif, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_PARTE_OPERARIO = @id;",
            _ => throw new InvalidOperationException("Acción supervisor no soportada.")
        };

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
