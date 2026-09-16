using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.11.2 — Create entrada orquestado (espejo PHP PartesEntradasService::storeLocal).
/// </summary>
public sealed class PartesEntradasCreateRunner
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

        if (string.IsNullOrWhiteSpace(database) || idParteOperario is null or <= 0 || usuarioId is null or <= 0)
        {
            return PartesEntradasRunnerHelpers.Fail(
                "INVALID_PARAMETERS",
                "id_parte_operario, usuario_id y _database son obligatorios.");
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
                var payload = await CreateCoreAsync(
                        connection, transaction, timeoutSeconds, idParteOperario.Value, usuarioId.Value, parameters, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return PartesEntradasRunnerHelpers.Ok(payload);
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

    private static async Task<Dictionary<string, object?>> CreateCoreAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        int usuarioId,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var idOperario = await PartesEntradasRunnerHelpers.AssertParteOpenPropietarioAsync(
                connection, transaction, timeoutSeconds, idParteOperario, usuarioId, cancellationToken)
            .ConfigureAwait(false);

        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_ENTRADAS", cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(PartesEntradasRunnerHelpers.TableMissingMessage);
        }

        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_CONCEPTOS_TIEMPO", cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(PartesEntradasRunnerHelpers.ConceptosMissingMessage);
        }

        var idAsignacionItem = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_asignacion_item");
        var idOrdenTrabajo = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_orden_trabajo");
        if (idAsignacionItem is not > 0 && idOrdenTrabajo is not > 0)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_orden_trabajo",
                "Indique la orden de trabajo o un ítem de asignación planificada.",
                "Orden de trabajo requerida para carga sin ítem de asignación");
        }

        var fechaDesde = PartesEntradasRunnerHelpers.ParseDateTime(
            parameters.TryGetValue("fecha_hora_desde", out var fd) ? fd : null);
        var fechaHasta = PartesEntradasRunnerHelpers.ParseDateTime(
            parameters.TryGetValue("fecha_hora_hasta", out var fh) ? fh : null);
        if (fechaHasta is not null && fechaDesde is not null && fechaHasta <= fechaDesde)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "fecha_hora_hasta",
                "Debe ser posterior a fecha desde.",
                "Fecha hasta debe ser posterior a fecha desde");
        }

        var minutosTotal = AsignacionesRunnerHelpers.ExtractInt(parameters, "minutos");
        if (minutosTotal is null && fechaDesde is not null && fechaHasta is not null)
        {
            minutosTotal = (int)Math.Round((fechaHasta.Value - fechaDesde.Value).TotalMinutes);
        }

        if (minutosTotal is null or < 1)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "minutos",
                "Indique duración en minutos o intervalo.",
                "Debe indicar minutos o intervalo de tiempo");
        }

        var npMin = AsignacionesRunnerHelpers.ExtractInt(parameters, "minutos_no_productivos") ?? 0;
        var idConceptoNp = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_concepto_tiempo_no_productivo");
        if (npMin > 0)
        {
            if (idConceptoNp is not > 0)
            {
                PartesEntradasRunnerHelpers.ThrowField(
                    "id_concepto_tiempo_no_productivo",
                    "Indique concepto para el tiempo no productivo.",
                    "Concepto no productivo requerido");
            }

            var (npProductivo, npExists) = await PartesEntradasRunnerHelpers.LoadConceptoAsync(
                    connection, transaction, timeoutSeconds, idConceptoNp.Value, cancellationToken)
                .ConfigureAwait(false);
            if (!npExists || npProductivo)
            {
                PartesEntradasRunnerHelpers.ThrowField(
                    "id_concepto_tiempo_no_productivo",
                    "Debe ser un concepto no productivo.",
                    "Concepto no productivo inválido");
            }
        }

        if (npMin >= minutosTotal.Value)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "minutos_no_productivos",
                "Deben ser menores que el total de minutos del intervalo.",
                "Minutos no productivos demasiado altos");
        }

        var minutosProductivos = minutosTotal.Value - npMin;
        if (minutosProductivos < 1)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "minutos",
                "El intervalo debe dejar al menos 1 minuto productivo tras descontar lo no productivo.",
                "Tiempo productivo insuficiente");
        }

        var idConcepto = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_concepto_tiempo");
        if (idConcepto is not > 0)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_concepto_tiempo",
                "El concepto de tiempo informado no existe.",
                "Errores de validación");
        }

        var (esProductivo, conceptoExists) = await PartesEntradasRunnerHelpers.LoadConceptoAsync(
                connection, transaction, timeoutSeconds, idConcepto.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!conceptoExists)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_concepto_tiempo",
                "El concepto de tiempo informado no existe.",
                "Errores de validación");
        }

        if (npMin > 0 && !esProductivo)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_concepto_tiempo",
                "Si informa tiempo no productivo, el concepto principal debe ser productivo.",
                "Use concepto productivo para la carga principal");
        }

        var unidadesHechas = PartesEntradasRunnerHelpers.ExtractDouble(parameters, "unidades_hechas");
        var unidadesMerma = PartesEntradasRunnerHelpers.ExtractDouble(parameters, "unidades_merma");
        var unidadesRetrabajo = PartesEntradasRunnerHelpers.ExtractDouble(parameters, "unidades_retrabajo");
        if (!esProductivo && ((unidadesHechas ?? 0) > 0 || (unidadesMerma ?? 0) > 0 || (unidadesRetrabajo ?? 0) > 0))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "unidades_hechas",
                "El concepto no productivo no admite unidades.",
                "Concepto no productivo no permite unidades");
        }

        var maquinasExisten = await AsignacionesRunnerHelpers.TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_MAQUINAS", cancellationToken)
            .ConfigureAwait(false);
        var idMaquinaPayload = AsignacionesRunnerHelpers.ExtractInt(parameters, "id_maquina");
        if (maquinasExisten && idMaquinaPayload is not > 0)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_maquina",
                "Seleccione una máquina.",
                "Máquina requerida");
        }

        if (maquinasExisten && idMaquinaPayload is > 0
            && !await PartesEntradasRunnerHelpers.MaquinaExistsAsync(
                    connection, transaction, timeoutSeconds, idMaquinaPayload.Value, cancellationToken)
                .ConfigureAwait(false))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_maquina",
                "La máquina informada no existe.",
                "Errores de validación");
        }

        int? idArticulo = null;
        int? idOperacion = null;
        int? idTipoTarea = null;
        int? idMaquina = idMaquinaPayload is > 0 ? idMaquinaPayload : null;
        int? idOtPersist = null;
        int? idItemPersist = null;
        string? origenCarga = null;
        int? nroOrdenOperacionPersist = null;

        if (idAsignacionItem is > 0)
        {
            await EnsureItemAsignadoAsync(
                    connection, transaction, timeoutSeconds, idAsignacionItem.Value, idOperario, cancellationToken)
                .ConfigureAwait(false);
            var item = await LoadAsignacionItemAsync(
                    connection, transaction, timeoutSeconds, idAsignacionItem.Value, cancellationToken)
                .ConfigureAwait(false);
            idItemPersist = idAsignacionItem.Value;
            idOtPersist = item.IdOrdenTrabajo;
            idArticulo = item.IdArticulo;
            idOperacion = item.IdOperacion;
            idTipoTarea = item.IdTipoTarea;
            idMaquina ??= item.IdMaquina;
            nroOrdenOperacionPersist = item.NroOrdenOperacion;
            origenCarga = "plan";
        }
        else if (idOrdenTrabajo is > 0)
        {
            var ot = await LoadOrdenTrabajoAsync(
                    connection, transaction, timeoutSeconds, idOrdenTrabajo.Value, cancellationToken)
                .ConfigureAwait(false);
            idOtPersist = idOrdenTrabajo.Value;
            idArticulo = ot.IdArticulo;
            idOperacion = ot.IdOperacion;
            origenCarga = "libre";
            var nroSolicitado = AsignacionesRunnerHelpers.ExtractInt(parameters, "nro_orden_operacion");
            nroOrdenOperacionPersist = ResolveNroOrdenDesdeOt(ot.NroOrden, nroSolicitado);
        }

        if (maquinasExisten && idMaquina is not > 0)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_maquina",
                "Seleccione una máquina.",
                "Máquina requerida");
        }

        var hasNroCol = await AsignacionesRunnerHelpers.ColumnExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_ENTRADAS", "NRO_ORDEN_OPERACION", cancellationToken)
            .ConfigureAwait(false);
        var notas = AsignacionesRunnerHelpers.ExtractString(parameters, "notas");
        var fechaAlta = PartesOperarioRunnerHelpers.NowSql();

        var idEntrada = await InsertEntradaAsync(
                connection,
                transaction,
                timeoutSeconds,
                idParteOperario,
                idItemPersist,
                idOtPersist,
                idArticulo,
                idOperacion,
                idTipoTarea,
                idMaquina,
                idConcepto.Value,
                minutosProductivos,
                fechaDesde,
                fechaHasta,
                esProductivo ? unidadesHechas : null,
                esProductivo ? unidadesMerma : null,
                esProductivo ? unidadesRetrabajo : null,
                notas,
                fechaAlta,
                usuarioId,
                origenCarga,
                hasNroCol ? nroOrdenOperacionPersist : null,
                hasNroCol,
                cancellationToken)
            .ConfigureAwait(false);

        int? idEntradaNp = null;
        if (npMin > 0 && idConceptoNp is > 0)
        {
            idEntradaNp = await InsertEntradaAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    idParteOperario,
                    idItemPersist,
                    idOtPersist,
                    idArticulo,
                    idOperacion,
                    idTipoTarea,
                    idMaquina,
                    idConceptoNp.Value,
                    npMin,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    fechaAlta,
                    usuarioId,
                    origenCarga,
                    hasNroCol ? nroOrdenOperacionPersist : null,
                    hasNroCol,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var resultado = new Dictionary<string, object?>
        {
            ["id"] = idEntrada,
            ["id_concepto_tiempo"] = idConcepto.Value,
            ["minutos"] = minutosProductivos,
            ["unidades_hechas"] = esProductivo ? unidadesHechas : null,
            ["id_orden_trabajo"] = idOtPersist,
            ["origen_carga"] = origenCarga,
            ["id_entrada_no_productiva"] = idEntradaNp
        };
        if (hasNroCol)
        {
            resultado["nro_orden_operacion"] = nroOrdenOperacionPersist;
        }

        return resultado;
    }

    private static async Task EnsureItemAsignadoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacionItem,
        int idOperario,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS", cancellationToken)
                .ConfigureAwait(false))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_asignacion_item",
                "No está asignado a este ítem planificado.",
                "Asignación no válida para su legajo");
        }

        const string sql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS
WHERE ID_ASIGNACION_ITEM = @idItem AND ID_OPERARIO = @idOperario;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idAsignacionItem;
        cmd.Parameters.Add("@idOperario", SqlDbType.Int).Value = idOperario;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalar is null or DBNull)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_asignacion_item",
                "No está asignado a este ítem planificado.",
                "Asignación no válida para su legajo");
        }
    }

    private sealed record ItemRow(
        int? IdOrdenTrabajo,
        int? IdArticulo,
        int? IdOperacion,
        int? IdTipoTarea,
        int? IdMaquina,
        int? NroOrdenOperacion);

    private static async Task<ItemRow> LoadAsignacionItemAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacionItem,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                .ConfigureAwait(false))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_asignacion_item",
                "Ítem no encontrado.",
                "Ítem de asignación inexistente");
        }

        const string sql = @"
SELECT TOP 1
    CAST(ID_ORDEN_TRABAJO AS INT) AS id_orden_trabajo,
    CAST(ID_ARTICULO AS INT) AS id_articulo,
    CAST(ID_OPERACION AS INT) AS id_operacion,
    CAST(ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
    CAST(ID_MAQUINA AS INT) AS id_maquina,
    CAST(NRO_ORDEN_OPERACION AS INT) AS nro_orden_operacion
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
WHERE ID_ASIGNACION_ITEM = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacionItem;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_asignacion_item",
                "Ítem no encontrado.",
                "Ítem de asignación inexistente");
        }

        int? ReadInt(string col) =>
            reader[col] is DBNull ? null : Convert.ToInt32(reader[col], CultureInfo.InvariantCulture);

        return new ItemRow(
            ReadInt("id_orden_trabajo"),
            ReadInt("id_articulo"),
            ReadInt("id_operacion"),
            ReadInt("id_tipo_tarea"),
            ReadInt("id_maquina"),
            ReadInt("nro_orden_operacion") is > 0 ? ReadInt("nro_orden_operacion") : null);
    }

    private sealed record OtRow(int? IdArticulo, int? IdOperacion, int? NroOrden);

    private static async Task<OtRow> LoadOrdenTrabajoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrdenTrabajo,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", cancellationToken)
                .ConfigureAwait(false))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_orden_trabajo",
                "OT inválida.",
                "Orden de trabajo no encontrada");
        }

        const string sql = @"
SELECT TOP 1
    CAST(ID_ARTICULO AS INT) AS id_articulo,
    CAST(ID_OPERACION AS INT) AS id_operacion,
    CAST(NRO_ORDEN AS INT) AS nro_orden
FROM dbo.PQ_PRD_ORDENES_TRABAJO
WHERE ID_ORDEN_TRABAJO = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idOrdenTrabajo;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "id_orden_trabajo",
                "OT inválida.",
                "Orden de trabajo no encontrada");
        }

        int? ReadInt(string col) =>
            reader[col] is DBNull ? null : Convert.ToInt32(reader[col], CultureInfo.InvariantCulture);

        return new OtRow(ReadInt("id_articulo"), ReadInt("id_operacion"), ReadInt("nro_orden"));
    }

    private static int? ResolveNroOrdenDesdeOt(int? nroOrdenOt, int? nroSolicitado)
    {
        if (nroOrdenOt is not > 0)
        {
            return null;
        }

        if (nroSolicitado is > 0 && nroSolicitado.Value != nroOrdenOt.Value)
        {
            PartesEntradasRunnerHelpers.ThrowField(
                "nro_orden_operacion",
                "El número de orden de operación no coincide con la OT seleccionada.");
        }

        return nroOrdenOt;
    }

    private static async Task<int> InsertEntradaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        int? idAsignacionItem,
        int? idOrdenTrabajo,
        int? idArticulo,
        int? idOperacion,
        int? idTipoTarea,
        int? idMaquina,
        int idConcepto,
        int minutos,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        double? unidadesHechas,
        double? unidadesMerma,
        double? unidadesRetrabajo,
        string? notas,
        string fechaAlta,
        int usuarioId,
        string? origenCarga,
        int? nroOrdenOperacion,
        bool hasNroCol,
        CancellationToken cancellationToken)
    {
        var nroSql = hasNroCol ? ", NRO_ORDEN_OPERACION" : string.Empty;
        var nroVal = hasNroCol ? ", @nro" : string.Empty;
        var sql = $@"
INSERT INTO dbo.PQ_PRD_PARTES_ENTRADAS
    (ID_PARTE_OPERARIO, ID_ASIGNACION_ITEM, ID_ORDEN_TRABAJO, ID_ARTICULO, ID_OPERACION, ID_TIPO_TAREA, ID_MAQUINA,
     ID_CONCEPTO_TIEMPO, MINUTOS, FECHA_HORA_DESDE, FECHA_HORA_HASTA, UNIDADES_HECHAS, UNIDADES_MERMA, UNIDADES_RETRABAJO,
     NOTAS, FECHA_ALTA, USUARIO_ALTA, ORIGEN_CARGA{nroSql})
VALUES
    (@idParte, @idItem, @idOt, @idArticulo, @idOperacion, @idTipoTarea, @idMaquina,
     @idConcepto, @minutos, @desde, @hasta, @uh, @um, @ur,
     @notas, @fechaAlta, @usuarioAlta, @origen{nroVal});
SELECT CAST(SCOPE_IDENTITY() AS INT);";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@idParte", SqlDbType.Int).Value = idParteOperario;
        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(idAsignacionItem);
        cmd.Parameters.Add("@idOt", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(idOrdenTrabajo);
        cmd.Parameters.Add("@idArticulo", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(idArticulo);
        cmd.Parameters.Add("@idOperacion", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(idOperacion);
        cmd.Parameters.Add("@idTipoTarea", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(idTipoTarea);
        cmd.Parameters.Add("@idMaquina", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(idMaquina);
        cmd.Parameters.Add("@idConcepto", SqlDbType.Int).Value = idConcepto;
        cmd.Parameters.Add("@minutos", SqlDbType.Int).Value = minutos;
        cmd.Parameters.Add("@desde", SqlDbType.DateTime).Value = PartesEntradasRunnerHelpers.DbValue(fechaDesde);
        cmd.Parameters.Add("@hasta", SqlDbType.DateTime).Value = PartesEntradasRunnerHelpers.DbValue(fechaHasta);
        cmd.Parameters.Add("@uh", SqlDbType.Decimal).Value = PartesEntradasRunnerHelpers.DbValue(unidadesHechas);
        cmd.Parameters.Add("@um", SqlDbType.Decimal).Value = PartesEntradasRunnerHelpers.DbValue(unidadesMerma);
        cmd.Parameters.Add("@ur", SqlDbType.Decimal).Value = PartesEntradasRunnerHelpers.DbValue(unidadesRetrabajo);
        cmd.Parameters.Add("@notas", SqlDbType.NVarChar, 2000).Value = PartesEntradasRunnerHelpers.DbValue(notas);
        cmd.Parameters.Add("@fechaAlta", SqlDbType.NVarChar, 19).Value = fechaAlta;
        cmd.Parameters.Add("@usuarioAlta", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@origen", SqlDbType.NVarChar, 20).Value = PartesEntradasRunnerHelpers.DbValue(origenCarga);
        if (hasNroCol)
        {
            cmd.Parameters.Add("@nro", SqlDbType.Int).Value = PartesEntradasRunnerHelpers.DbValue(nroOrdenOperacion);
        }

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }
}
