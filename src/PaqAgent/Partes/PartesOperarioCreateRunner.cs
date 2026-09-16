using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.10.2 — alta PartesOperario Open orquestada (espejo PHP PartesOperarioService::storeLocal).
/// Sin SP monolítico.
/// </summary>
public sealed class PartesOperarioCreateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var fechaParte = AsignacionesRunnerHelpers.NormalizeDate(
            AsignacionesRunnerHelpers.ExtractString(parameters, "fecha_parte"));
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id");

        if (string.IsNullOrWhiteSpace(database) || fechaParte is null || usuarioId is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "fecha_parte, usuario_id y _database son obligatorios.");
        }

        if (!AsignacionesRunnerHelpers.TryParseDateOnly(fechaParte, out _))
        {
            return Fail("VALIDATION", "La fecha del parte debe tener formato AAAA-MM-DD.");
        }

        var idTurno = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_turno");
        if (idTurno is <= 0)
        {
            idTurno = null;
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
                    throw new InvalidOperationException(PartesOperarioRunnerHelpers.TableMissingMessage);
                }

                var idOperario = await PartesOperarioRunnerHelpers.ResolveIdOperarioAsync(
                        connection, transaction, timeoutSeconds, usuarioId.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (idOperario is null)
                {
                    throw new PartesOperarioRunnerHelpers.NoLegajoException(
                        PartesOperarioRunnerHelpers.NoLegajoCreateMessage);
                }

                if (idTurno is not null
                    && await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_TURNOS", cancellationToken)
                        .ConfigureAwait(false)
                    && !await AsignacionesRunnerHelpers.TurnoExistsAsync(
                            connection, transaction, timeoutSeconds, idTurno.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.ValidationException("El turno informado no existe.");
                }

                if (await PartesOperarioRunnerHelpers.DuplicateExistsAsync(
                            connection,
                            transaction,
                            timeoutSeconds,
                            fechaParte,
                            idTurno,
                            idOperario.Value,
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(
                        PartesOperarioRunnerHelpers.DuplicateMessage);
                }

                var newId = await InsertParteAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        fechaParte,
                        idTurno,
                        idOperario.Value,
                        usuarioId.Value,
                        cancellationToken)
                    .ConfigureAwait(false);

                var payload = await LoadCreatePayloadAsync(
                        connection, transaction, timeoutSeconds, newId, cancellationToken)
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
        catch (PartesOperarioRunnerHelpers.ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
        }
        catch (PartesOperarioRunnerHelpers.ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (SqlException sex) when (PartesOperarioRunnerHelpers.IsUniqueViolation(sex))
        {
            return Fail("CONFLICT", PartesOperarioRunnerHelpers.DuplicateMessage);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<int> InsertParteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string fechaParte,
        int? idTurno,
        int idOperario,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO dbo.PQ_PRD_PARTES_OPERARIO
    (FECHA_PARTE, ID_TURNO, ID_OPERARIO, ESTADO, FECHA_ALTA, USUARIO_ALTA)
OUTPUT INSERTED.ID_PARTE_OPERARIO AS id
VALUES
    (CONVERT(date, @fecha, 23), @idTurno, @idOperario, 0, CONVERT(datetime, @fechaAlta, 120), @usuarioId);";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 10).Value = fechaParte;
        cmd.Parameters.Add("@idTurno", SqlDbType.Int).Value = idTurno.HasValue ? idTurno.Value : DBNull.Value;
        cmd.Parameters.Add("@idOperario", SqlDbType.Int).Value = idOperario;
        cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@fechaAlta", SqlDbType.NVarChar, 19).Value =
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private static async Task<Dictionary<string, object?>> LoadCreatePayloadAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ID_PARTE_OPERARIO AS INT) AS id,
    CAST(FECHA_PARTE AS DATE) AS fecha_parte,
    CAST(ID_TURNO AS INT) AS id_turno,
    CAST(ESTADO AS INT) AS estado
FROM dbo.PQ_PRD_PARTES_OPERARIO
WHERE ID_PARTE_OPERARIO = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("No se pudo crear el parte");
        }

        return new Dictionary<string, object?>
        {
            ["id"] = reader["id"] is DBNull ? 0 : Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
            ["fecha_parte"] = AsignacionesRunnerHelpers.FormatDateOnly(reader["fecha_parte"]),
            ["id_turno"] = reader["id_turno"] is DBNull
                ? null
                : Convert.ToInt32(reader["id_turno"], CultureInfo.InvariantCulture),
            ["estado"] = reader["estado"] is DBNull
                ? 0
                : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture)
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
