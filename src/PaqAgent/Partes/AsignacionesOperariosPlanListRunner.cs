using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>D6.9.12 — OperariosPlan.List orquestado (catálogo + plan por ítem; solo Draft).</summary>
public sealed class AsignacionesOperariosPlanListRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idAsignacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_asignacion");

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
                agentOptions.Sql, connectTimeoutSeconds: 15, databaseOverride: database);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var (estado, _, _) = await AsignacionesRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (estado != 0)
                {
                    throw new AsignacionesRunnerHelpers.ConflictException(
                        "Solo asignaciones en estado Borrador permiten planificar operarios");
                }

                var operarios = new List<Dictionary<string, object?>>();
                if (await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_SUELD_LEGAJOS", cancellationToken)
                        .ConfigureAwait(false))
                {
                    const string legSql = @"
SELECT CAST(ID AS INT) AS id, CAST(NRO_LEGAJO AS NVARCHAR(50)) AS nro_legajo,
       LTRIM(RTRIM(ISNULL(APELLIDO, N'') + N', ' + ISNULL(NOMBRE, N''))) AS nombre_completo
FROM dbo.PQ_SUELD_LEGAJOS
ORDER BY NRO_LEGAJO;";
                    await using var cmd = new SqlCommand(legSql, connection, transaction) { CommandTimeout = timeoutSeconds };
                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var nombre = reader["nombre_completo"] is DBNull
                            ? null
                            : Convert.ToString(reader["nombre_completo"], CultureInfo.InvariantCulture)?.Trim().Trim(',');
                        var nro = reader["nro_legajo"] is DBNull
                            ? null
                            : Convert.ToString(reader["nro_legajo"], CultureInfo.InvariantCulture);
                        operarios.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["id"] = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                            ["nro_legajo"] = nro,
                            ["nombre_completo"] = string.IsNullOrWhiteSpace(nombre) ? nro : nombre
                        });
                    }
                }

                var items = new List<Dictionary<string, object?>>();
                if (await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                        .ConfigureAwait(false))
                {
                    const string itemSql = @"
SELECT CAST(ID_ASIGNACION_ITEM AS INT) AS id_item
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
WHERE ID_ASIGNACION = @id
ORDER BY ID_ASIGNACION_ITEM;";
                    var itemIds = new List<int>();
                    await using (var cmd = new SqlCommand(itemSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                    {
                        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion.Value;
                        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            itemIds.Add(Convert.ToInt32(reader["id_item"], CultureInfo.InvariantCulture));
                        }
                    }

                    var hasOps = await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS", cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var iid in itemIds)
                    {
                        var ids = new List<int>();
                        if (hasOps)
                        {
                            const string opSql = @"
SELECT CAST(ID_OPERARIO AS INT) AS id_operario
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
WHERE ID_ASIGNACION_ITEM = @iid;";
                            await using var cmd = new SqlCommand(opSql, connection, transaction) { CommandTimeout = timeoutSeconds };
                            cmd.Parameters.Add("@iid", SqlDbType.Int).Value = iid;
                            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                ids.Add(Convert.ToInt32(reader["id_operario"], CultureInfo.InvariantCulture));
                            }
                        }

                        items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["id_asignacion_item"] = iid,
                            ["id_operarios"] = ids
                        });
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Ok(new Dictionary<string, object?>
                {
                    ["operarios"] = operarios,
                    ["items"] = items
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
