using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>D6.9.14 — Create operario en ítem orquestado.</summary>
public sealed class AsignacionesItemsOperariosCreateRunner
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
        var rolPlan = AsignacionesRunnerHelpers.ExtractString(parameters, "rol_plan");
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

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

                const string existsSql = @"
SELECT TOP 1 1 FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
WHERE ID_ASIGNACION_ITEM = @idItem AND ID_OPERARIO = @idOp;";
                await using (var cmd = new SqlCommand(existsSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                    cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = idOperario.Value;
                    var exists = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    if (exists is not null and not DBNull)
                    {
                        throw new AsignacionesRunnerHelpers.ConflictException("El operario ya está asignado a esta tarea");
                    }
                }

                var fechaAlta = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                const string insertSql = @"
INSERT INTO dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
    (ID_ASIGNACION_ITEM, ID_OPERARIO, ROL_PLAN, FECHA_ALTA, USUARIO_ALTA)
OUTPUT INSERTED.ID_ASIGITEM_OPERARIO AS id
VALUES (@idItem, @idOp, @rol, CONVERT(datetime, @fecha, 120), @usuario);";

                int newId;
                await using (var cmd = new SqlCommand(insertSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem.Value;
                    cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = idOperario.Value;
                    cmd.Parameters.Add("@rol", SqlDbType.NVarChar, 50).Value =
                        string.IsNullOrWhiteSpace(rolPlan) ? DBNull.Value : rolPlan.Trim();
                    cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 19).Value = fechaAlta;
                    cmd.Parameters.Add("@usuario", SqlDbType.Int).Value = usuarioId;
                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    newId = scalar is null or DBNull ? 0 : Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
                }

                var list = await AsignacionesItemsOperariosRunnerHelpers.LoadOperariosForItemAsync(
                        connection, transaction, timeoutSeconds, idItem.Value, cancellationToken)
                    .ConfigureAwait(false);
                var created = list.FirstOrDefault(x => Convert.ToInt32(x["id"], CultureInfo.InvariantCulture) == newId)
                    ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = newId,
                        ["id_operario"] = idOperario.Value,
                        ["nro_legajo"] = null,
                        ["nombre_completo"] = idOperario.Value.ToString(CultureInfo.InvariantCulture),
                        ["rol_plan"] = string.IsNullOrWhiteSpace(rolPlan) ? null : rolPlan.Trim()
                    };

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(created);
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
