using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.8.5 — baja Órdenes de trabajo orquestada (espejo PHP OrdenTrabajoController::destroy / OrdenesTrabajoService::destroyLocal).
/// Baja por id de fila. Sin SP monolítico.
/// </summary>
public sealed class OrdenesTrabajoDeleteRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "_database");
        var idOrden = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo");
        var usuarioCodigo = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "usuario_codigo")?.Trim();

        if (string.IsNullOrWhiteSpace(database) || idOrden is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "id_orden_trabajo y _database son obligatorios.");
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

        if (!string.IsNullOrEmpty(usuarioCodigo)
            && string.Equals(usuarioCodigo, "EMP", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("FORBIDDEN", "No autorizado para eliminar órdenes de trabajo.");
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

                const string loadSql = """
                    SELECT CAST(ID_ORDEN_TRABAJO AS INT) AS id, CAST(ESTADO AS INT) AS estado
                    FROM dbo.PQ_PRD_ORDENES_TRABAJO
                    WHERE ID_ORDEN_TRABAJO = @id
                    """;
                int? estado = null;
                await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, loadSql))
                {
                    cmd.Parameters.AddWithValue("@id", idOrden.Value);
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return Fail("NOT_FOUND", "Orden de trabajo no encontrada");
                    }

                    estado = Convert.ToInt32(reader["estado"], System.Globalization.CultureInfo.InvariantCulture);
                }

                if (estado is not (0 or 1))
                {
                    throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                        "Solo se pueden eliminar OT en estado Borrador o Abierta");
                }

                var msgVinculo = await OrdenesTrabajoRunnerHelpers.OrdenNoEliminablePorVinculosAsync(
                        connection, transaction, timeoutSeconds, idOrden.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (msgVinculo is not null)
                {
                    if (await OrdenesTrabajoRunnerHelpers.OrdenTieneAsignacionesAsync(
                            connection, transaction, timeoutSeconds, idOrden.Value, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                            "No se puede eliminar la orden: existe al menos un ítem de asignación vinculado a esta OT.");
                    }

                    throw new OrdenesTrabajoRunnerHelpers.ValidationException(msgVinculo);
                }

                const string deleteSql = "DELETE FROM dbo.PQ_PRD_ORDENES_TRABAJO WHERE ID_ORDEN_TRABAJO = @id";
                await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, deleteSql))
                {
                    cmd.Parameters.AddWithValue("@id", idOrden.Value);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new Dictionary<string, object?>());
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
        catch (OrdenesTrabajoRunnerHelpers.ValidationException vex)
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
