using System.Data;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>D6.9.16 — Delete operario de ítem orquestado.</summary>
public sealed class AsignacionesItemsOperariosDeleteRunner
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
        var idOperario = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_operario");

        if (string.IsNullOrWhiteSpace(database)
            || idAsignacion is null or <= 0
            || idItem is null or <= 0
            || idOperario is null or <= 0)
        {
            return Fail("INVALID_PARAMETERS", "id_asignacion, id_item, id_operario y _database son obligatorios.");
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
                if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new AsignacionesRunnerHelpers.ValidationException("Tabla no disponible");
                }

                await AsignacionesItemsOperariosRunnerHelpers.AssertDraftAndItemAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        idAsignacion.Value,
                        idItem.Value,
                        "Solo asignaciones en estado Borrador permiten modificar operarios",
                        cancellationToken)
                    .ConfigureAwait(false);

                const string deleteSql = @"
DELETE FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
WHERE ID_ASIGNACION_ITEM = @idItem AND ID_OPERARIO = @idOp;";
                int deleted;
                await using (var cmd = new SqlCommand(deleteSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                    cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = idOperario.Value;
                    deleted = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (deleted == 0)
                {
                    throw new AsignacionesRunnerHelpers.NotFoundException("Operario no asignado a esta tarea");
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new Dictionary<string, object?>
                {
                    ["eliminado"] = true,
                    ["id_operario"] = idOperario.Value
                });
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
        new() { Status = JobStatuses.Success, Data = data };

    private static PartesOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
