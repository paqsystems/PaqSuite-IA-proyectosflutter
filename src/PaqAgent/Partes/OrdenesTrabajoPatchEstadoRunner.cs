using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.8.6 — patch estado OT orquestado (espejo PHP OrdenTrabajoController::patchEstado / OrdenesTrabajoService::patchEstadoLocal).
/// Sin SP monolítico.
/// </summary>
public sealed class OrdenesTrabajoPatchEstadoRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "_database");
        var idOrden = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo");
        var estado = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "estado");
        var usuarioId = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

        if (string.IsNullOrWhiteSpace(database) || idOrden is null or <= 0 || estado is null)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "id_orden_trabajo, estado y _database son obligatorios.");
        }

        if (estado is not (0 or 1 or 2))
        {
            return Fail("VALIDATION", "El estado debe ser 0 (Borrador), 1 (Abierta) o 2 (Cerrada).");
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
                int actual;
                await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, loadSql))
                {
                    cmd.Parameters.AddWithValue("@id", idOrden.Value);
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return Fail("NOT_FOUND", "Orden de trabajo no encontrada");
                    }

                    actual = Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
                }

                var msg = await OrdenesTrabajoRunnerHelpers.ValidateEstadoTransitionAsync(
                        connection, transaction, timeoutSeconds, idOrden.Value, actual, estado.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (msg is not null)
                {
                    throw new OrdenesTrabajoRunnerHelpers.ValidationException(msg);
                }

                const string updateSql = """
                    UPDATE dbo.PQ_PRD_ORDENES_TRABAJO
                    SET ESTADO = @estado,
                        FECHA_MODIF = GETDATE(),
                        USUARIO_MODIF = @usuario
                    WHERE ID_ORDEN_TRABAJO = @id
                    """;
                await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, updateSql))
                {
                    cmd.Parameters.AddWithValue("@estado", estado.Value);
                    cmd.Parameters.AddWithValue("@usuario", Math.Max(0, usuarioId));
                    cmd.Parameters.AddWithValue("@id", idOrden.Value);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var hasNroOrden = await OrdenesTrabajoRunnerHelpers.ColumnExistsAsync(
                        connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", "NRO_ORDEN", cancellationToken)
                    .ConfigureAwait(false);
                var items = await OrdenesTrabajoRunnerHelpers.LoadItemsByIdsAsync(
                        connection, transaction, timeoutSeconds, [idOrden.Value], hasNroOrden, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                if (items.Count == 0)
                {
                    return Fail("NOT_FOUND", "Orden de trabajo no encontrada");
                }

                return Ok(items[0]);
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
