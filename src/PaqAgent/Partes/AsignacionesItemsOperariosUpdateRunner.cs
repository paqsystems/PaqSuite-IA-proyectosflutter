using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>D6.9.15 — Update/sync operarios de ítem orquestado.</summary>
public sealed class AsignacionesItemsOperariosUpdateRunner
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
        var ids = AsignacionesItemsOperariosRunnerHelpers.ExtractIntList(parameters, "id_operarios");

        if (string.IsNullOrWhiteSpace(database) || idAsignacion is null or <= 0 || idItem is null or <= 0)
        {
            return Fail("INVALID_PARAMETERS", "id_asignacion, id_item y _database son obligatorios.");
        }

        if (!parameters.ContainsKey("id_operarios"))
        {
            return Fail("INVALID_PARAMETERS", "id_operarios es obligatorio.");
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

                if (ids.Count == 0)
                {
                    const string deleteAll = @"
DELETE FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS WHERE ID_ASIGNACION_ITEM = @idItem;";
                    await using var cmd = new SqlCommand(deleteAll, connection, transaction) { CommandTimeout = timeoutSeconds };
                    cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var inParams = string.Join(",", ids.Select((_, i) => "@p" + i));
                    var deleteSql = $@"
DELETE FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
WHERE ID_ASIGNACION_ITEM = @idItem AND ID_OPERARIO NOT IN ({inParams});";
                    await using (var cmd = new SqlCommand(deleteSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                    {
                        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                        for (var i = 0; i < ids.Count; i++)
                        {
                            cmd.Parameters.Add("@p" + i, SqlDbType.Int).Value = ids[i];
                        }

                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    var existentes = new HashSet<int>();
                    const string existSql = @"
SELECT CAST(ID_OPERARIO AS INT) AS id_operario
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS WHERE ID_ASIGNACION_ITEM = @idItem;";
                    await using (var cmd = new SqlCommand(existSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                    {
                        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            existentes.Add(Convert.ToInt32(reader["id_operario"], CultureInfo.InvariantCulture));
                        }
                    }

                    var fechaAlta = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    foreach (var opId in ids)
                    {
                        if (existentes.Contains(opId))
                        {
                            continue;
                        }

                        const string insertSql = @"
INSERT INTO dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
    (ID_ASIGNACION_ITEM, ID_OPERARIO, ROL_PLAN, FECHA_ALTA, USUARIO_ALTA)
VALUES (@idItem, @idOp, NULL, CONVERT(datetime, @fecha, 120), @usuario);";
                        await using var cmd = new SqlCommand(insertSql, connection, transaction) { CommandTimeout = timeoutSeconds };
                        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                        cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = opId;
                        cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 19).Value = fechaAlta;
                        cmd.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                var items = await AsignacionesItemsOperariosRunnerHelpers.LoadOperariosForItemAsync(
                        connection, transaction, timeoutSeconds, idItem.Value, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new Dictionary<string, object?> { ["items"] = items });
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
