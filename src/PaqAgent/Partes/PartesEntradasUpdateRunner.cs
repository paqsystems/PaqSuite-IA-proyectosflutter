using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.11.3 — Update entrada orquestado (espejo PHP PartesEntradasService::updateLocal).
/// No acepta ni modifica ID_ASIGNACION_ITEM / ORIGEN_CARGA.
/// </summary>
public sealed class PartesEntradasUpdateRunner
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

                var current = await LoadEntradaAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, idParteEntrada.Value, cancellationToken)
                    .ConfigureAwait(false);

                var minutos = parameters.ContainsKey("minutos")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "minutos")
                    : current.Minutos;
                var fechaDesde = parameters.ContainsKey("fecha_hora_desde")
                    ? PartesEntradasRunnerHelpers.ParseDateTime(parameters["fecha_hora_desde"])
                    : current.FechaDesde;
                var fechaHasta = parameters.ContainsKey("fecha_hora_hasta")
                    ? PartesEntradasRunnerHelpers.ParseDateTime(parameters["fecha_hora_hasta"])
                    : current.FechaHasta;

                if (parameters.ContainsKey("fecha_hora_desde")
                    || parameters.ContainsKey("fecha_hora_hasta")
                    || parameters.ContainsKey("minutos"))
                {
                    if (minutos is null && fechaDesde is not null && fechaHasta is not null)
                    {
                        minutos = (int)Math.Round((fechaHasta.Value - fechaDesde.Value).TotalMinutes);
                    }

                    if (minutos is null or < 1)
                    {
                        PartesEntradasRunnerHelpers.ThrowField(
                            "minutos",
                            "Indique duración en minutos o intervalo.",
                            "Debe indicar minutos o intervalo de tiempo");
                    }
                }

                var idConcepto = parameters.ContainsKey("id_concepto_tiempo")
                    ? AsignacionesRunnerHelpers.ExtractInt(parameters, "id_concepto_tiempo")
                    : current.IdConcepto;
                var esProductivo = true;
                if (parameters.ContainsKey("id_concepto_tiempo"))
                {
                    if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                                connection, transaction, timeoutSeconds, "PQ_PRD_CONCEPTOS_TIEMPO", cancellationToken)
                            .ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(PartesEntradasRunnerHelpers.ConceptosMissingMessage);
                    }

                    if (idConcepto is not > 0)
                    {
                        PartesEntradasRunnerHelpers.ThrowField(
                            "id_concepto_tiempo",
                            "El concepto de tiempo informado no existe.",
                            "Errores de validación");
                    }

                    var (prod, exists) = await PartesEntradasRunnerHelpers.LoadConceptoAsync(
                            connection, transaction, timeoutSeconds, idConcepto.Value, cancellationToken)
                        .ConfigureAwait(false);
                    if (!exists)
                    {
                        PartesEntradasRunnerHelpers.ThrowField(
                            "id_concepto_tiempo",
                            "El concepto de tiempo informado no existe.",
                            "Errores de validación");
                    }

                    esProductivo = prod;
                }
                else if (idConcepto is > 0
                    && await AsignacionesRunnerHelpers.TableExistsAsync(
                            connection, transaction, timeoutSeconds, "PQ_PRD_CONCEPTOS_TIEMPO", cancellationToken)
                        .ConfigureAwait(false))
                {
                    var (prod, exists) = await PartesEntradasRunnerHelpers.LoadConceptoAsync(
                            connection, transaction, timeoutSeconds, idConcepto.Value, cancellationToken)
                        .ConfigureAwait(false);
                    if (exists)
                    {
                        esProductivo = prod;
                    }
                }

                var unidadesHechas = parameters.ContainsKey("unidades_hechas")
                    ? PartesEntradasRunnerHelpers.ExtractDouble(parameters, "unidades_hechas")
                    : current.UnidadesHechas;
                var unidadesMerma = parameters.ContainsKey("unidades_merma")
                    ? PartesEntradasRunnerHelpers.ExtractDouble(parameters, "unidades_merma")
                    : current.UnidadesMerma;
                var unidadesRetrabajo = parameters.ContainsKey("unidades_retrabajo")
                    ? PartesEntradasRunnerHelpers.ExtractDouble(parameters, "unidades_retrabajo")
                    : current.UnidadesRetrabajo;

                if (!esProductivo && ((unidadesHechas ?? 0) > 0 || (unidadesMerma ?? 0) > 0 || (unidadesRetrabajo ?? 0) > 0))
                {
                    PartesEntradasRunnerHelpers.ThrowField(
                        "unidades_hechas",
                        "El concepto no productivo no admite unidades.",
                        "Concepto no productivo no permite unidades");
                }

                int? idMaquina = current.IdMaquina;
                if (parameters.ContainsKey("id_maquina"))
                {
                    idMaquina = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_maquina");
                    if (idMaquina is > 0
                        && await AsignacionesRunnerHelpers.TableExistsAsync(
                                connection, transaction, timeoutSeconds, "PQ_PRD_MAQUINAS", cancellationToken)
                            .ConfigureAwait(false)
                        && !await PartesEntradasRunnerHelpers.MaquinaExistsAsync(
                                connection, transaction, timeoutSeconds, idMaquina.Value, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        PartesEntradasRunnerHelpers.ThrowField(
                            "id_maquina",
                            "La máquina informada no existe.",
                            "Errores de validación");
                    }
                }

                var notas = parameters.ContainsKey("notas")
                    ? AsignacionesRunnerHelpers.ExtractString(parameters, "notas")
                    : current.Notas;

                const string sql = @"
UPDATE dbo.PQ_PRD_PARTES_ENTRADAS
SET ID_CONCEPTO_TIEMPO = @idConcepto,
    MINUTOS = @minutos,
    FECHA_HORA_DESDE = @desde,
    FECHA_HORA_HASTA = @hasta,
    UNIDADES_HECHAS = @uh,
    UNIDADES_MERMA = @um,
    UNIDADES_RETRABAJO = @ur,
    NOTAS = @notas,
    ID_MAQUINA = @idMaquina,
    FECHA_MODIF = @fechaModif,
    USUARIO_MODIF = @usuarioModif
WHERE ID_PARTE_OPERARIO = @idParte AND ID_PARTE_ENTRADA = @idEntrada;";

                await using (var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds })
                {
                    cmd.Parameters.Add("@idConcepto", SqlDbType.Int).Value =
                        PartesEntradasRunnerHelpers.DbValue(idConcepto);
                    cmd.Parameters.Add("@minutos", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(minutos);
                    cmd.Parameters.Add("@desde", SqlDbType.DateTime).Value =
                        PartesEntradasRunnerHelpers.DbValue(fechaDesde);
                    cmd.Parameters.Add("@hasta", SqlDbType.DateTime).Value =
                        PartesEntradasRunnerHelpers.DbValue(fechaHasta);
                    cmd.Parameters.Add("@uh", SqlDbType.Float).Value =
                        PartesEntradasRunnerHelpers.DbValue(esProductivo ? unidadesHechas : null);
                    cmd.Parameters.Add("@um", SqlDbType.Float).Value =
                        PartesEntradasRunnerHelpers.DbValue(esProductivo ? unidadesMerma : null);
                    cmd.Parameters.Add("@ur", SqlDbType.Float).Value =
                        PartesEntradasRunnerHelpers.DbValue(esProductivo ? unidadesRetrabajo : null);
                    cmd.Parameters.Add("@notas", SqlDbType.NVarChar, 2000).Value =
                        PartesEntradasRunnerHelpers.DbValue(notas);
                    cmd.Parameters.Add("@idMaquina", SqlDbType.Int).Value =
                        PartesEntradasRunnerHelpers.DbValue(idMaquina is > 0 ? idMaquina : null);
                    cmd.Parameters.Add("@fechaModif", SqlDbType.NVarChar, 19).Value =
                        PartesOperarioRunnerHelpers.NowSql();
                    cmd.Parameters.Add("@usuarioModif", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@idParte", SqlDbType.Int).Value = idParteOperario.Value;
                    cmd.Parameters.Add("@idEntrada", SqlDbType.Int).Value = idParteEntrada.Value;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return PartesEntradasRunnerHelpers.Ok(new Dictionary<string, object?>
                {
                    ["id"] = idParteEntrada.Value,
                    ["id_concepto_tiempo"] = idConcepto,
                    ["minutos"] = minutos
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

    private sealed record EntradaRow(
        int? IdConcepto,
        int? Minutos,
        DateTime? FechaDesde,
        DateTime? FechaHasta,
        double? UnidadesHechas,
        double? UnidadesMerma,
        double? UnidadesRetrabajo,
        string? Notas,
        int? IdMaquina);

    private static async Task<EntradaRow> LoadEntradaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        int idParteEntrada,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ID_CONCEPTO_TIEMPO AS INT) AS id_concepto,
    CAST(MINUTOS AS INT) AS minutos,
    FECHA_HORA_DESDE AS fecha_desde,
    FECHA_HORA_HASTA AS fecha_hasta,
    CAST(UNIDADES_HECHAS AS FLOAT) AS unidades_hechas,
    CAST(UNIDADES_MERMA AS FLOAT) AS unidades_merma,
    CAST(UNIDADES_RETRABAJO AS FLOAT) AS unidades_retrabajo,
    CAST(NOTAS AS NVARCHAR(2000)) AS notas,
    CAST(ID_MAQUINA AS INT) AS id_maquina
FROM dbo.PQ_PRD_PARTES_ENTRADAS
WHERE ID_PARTE_OPERARIO = @idParte AND ID_PARTE_ENTRADA = @idEntrada;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@idParte", SqlDbType.Int).Value = idParteOperario;
        cmd.Parameters.Add("@idEntrada", SqlDbType.Int).Value = idParteEntrada;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(PartesEntradasRunnerHelpers.EntradaNotFoundMessage);
        }

        int? ReadInt(string col) =>
            reader[col] is DBNull ? null : Convert.ToInt32(reader[col], CultureInfo.InvariantCulture);
        double? ReadDbl(string col) =>
            reader[col] is DBNull ? null : Convert.ToDouble(reader[col], CultureInfo.InvariantCulture);

        return new EntradaRow(
            ReadInt("id_concepto"),
            ReadInt("minutos"),
            reader["fecha_desde"] is DBNull ? null : Convert.ToDateTime(reader["fecha_desde"], CultureInfo.InvariantCulture),
            reader["fecha_hasta"] is DBNull ? null : Convert.ToDateTime(reader["fecha_hasta"], CultureInfo.InvariantCulture),
            ReadDbl("unidades_hechas"),
            ReadDbl("unidades_merma"),
            ReadDbl("unidades_retrabajo"),
            reader["notas"] is DBNull ? null : Convert.ToString(reader["notas"], CultureInfo.InvariantCulture),
            ReadInt("id_maquina"));
    }
}
