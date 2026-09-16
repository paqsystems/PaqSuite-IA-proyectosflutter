using System.Data;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.11.5 — Reclasificar entrada orquestado (espejo PHP PartesEntradasService::reclasificarLocal).
/// Supervisor: sin filtro de dueño. Parte Reviewed + circuito activo.
/// </summary>
public sealed class PartesEntradasReclasificarRunner
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
        var notasRevision = AsignacionesRunnerHelpers.ExtractString(parameters, "notas_revision");

        if (string.IsNullOrWhiteSpace(database)
            || idParteOperario is null or <= 0
            || idParteEntrada is null or <= 0
            || usuarioId is null or <= 0)
        {
            return PartesEntradasRunnerHelpers.Fail(
                "INVALID_PARAMETERS",
                "id_parte_operario, id_parte_entrada, usuario_id y _database son obligatorios.");
        }

        if (string.IsNullOrWhiteSpace(notasRevision))
        {
            return PartesEntradasRunnerHelpers.Fail(
                "INVALID_PARAMETERS",
                "El parametro notas_revision es obligatorio.");
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
                if (!await PartesOperarioRunnerHelpers.CircuitoAutorizacionActivoAsync(
                            connection, transaction, timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.ConflictException(
                        PartesEntradasRunnerHelpers.ConflictCircuitoMessage);
                }

                if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_OPERARIO", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesEntradasRunnerHelpers.ReclasificarNotFoundMessage);
                }

                int estado;
                try
                {
                    (estado, _) = await PartesOperarioRunnerHelpers.LoadEstadoAsync(
                            connection, transaction, timeoutSeconds, idParteOperario.Value, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (PartesOperarioRunnerHelpers.NotFoundException)
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesEntradasRunnerHelpers.ReclasificarNotFoundMessage);
                }

                if (estado != PartesOperarioRunnerHelpers.EstadoReviewed)
                {
                    throw new PartesOperarioRunnerHelpers.NotFoundException(
                        PartesEntradasRunnerHelpers.ReclasificarNotFoundMessage);
                }

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

                var sets = new List<string>
                {
                    "NOTAS_REVISION = @notasRevision",
                    "FECHA_REVISION = @fechaRev",
                    "ID_USUARIO_REVISION = @usuarioId",
                    "FECHA_MODIF = @fechaRev",
                    "USUARIO_MODIF = @usuarioId"
                };
                await using var cmd = new SqlCommand { Connection = connection, Transaction = transaction, CommandTimeout = timeoutSeconds };
                cmd.Parameters.Add("@notasRevision", SqlDbType.NVarChar, 2000).Value = notasRevision.Trim();
                cmd.Parameters.Add("@fechaRev", SqlDbType.NVarChar, 19).Value = PartesOperarioRunnerHelpers.NowSql();
                cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId.Value;
                cmd.Parameters.Add("@idParte", SqlDbType.Int).Value = idParteOperario.Value;
                cmd.Parameters.Add("@idEntrada", SqlDbType.Int).Value = idParteEntrada.Value;

                if (parameters.ContainsKey("id_asignacion_item"))
                {
                    sets.Add("ID_ASIGNACION_ITEM = @idItem");
                    var idItem = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_asignacion_item");
                    cmd.Parameters.Add("@idItem", SqlDbType.Int).Value =
                        PartesEntradasRunnerHelpers.DbValue(idItem is > 0 ? idItem : null);
                }

                if (parameters.ContainsKey("id_orden_trabajo"))
                {
                    sets.Add("ID_ORDEN_TRABAJO = @idOt");
                    var idOt = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo");
                    cmd.Parameters.Add("@idOt", SqlDbType.Int).Value =
                        PartesEntradasRunnerHelpers.DbValue(idOt is > 0 ? idOt : null);
                }

                if (parameters.ContainsKey("id_maquina"))
                {
                    sets.Add("ID_MAQUINA = @idMaquina");
                    var idMaquina = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_maquina");
                    cmd.Parameters.Add("@idMaquina", SqlDbType.Int).Value =
                        PartesEntradasRunnerHelpers.DbValue(idMaquina is > 0 ? idMaquina : null);
                }

                if (parameters.ContainsKey("id_operacion"))
                {
                    sets.Add("ID_OPERACION = @idOperacion");
                    var idOperacion = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_operacion");
                    cmd.Parameters.Add("@idOperacion", SqlDbType.Int).Value =
                        PartesEntradasRunnerHelpers.DbValue(idOperacion is > 0 ? idOperacion : null);
                }

                var idConcepto = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_concepto_tiempo");
                if (parameters.ContainsKey("id_concepto_tiempo") && idConcepto is > 0)
                {
                    sets.Add("ID_CONCEPTO_TIEMPO = @idConcepto");
                    cmd.Parameters.Add("@idConcepto", SqlDbType.Int).Value = idConcepto.Value;
                }

                cmd.CommandText = $@"
UPDATE dbo.PQ_PRD_PARTES_ENTRADAS
SET {string.Join(", ", sets)}
WHERE ID_PARTE_OPERARIO = @idParte AND ID_PARTE_ENTRADA = @idEntrada;";
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return PartesEntradasRunnerHelpers.Ok(new Dictionary<string, object?>
                {
                    ["id"] = idParteEntrada.Value
                });
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
