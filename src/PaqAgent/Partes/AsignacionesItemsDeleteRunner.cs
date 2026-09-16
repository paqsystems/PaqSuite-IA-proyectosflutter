using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>D6.9.11 — Delete ítem de asignación orquestado.</summary>
public sealed class AsignacionesItemsDeleteRunner
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

                const string deleteSql = @"
DELETE FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
WHERE ID_ASIGNACION = @idAsig AND ID_ASIGNACION_ITEM = @idItem;";
                await using (var cmd = new SqlCommand(deleteSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.AddWithValue("@idAsig", idAsignacion.Value);
                    cmd.Parameters.AddWithValue("@idItem", idItem.Value);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new Dictionary<string, object?>
                {
                    ["eliminado"] = true,
                    ["id"] = idItem.Value
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
