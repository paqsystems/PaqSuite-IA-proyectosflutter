using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.9.5 — Publicar Asignaciones (Draft→Published) orquestado.
/// Espejo PHP: assertPlanParaPublicar + congelarSnapshotItems + UPDATE ESTADO/FECHA_PUBLICACION.
/// Sin SP monolítico.
/// </summary>
public sealed class AsignacionesPublicarRunner
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

                var (estado, _, fechaAsignacion) = await AsignacionesRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (estado != 0)
                {
                    throw new AsignacionesRunnerHelpers.ConflictException(
                        "Solo asignaciones en estado Borrador pueden publicarse");
                }

                await AssertPlanParaPublicarAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, cancellationToken)
                    .ConfigureAwait(false);

                await CongelarSnapshotItemsAsync(
                        connection, transaction, timeoutSeconds, idAsignacion.Value, fechaAsignacion, cancellationToken)
                    .ConfigureAwait(false);

                var fechaPublicacion = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                const string updateSql = @"
UPDATE dbo.PQ_PRD_ASIGNACIONES
SET ESTADO = 1,
    FECHA_PUBLICACION = CONVERT(datetime, @fechaPub, 120)
WHERE ID_ASIGNACION = @id;";

                await using (var cmd = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@fechaPub", SqlDbType.NVarChar, 19).Value = fechaPublicacion;
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

    private static async Task AssertPlanParaPublicarAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacion,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                .ConfigureAwait(false))
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "Debe haber al menos una tarea planificada para publicar.");
        }

        var itemIds = new List<int>();
        const string itemsSql = @"
SELECT CAST(ID_ASIGNACION_ITEM AS INT) AS id
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
WHERE ID_ASIGNACION = @id;";
        await using (var cmd = new SqlCommand(itemsSql, connection, transaction) { CommandTimeout = timeoutSeconds })
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                itemIds.Add(Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture));
            }
        }

        if (itemIds.Count == 0)
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "Debe haber al menos una tarea planificada para publicar.");
        }

        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS", cancellationToken)
                .ConfigureAwait(false))
        {
            throw new AsignacionesRunnerHelpers.ValidationException(
                "No existe la tabla de operarios por tarea. Ejecute las migraciones de la base de datos de la empresa.");
        }

        var countsByItem = new Dictionary<int, int>();
        const string countsSql = @"
SELECT CAST(ID_ASIGNACION_ITEM AS INT) AS id_item, COUNT(*) AS c
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
WHERE ID_ASIGNACION_ITEM IN (SELECT CAST(ID_ASIGNACION_ITEM AS INT) FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS WHERE ID_ASIGNACION = @id)
GROUP BY ID_ASIGNACION_ITEM;";
        await using (var cmd = new SqlCommand(countsSql, connection, transaction) { CommandTimeout = timeoutSeconds })
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var itemId = Convert.ToInt32(reader["id_item"], CultureInfo.InvariantCulture);
                var count = Convert.ToInt32(reader["c"], CultureInfo.InvariantCulture);
                countsByItem[itemId] = count;
            }
        }

        foreach (var itemId in itemIds)
        {
            if ((countsByItem.TryGetValue(itemId, out var c) ? c : 0) == 0)
            {
                throw new AsignacionesRunnerHelpers.ValidationException(
                    "Cada tarea planificada debe tener al menos un operario asignado antes de publicar.");
            }
        }
    }

    private static async Task CongelarSnapshotItemsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacion,
        string? fechaRef,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var fecha = fechaRef ?? DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var hasStd = await AsignacionesRunnerHelpers.TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", cancellationToken)
            .ConfigureAwait(false);

        const string itemsSql = @"
SELECT
    CAST(ID_ASIGNACION_ITEM AS INT) AS id_item,
    CAST(ID_ARTICULO AS INT) AS id_articulo,
    CAST(ID_OPERACION AS INT) AS id_operacion,
    UNIDADES_HORA_STD AS unidades_hora_std
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
WHERE ID_ASIGNACION = @id;";

        var pending = new List<(int ItemId, int IdArt, int IdOp)>();
        await using (var cmd = new SqlCommand(itemsSql, connection, transaction) { CommandTimeout = timeoutSeconds })
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader["unidades_hora_std"] is not DBNull)
                {
                    continue;
                }

                if (reader["id_articulo"] is DBNull || reader["id_operacion"] is DBNull)
                {
                    continue;
                }

                pending.Add((
                    Convert.ToInt32(reader["id_item"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader["id_articulo"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader["id_operacion"], CultureInfo.InvariantCulture)));
            }
        }

        if (!hasStd || pending.Count == 0)
        {
            return;
        }

        foreach (var (itemId, idArt, idOp) in pending)
        {
            const string stdSql = @"
SELECT TOP 1 UNIDADES_HORA_STD
FROM dbo.PQ_PRD_ARTICULO_OPERACION_STD
WHERE ID_ARTICULO = @idArt
  AND ID_OPERACION = @idOp
  AND ACTIVO = 1
  AND (VIGENTE_DESDE IS NULL OR VIGENTE_DESDE <= CONVERT(date, @fecha, 23))
  AND (VIGENTE_HASTA IS NULL OR VIGENTE_HASTA >= CONVERT(date, @fecha, 23))
ORDER BY VIGENTE_DESDE DESC;";

            object? stdValue;
            await using (var cmd = new SqlCommand(stdSql, connection, transaction) { CommandTimeout = timeoutSeconds })
            {
                cmd.Parameters.Add("@idArt", SqlDbType.Int).Value = idArt;
                cmd.Parameters.Add("@idOp", SqlDbType.Int).Value = idOp;
                cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 10).Value = fecha;
                stdValue = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            if (stdValue is null or DBNull)
            {
                continue;
            }

            const string updSql = @"
UPDATE dbo.PQ_PRD_ASIGNACIONES_ITEMS
SET UNIDADES_HORA_STD = @std
WHERE ID_ASIGNACION_ITEM = @idItem;";
            await using var upd = new SqlCommand(updSql, connection, transaction) { CommandTimeout = timeoutSeconds };
            upd.Parameters.Add("@std", SqlDbType.Decimal).Value = stdValue;
            upd.Parameters["@std"].Precision = 18;
            upd.Parameters["@std"].Scale = 4;
            upd.Parameters.Add("@idItem", SqlDbType.Int).Value = itemId;
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
