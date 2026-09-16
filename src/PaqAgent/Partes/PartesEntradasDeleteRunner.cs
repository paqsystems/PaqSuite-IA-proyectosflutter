using System.Data;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.11.4 — Delete entrada orquestado (espejo PHP PartesEntradasService::destroyLocal).
/// </summary>
public sealed class PartesEntradasDeleteRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = AsignacionesRunnerHelpers.ExtractString(parameters, "_database");
        var idParteOperario = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_parte_operario");
        var idParteEntrada = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_parte_entrada");
        var usuarioId = AsignacionesRunnerHelpers.ExtractInt(parameters, "usuario_id");

        if (string.IsNullOrWhiteSpace(database)
            || idParteOperario is null or <= 0
            || idParteEntrada is null or <= 0
            || usuarioId is null or <= 0)
        {
            return PartesEntradasRunnerHelpers.Fail(
                "INVALID_PARAMETERS",
                "id_parte_operario, id_parte_entrada, usuario_id y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return PartesEntradasRunnerHelpers.Degraded();
        }

        try
        {
            var connectionString = SqlConnectionStringFactory.Build(
                agentOptions.Sql, connectTimeoutSeconds: 15, databaseOverride: database);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await PartesEntradasRunnerHelpers.AssertParteOpenPropietarioAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, usuarioId.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_ENTRADAS", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new InvalidOperationException(PartesEntradasRunnerHelpers.TableMissingMessage);
                }

                const string existsSql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_PARTES_ENTRADAS
WHERE ID_PARTE_OPERARIO = @idParte AND ID_PARTE_ENTRADA = @idEntrada;";
                await using (var existsCmd = new SqlCommand(existsSql, connection, transaction)
                {
                    CommandTimeout = timeoutSeconds
                })
                {
                    existsCmd.Parameters.Add("@idParte", SqlDbType.Int).Value = idParteOperario.Value;
                    existsCmd.Parameters.Add("@idEntrada", SqlDbType.Int).Value = idParteEntrada.Value;
                    var scalar = await existsCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    if (scalar is null or DBNull)
                    {
                        throw new PartesOperarioRunnerHelpers.NotFoundException(
                            PartesEntradasRunnerHelpers.EntradaNotFoundMessage);
                    }
                }

                const string deleteSql = @"
DELETE FROM dbo.PQ_PRD_PARTES_ENTRADAS
WHERE ID_PARTE_OPERARIO = @idParte AND ID_PARTE_ENTRADA = @idEntrada;";
                await using (var deleteCmd = new SqlCommand(deleteSql, connection, transaction)
                {
                    CommandTimeout = timeoutSeconds
                })
                {
                    deleteCmd.Parameters.Add("@idParte", SqlDbType.Int).Value = idParteOperario.Value;
                    deleteCmd.Parameters.Add("@idEntrada", SqlDbType.Int).Value = idParteEntrada.Value;
                    await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return PartesEntradasRunnerHelpers.Ok(new Dictionary<string, object?>());
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            return PartesEntradasRunnerHelpers.MapMutationException(ex);
        }
    }
}
