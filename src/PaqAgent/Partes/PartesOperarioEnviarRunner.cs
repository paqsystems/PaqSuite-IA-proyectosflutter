using System.Data;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.10.4 — Enviar PartesOperario Open→Submitted orquestado (espejo PHP PartesOperarioService::enviarLocal).
/// Sin SP monolítico. Replica circuito_autorizacion_activo en company.
/// </summary>
public sealed class PartesOperarioEnviarRunner
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

                if (!await PartesOperarioRunnerHelpers.CircuitoAutorizacionActivoAsync(
                            connection, transaction, timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(
                        PartesOperarioRunnerHelpers.ConflictCircuitoEnviarMessage);
                }

                var idOperario = await PartesOperarioRunnerHelpers.ResolveIdOperarioAsync(
                        connection, transaction, timeoutSeconds, usuarioId.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (idOperario is null)
                {
                    throw new PartesOperarioRunnerHelpers.NoLegajoException(
                        PartesOperarioRunnerHelpers.NoLegajoMessage);
                }

                var (estado, idOperarioParte) = await PartesOperarioRunnerHelpers.LoadEstadoAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (idOperarioParte != idOperario.Value)
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesOperarioRunnerHelpers.NotFoundMessage);
                }

                if (estado != PartesOperarioRunnerHelpers.EstadoOpen)
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(
                        PartesOperarioRunnerHelpers.ConflictEnviarMessage);
                }

                await PartesOperarioRunnerHelpers.EnsureEntradasValidasParaEnviarAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
                    .ConfigureAwait(false);

                const string updateSql = @"
UPDATE dbo.PQ_PRD_PARTES_OPERARIO
SET ESTADO = @estado,
    FECHA_MODIF = CONVERT(datetime, @fechaModif, 120),
    USUARIO_MODIF = @usuarioId
WHERE ID_PARTE_OPERARIO = @id
  AND ID_OPERARIO = @idOperario;";

                await using (var cmd = new SqlCommand(updateSql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@estado", SqlDbType.Int).Value = PartesOperarioRunnerHelpers.EstadoSubmitted;
                    cmd.Parameters.Add("@fechaModif", SqlDbType.NVarChar, 19).Value =
                        PartesOperarioRunnerHelpers.NowSql();
                    cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario.Value;
                    cmd.Parameters.Add("@idOperario", SqlDbType.Int).Value = idOperario.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var payload = await PartesOperarioRunnerHelpers.LoadIdEstadoPayloadAsync(
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
