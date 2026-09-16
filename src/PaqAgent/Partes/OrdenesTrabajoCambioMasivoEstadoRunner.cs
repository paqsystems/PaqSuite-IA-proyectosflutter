using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.8.6 — cambio masivo estado OT orquestado (espejo PHP OrdenTrabajoController::cambioMasivoEstado).
/// operacion: cerrar (Abierta→Cerrada) | reabrir (Cerrada→Abierta). Sin SP monolítico.
/// </summary>
public sealed class OrdenesTrabajoCambioMasivoEstadoRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "_database");
        var operacion = (OrdenesTrabajoRunnerHelpers.ExtractString(parameters, "operacion") ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var usuarioId = OrdenesTrabajoRunnerHelpers.ExtractInt(parameters, "usuario_id") ?? 0;

        List<int> ids;
        try
        {
            ids = OrdenesTrabajoRunnerHelpers.ParseIdsJson(parameters);
        }
        catch (OrdenesTrabajoRunnerHelpers.ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Fail("VALIDATION", "ids_json debe ser un array JSON de enteros.");
        }

        if (string.IsNullOrWhiteSpace(database) || ids.Count == 0 || operacion is not ("cerrar" or "reabrir"))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "ids_json (array int > 0), operacion (cerrar|reabrir) y _database son obligatorios.");
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

                var actualizadas = 0;
                foreach (var id in ids)
                {
                    const string loadSql = """
                        SELECT CAST(ESTADO AS INT) AS estado
                        FROM dbo.PQ_PRD_ORDENES_TRABAJO
                        WHERE ID_ORDEN_TRABAJO = @id
                        """;
                    int? estado = null;
                    await using (var cmd = OrdenesTrabajoRunnerHelpers.CreateCommand(connection, transaction, timeoutSeconds, loadSql))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            throw new OrdenesTrabajoRunnerHelpers.ValidationException($"OT {id} no encontrada.");
                        }

                        estado = Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
                    }

                    int nuevoEstado;
                    if (operacion == "cerrar")
                    {
                        if (estado != 1)
                        {
                            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                                "Solo se pueden cerrar OT en estado Abierta.");
                        }

                        nuevoEstado = 2;
                    }
                    else
                    {
                        if (estado != 2)
                        {
                            throw new OrdenesTrabajoRunnerHelpers.ValidationException(
                                "Solo se pueden reabrir OT en estado Cerrada.");
                        }

                        nuevoEstado = 1;
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
                        cmd.Parameters.AddWithValue("@estado", nuevoEstado);
                        cmd.Parameters.AddWithValue("@usuario", Math.Max(0, usuarioId));
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    actualizadas++;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new Dictionary<string, object?> { ["actualizadas"] = actualizadas });
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
