using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PaqAgent.Partes;

/// <summary>
/// Helpers SQL compartidos para runners OT Update/Delete (catálogo, std, sync, formatItem).
/// </summary>
internal static class OrdenesTrabajoRunnerHelpers
{
    internal sealed class ValidationException(string message) : Exception(message);

    internal sealed class ConflictException(string message) : Exception(message);

    internal static async Task<bool> TableExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection,
            transaction,
            timeoutSeconds,
            "SELECT 1 FROM sys.tables WHERE name = @n AND schema_id = SCHEMA_ID(N'dbo')");
        cmd.Parameters.AddWithValue("@n", tableName);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<bool> ColumnExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection,
            transaction,
            timeoutSeconds,
            "SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c");
        cmd.Parameters.AddWithValue("@t", "dbo." + tableName);
        cmd.Parameters.AddWithValue("@c", columnName);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        int timeoutSeconds,
        string name,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(
            "SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(@n)",
            connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };
        cmd.Parameters.AddWithValue("@n", "dbo." + name);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<string?> ResolveArticuloTableAsync(
        SqlConnection connection,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { "pq_vwarticulos", "STA11" })
        {
            if (await ObjectExistsAsync(connection, timeoutSeconds, candidate, cancellationToken).ConfigureAwait(false)
                && await ColumnExistsAsync(connection, null, timeoutSeconds, candidate, "ID_STA11", cancellationToken)
                    .ConfigureAwait(false)
                && await ColumnExistsAsync(connection, null, timeoutSeconds, candidate, "COD_ARTICU", cancellationToken)
                    .ConfigureAwait(false)
                && await ColumnExistsAsync(connection, null, timeoutSeconds, candidate, "DESCRIPCIO", cancellationToken)
                    .ConfigureAwait(false))
            {
                return "dbo." + candidate;
            }
        }

        return null;
    }

    internal static async Task<bool> ArticuloExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idArticulo,
        CancellationToken cancellationToken)
    {
        var table = await ResolveArticuloTableAsync(connection, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (table is null)
        {
            return true;
        }

        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds, $"SELECT TOP 1 1 FROM {table} WHERE ID_STA11 = @id");
        cmd.Parameters.AddWithValue("@id", idArticulo);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<bool> OperacionActivaExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOperacion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_OPERACIONES
            WHERE ID_OPERACION = @id AND ACTIVA = 1
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id", idOperacion);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<bool> ParStdVigenteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idArticulo,
        int idOperacion,
        string fechaRef,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_ARTICULO_OPERACION_STD
            WHERE ID_ARTICULO = @idArt
              AND ID_OPERACION = @idOp
              AND ACTIVO = 1
              AND (VIGENTE_DESDE IS NULL OR VIGENTE_DESDE <= @fecha)
              AND (VIGENTE_HASTA IS NULL OR VIGENTE_HASTA >= @fecha)
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@idArt", idArticulo);
        cmd.Parameters.AddWithValue("@idOp", idOperacion);
        cmd.Parameters.AddWithValue("@fecha", fechaRef);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<int> LoadOtIncrementoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PARAMETROS_GRAL", cancellationToken)
                .ConfigureAwait(false))
        {
            return 1;
        }

        const string sql = """
            SELECT TOP 1 Valor_Int, Valor_String, Valor_Text
            FROM dbo.PQ_PARAMETROS_GRAL
            WHERE LOWER([Programa]) = LOWER(N'PartesProduccion')
              AND Clave = N'ot_incremento_orden_operacion'
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        if (reader["Valor_Int"] is not DBNull && reader["Valor_Int"] is not null
            && int.TryParse(Convert.ToString(reader["Valor_Int"], CultureInfo.InvariantCulture), out var fromInt)
            && fromInt >= 1)
        {
            return fromInt;
        }

        var raw = Convert.ToString(reader["Valor_String"] ?? reader["Valor_Text"], CultureInfo.InvariantCulture);
        if (int.TryParse(raw, out var fromStr) && fromStr >= 1)
        {
            return fromStr;
        }

        return 1;
    }

    internal static async Task<bool> OtIniciaEnBorradorAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PARAMETROS_GRAL", cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        const string sql = """
            SELECT TOP 1 Valor_Bool, Valor_String, Valor_Text
            FROM dbo.PQ_PARAMETROS_GRAL
            WHERE LOWER([Programa]) = LOWER(N'PartesProduccion')
              AND Clave = N'ot_inicia_borrador'
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (reader["Valor_Bool"] is not DBNull && reader["Valor_Bool"] is not null)
        {
            return Convert.ToBoolean(reader["Valor_Bool"], CultureInfo.InvariantCulture);
        }

        var raw = (Convert.ToString(reader["Valor_String"] ?? reader["Valor_Text"], CultureInfo.InvariantCulture) ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return raw is not ("0" or "false" or "no" or "n");
    }

    internal sealed record OpNro(int IdOperacion, int NroOrden);

    internal static async Task<List<OpNro>> ResolverOperacionesConNroOrdenAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idArticulo,
        IReadOnlyList<int> idOperaciones,
        string fechaRef,
        int incremento,
        CancellationToken cancellationToken)
    {
        var ids = idOperaciones.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var hasStd = await TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", cancellationToken)
            .ConfigureAwait(false);
        var hasNroStd = hasStd
            && await ColumnExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", "NRO_ORDEN", cancellationToken)
                .ConfigureAwait(false);

        var nroPorOp = new Dictionary<int, int>();
        var incluidosAuto = new List<int>();

        if (hasStd)
        {
            var inParams = string.Join(",", ids.Select((_, i) => "@op" + i));
            var nroSelect = hasNroStd ? "NRO_ORDEN" : "CAST(0 AS INT) AS NRO_ORDEN";
            var sql = $"""
                SELECT ID_OPERACION, {nroSelect}
                FROM dbo.PQ_PRD_ARTICULO_OPERACION_STD
                WHERE ID_ARTICULO = @idArt
                  AND ID_OPERACION IN ({inParams})
                  AND ACTIVO = 1
                  AND (VIGENTE_DESDE IS NULL OR VIGENTE_DESDE <= @fecha)
                  AND (VIGENTE_HASTA IS NULL OR VIGENTE_HASTA >= @fecha)
                ORDER BY VIGENTE_DESDE DESC
                """;
            await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
            cmd.Parameters.AddWithValue("@idArt", idArticulo);
            cmd.Parameters.AddWithValue("@fecha", fechaRef);
            for (var i = 0; i < ids.Count; i++)
            {
                cmd.Parameters.AddWithValue("@op" + i, ids[i]);
            }

            var seen = new HashSet<int>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idOp = Convert.ToInt32(reader["ID_OPERACION"], CultureInfo.InvariantCulture);
                if (!seen.Add(idOp))
                {
                    continue;
                }

                var nroStd = hasNroStd ? Convert.ToInt32(reader["NRO_ORDEN"], CultureInfo.InvariantCulture) : 0;
                if (nroStd > 0)
                {
                    nroPorOp[idOp] = nroStd;
                }
                else
                {
                    incluidosAuto.Add(idOp);
                }
            }
        }

        if (incluidosAuto.Count > 0)
        {
            var sorted = incluidosAuto.OrderBy(id => id).ToList();
            var inc = Math.Max(1, incremento);
            for (var i = 0; i < sorted.Count; i++)
            {
                nroPorOp[sorted[i]] = inc * (i + 1);
            }
        }

        var outList = new List<OpNro>();
        foreach (var idOp in ids)
        {
            if (nroPorOp.TryGetValue(idOp, out var nro))
            {
                outList.Add(new OpNro(idOp, nro));
            }
        }

        return outList.OrderBy(x => x.NroOrden).ToList();
    }

    internal static async Task<bool> OrdenTieneAsignacionesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrden,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS", cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
            WHERE ID_ORDEN_TRABAJO = @id
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id", idOrden);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<bool> OrdenTienePartesEntradasAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrden,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_ENTRADAS", cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_PARTES_ENTRADAS
            WHERE ID_ORDEN_TRABAJO = @id
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id", idOrden);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    internal static async Task<string?> OrdenNoEliminablePorVinculosAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrden,
        CancellationToken cancellationToken)
    {
        if (await OrdenTieneAsignacionesAsync(connection, transaction, timeoutSeconds, idOrden, cancellationToken)
                .ConfigureAwait(false))
        {
            return "No se puede quitar la operación: existe al menos un ítem de asignación vinculado a esta fila OT.";
        }

        if (await OrdenTienePartesEntradasAsync(connection, transaction, timeoutSeconds, idOrden, cancellationToken)
                .ConfigureAwait(false))
        {
            return "No se puede quitar la operación: existen entradas de partes vinculadas a esta fila OT.";
        }

        return null;
    }

    /// <summary>Reglas espejo PHP OrdenTrabajoController::validateEstadoTransition / OrdenesTrabajoService.</summary>
    internal static async Task<string?> ValidateEstadoTransitionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOrden,
        int actual,
        int nuevoEstado,
        CancellationToken cancellationToken)
    {
        if (actual is not (0 or 1 or 2))
        {
            return "La orden tiene un estado no soportado; revise los datos antes de continuar.";
        }

        if (nuevoEstado == actual)
        {
            return null;
        }

        if (nuevoEstado is not (0 or 1 or 2))
        {
            return "El estado debe ser 0 (Borrador), 1 (Abierta) o 2 (Cerrada).";
        }

        if (actual == 2 && nuevoEstado == 0)
        {
            return "No se puede pasar de Cerrada a Borrador.";
        }

        if (nuevoEstado == 0)
        {
            var otInicia = await OtIniciaEnBorradorAsync(
                    connection, transaction, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (!otInicia)
            {
                return "El estado Borrador no está habilitado en los parámetros del módulo.";
            }

            if (actual != 1)
            {
                return "Solo se puede pasar a Borrador desde Abierta.";
            }

            if (await OrdenTieneAsignacionesAsync(
                    connection, transaction, timeoutSeconds, idOrden, cancellationToken)
                .ConfigureAwait(false))
            {
                return "No se puede pasar a Borrador: la OT está referenciada en asignaciones.";
            }
        }

        if (actual == 0 && nuevoEstado is 1 or 2)
        {
            return null;
        }

        if (actual == 1 && nuevoEstado is 0 or 2)
        {
            return null;
        }

        if (actual == 2 && nuevoEstado == 1)
        {
            return null;
        }

        return "Transición de estado no permitida.";
    }

    internal static List<int> ParseIdsJson(IReadOnlyDictionary<string, object?> parameters, string key = "ids_json")
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is JsonElement jeDirect && jeDirect.ValueKind == JsonValueKind.Array)
        {
            return ParseIntArray(jeDirect);
        }

        var json = ExtractString(parameters, key);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException("ids_json debe ser un array JSON de enteros.");
        }

        return ParseIntArray(doc.RootElement);
    }

    private static List<int> ParseIntArray(JsonElement array)
    {
        var list = new List<int>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && n > 0)
            {
                list.Add(n);
            }
            else if (el.ValueKind == JsonValueKind.String
                     && int.TryParse(el.GetString(), out var p)
                     && p > 0)
            {
                list.Add(p);
            }
        }

        return list.Distinct().ToList();
    }

    internal static async Task<List<Dictionary<string, object?>>> LoadItemsByIdsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        IReadOnlyList<int> ids,
        bool hasNroOrden,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var artTable = await ResolveArticuloTableAsync(connection, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasOps = await TableExistsAsync(connection, null, timeoutSeconds, "PQ_PRD_OPERACIONES", cancellationToken)
            .ConfigureAwait(false);

        var artJoin = artTable is null
            ? string.Empty
            : $"LEFT JOIN {artTable} art ON art.ID_STA11 = r.ID_ARTICULO";
        var artSelect = artTable is null
            ? """
              CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
              CAST(NULL AS NVARCHAR(250)) AS articulo_label
              """
            : """
              LTRIM(RTRIM(CAST(art.COD_ARTICU AS NVARCHAR(50)))) AS articulo_codigo,
              CASE
                  WHEN art.COD_ARTICU IS NULL THEN CAST(NULL AS NVARCHAR(250))
                  ELSE LTRIM(RTRIM(CAST(art.COD_ARTICU AS NVARCHAR(50)))) + N' – ' + LTRIM(RTRIM(CAST(ISNULL(art.DESCRIPCIO, N'') AS NVARCHAR(200))))
              END AS articulo_label
              """;

        var opJoin = hasOps ? "LEFT JOIN dbo.PQ_PRD_OPERACIONES op ON op.ID_OPERACION = r.ID_OPERACION" : string.Empty;
        var opSelect = hasOps
            ? """
              CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
              LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) AS operacion_codigo,
              LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100)))) AS operacion_nombre,
              CASE
                  WHEN op.CODIGO_OPERACION IS NOT NULL AND op.NOMBRE IS NOT NULL
                      THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) + N' – ' + LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
                  WHEN op.NOMBRE IS NOT NULL THEN LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
                  WHEN op.CODIGO_OPERACION IS NOT NULL THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50))))
                  ELSE CAST(NULL AS NVARCHAR(160))
              END AS operacion_label
              """
            : """
              CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
              CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
              CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
              CAST(NULL AS NVARCHAR(160)) AS operacion_label
              """;

        var nroSelect = hasNroOrden
            ? "CAST(ISNULL(r.NRO_ORDEN, 0) AS INT) AS nro_orden"
            : "CAST(NULL AS INT) AS nro_orden";

        var inParams = string.Join(",", ids.Select((_, i) => "@id" + i));
        var sql = $"""
            SELECT
                CAST(r.ID_ORDEN_TRABAJO AS INT) AS id,
                LTRIM(RTRIM(CAST(r.CODIGO_OT AS NVARCHAR(50)))) AS codigo,
                LTRIM(RTRIM(CAST(r.TIPO_REF_EXTERNA AS NVARCHAR(50)))) AS tipo_ref_externa,
                r.ID_REF_EXTERNA AS id_ref_externa,
                LTRIM(RTRIM(CAST(r.DESCRIPCION AS NVARCHAR(200)))) AS descripcion,
                CAST(r.ID_ARTICULO AS INT) AS id_articulo,
                {artSelect},
                {opSelect},
                CAST(r.CANTIDAD_A_PRODUCIR AS INT) AS cantidad_a_producir,
                r.FECHA_INICIO_PLAN AS fecha_inicio_plan,
                r.FECHA_FIN_PLAN AS fecha_fin_plan,
                CAST(r.ESTADO AS INT) AS estado,
                CASE CAST(r.ESTADO AS INT)
                    WHEN 0 THEN N'Borrador'
                    WHEN 1 THEN N'Abierta'
                    WHEN 2 THEN N'Cerrada'
                    ELSE N'Desconocido'
                END AS estado_label,
                r.OBSERVACIONES AS observaciones,
                r.FECHA_ALTA AS fecha_alta,
                {nroSelect}
            FROM dbo.PQ_PRD_ORDENES_TRABAJO r
            {artJoin}
            {opJoin}
            WHERE r.ID_ORDEN_TRABAJO IN ({inParams})
            ORDER BY r.ID_ORDEN_TRABAJO
            """;

        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        for (var i = 0; i < ids.Count; i++)
        {
            cmd.Parameters.AddWithValue("@id" + i, ids[i]);
        }

        var items = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var estado = reader["estado"] is DBNull ? 0 : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
            var item = new Dictionary<string, object?>
            {
                ["id"] = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                ["codigo"] = reader["codigo"] is DBNull ? null : reader["codigo"]?.ToString(),
                ["tipo_ref_externa"] = reader["tipo_ref_externa"] is DBNull ? null : reader["tipo_ref_externa"]?.ToString(),
                ["id_ref_externa"] = reader["id_ref_externa"] is DBNull ? null : reader["id_ref_externa"],
                ["descripcion"] = reader["descripcion"] is DBNull ? null : reader["descripcion"]?.ToString(),
                ["id_articulo"] = reader["id_articulo"] is DBNull ? null : Convert.ToInt32(reader["id_articulo"], CultureInfo.InvariantCulture),
                ["articulo_codigo"] = reader["articulo_codigo"] is DBNull ? null : reader["articulo_codigo"]?.ToString(),
                ["articulo_label"] = reader["articulo_label"] is DBNull ? null : reader["articulo_label"]?.ToString(),
                ["id_operacion"] = reader["id_operacion"] is DBNull ? null : Convert.ToInt32(reader["id_operacion"], CultureInfo.InvariantCulture),
                ["operacion_codigo"] = reader["operacion_codigo"] is DBNull ? null : reader["operacion_codigo"]?.ToString(),
                ["operacion_nombre"] = reader["operacion_nombre"] is DBNull ? null : reader["operacion_nombre"]?.ToString(),
                ["operacion_label"] = reader["operacion_label"] is DBNull ? null : reader["operacion_label"]?.ToString(),
                ["cantidad_a_producir"] = reader["cantidad_a_producir"] is DBNull
                    ? 0
                    : Convert.ToInt32(reader["cantidad_a_producir"], CultureInfo.InvariantCulture),
                ["fecha_inicio_plan"] = FormatDateOnly(reader["fecha_inicio_plan"]),
                ["fecha_fin_plan"] = FormatDateOnly(reader["fecha_fin_plan"]),
                ["estado"] = estado,
                ["estado_label"] = reader["estado_label"]?.ToString() ?? "Desconocido",
                ["observaciones"] = reader["observaciones"] is DBNull ? null : reader["observaciones"]?.ToString(),
                ["fecha_alta"] = FormatDateTime(reader["fecha_alta"])
            };
            if (hasNroOrden && reader["nro_orden"] is not DBNull)
            {
                item["nro_orden"] = Convert.ToInt32(reader["nro_orden"], CultureInfo.InvariantCulture);
            }

            items.Add(item);
        }

        return items;
    }

    internal static List<int> ResolveOperaciones(
        IReadOnlyDictionary<string, object?> parameters,
        bool? modoIndividual)
    {
        var individual = modoIndividual
            ?? (parameters.ContainsKey("id_operacion")
                && !parameters.ContainsKey("operaciones_incluidas_json")
                && !parameters.ContainsKey("operaciones_incluidas"));

        if (individual)
        {
            var idOp = ExtractInt(parameters, "id_operacion");
            if (idOp is null or <= 0)
            {
                throw new ValidationException("id_operacion es obligatorio en modo individual.");
            }

            return [idOp.Value];
        }

        if (parameters.TryGetValue("operaciones_incluidas_json", out var jsonRaw) && jsonRaw is not null)
        {
            var json = ExtractString(parameters, "operaciones_incluidas_json");
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ValidationException("Indique al menos una operación a incluir.");
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("operaciones_incluidas_json debe ser un array JSON.");
            }

            var list = new List<int>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && n > 0)
                {
                    list.Add(n);
                }
                else if (el.ValueKind == JsonValueKind.String
                         && int.TryParse(el.GetString(), out var p)
                         && p > 0)
                {
                    list.Add(p);
                }
            }

            return list.Distinct().ToList();
        }

        throw new ValidationException("Indique al menos una operación a incluir.");
    }

    internal static SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string sql)
    {
        var cmd = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = Math.Max(1, timeoutSeconds);
        return cmd;
    }

    internal static string? FormatDateOnly(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    internal static string? FormatDateTime(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    internal static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }

    internal static string? Truncate(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }

    internal static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je => je.ToString(),
            _ => raw.ToString()
        };
    }

    internal static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            int i => i,
            long l => (int)l,
            decimal d => (int)d,
            string s when int.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) => i,
            JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var p) => p,
            _ => null
        };
    }

    internal static bool? ExtractBool(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s when bool.TryParse(s, out var p) => p,
            string s when s is "1" or "true" or "True" => true,
            string s when s is "0" or "false" or "False" => false,
            JsonElement je when je.ValueKind is JsonValueKind.True => true,
            JsonElement je when je.ValueKind is JsonValueKind.False => false,
            _ => null
        };
    }
}
