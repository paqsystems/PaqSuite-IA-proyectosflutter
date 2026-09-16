using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.10.3 — Update cabecera PartesOperario Open orquestado (espejo PHP PartesOperarioService::updateLocal).
/// Sin SP monolítico.
/// </summary>
public sealed class PartesOperarioUpdateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idParteOperario = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_parte_operario");
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id");

        if (string.IsNullOrWhiteSpace(database)
            || idParteOperario is null or <= 0
            || usuarioId is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "id_parte_operario, usuario_id y _database son obligatorios.");
        }

        var observacionesRaw = AsignacionesRunnerHelpers.ExtractString(parameters, "observaciones");
        var observaciones = observacionesRaw?.Trim();
        if (observaciones is { Length: > 2000 })
        {
            return Fail("VALIDATION", "observaciones no puede superar 2000 caracteres.");
        }

        var observacionesProvided = parameters.ContainsKey("observaciones");
        if (observacionesProvided && string.IsNullOrWhiteSpace(observaciones))
        {
            observaciones = null;
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
                            connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_OPERARIO", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesOperarioRunnerHelpers.NotFoundMessage);
                }

                var idOperario = await PartesOperarioRunnerHelpers.ResolveIdOperarioAsync(
                        connection, transaction, timeoutSeconds, usuarioId.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (idOperario is null)
                {
                    throw new PartesOperarioRunnerHelpers.NoLegajoException(
                        PartesOperarioRunnerHelpers.NoLegajoMessage);
                }

                var (estado, idOperarioParte) = await LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (idOperarioParte != idOperario.Value)
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesOperarioRunnerHelpers.NotFoundMessage);
                }

                if (estado != 0)
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(
                        PartesOperarioRunnerHelpers.ConflictEditMessage);
                }

                const string updateSql = @"
UPDATE dbo.PQ_PRD_PARTES_OPERARIO
SET OBSERVACIONES = CASE WHEN @obsProvided = 1 THEN @obs ELSE OBSERVACIONES END,
    FECHA_MODIF = CONVERT(datetime, @fechaModif, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_PARTE_OPERARIO = @id
  AND ID_OPERARIO = @idOperario;";

                await using (var cmd = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@obsProvided", SqlDbType.Bit).Value = observacionesProvided;
                    cmd.Parameters.Add("@obs", SqlDbType.NVarChar, 2000).Value =
                        observacionesProvided
                            ? (string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones)
                            : DBNull.Value;
                    cmd.Parameters.Add("@fechaModif", SqlDbType.NVarChar, 19).Value =
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario.Value;
                    cmd.Parameters.Add("@idOperario", SqlDbType.Int).Value = idOperario.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var payload = await LoadUpdatePayloadAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
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
        catch (PartesOperarioRunnerHelpers.NoLegajoException nex)
        {
            return Fail("NO_LEGAJO", nex.Message);
        }
        catch (PartesOperarioRunnerHelpers.NotFoundException nfex)
        {
            return Fail("NOT_FOUND", nfex.Message);
        }
        catch (PartesOperarioRunnerHelpers.ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
        }
        catch (PartesOperarioRunnerHelpers.ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<(int Estado, int IdOperario)> LoadEstadoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ESTADO AS INT) AS estado,
    CAST(ID_OPERARIO AS INT) AS id_operario
FROM dbo.PQ_PRD_PARTES_OPERARIO
WHERE ID_PARTE_OPERARIO = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(
                PartesOperarioRunnerHelpers.NotFoundMessage);
        }

        var estado = reader["estado"] is DBNull
            ? 0
            : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
        var idOperario = reader["id_operario"] is DBNull
            ? 0
            : Convert.ToInt32(reader["id_operario"], CultureInfo.InvariantCulture);

        return (estado, idOperario);
    }

    private static async Task<Dictionary<string, object?>> LoadUpdatePayloadAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ID_PARTE_OPERARIO AS INT) AS id,
    CAST(OBSERVACIONES AS NVARCHAR(2000)) AS observaciones
FROM dbo.PQ_PRD_PARTES_OPERARIO
WHERE ID_PARTE_OPERARIO = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(
                PartesOperarioRunnerHelpers.NotFoundMessage);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = reader["id"] is DBNull ? 0 : Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
            ["observaciones"] = reader["observaciones"] is DBNull
                ? null
                : Convert.ToString(reader["observaciones"], CultureInfo.InvariantCulture)
        };
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
