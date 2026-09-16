using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.9.7 — Cancelar Asignaciones (Draft/Published→Cancelled) orquestado (espejo PHP AsignacionesService::cancelarLocal).
/// Sin SP monolítico.
/// </summary>
public sealed class AsignacionesCancelarRunner
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

                var (estado, _, _) = await AsignacionesRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (estado is 2 or 3)
                {
                    throw new AsignacionesRunnerHelpers.ConflictException(
                        "La asignación ya está cerrada o anulada");
                }

                var fechaModif = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                const string updateSql = @"
UPDATE dbo.PQ_PRD_ASIGNACIONES
SET ESTADO = 3,
    FECHA_MODIF = CONVERT(datetime, @fecha, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_ASIGNACION = @id;";

                await using (var cmd = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 19).Value = fechaModif;
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
