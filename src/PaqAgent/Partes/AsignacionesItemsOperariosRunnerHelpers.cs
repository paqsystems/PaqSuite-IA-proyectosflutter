using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PaqAgent.Partes;

/// <summary>Helpers compartidos D6.9.8–16 Items + Operarios.</summary>
internal static class AsignacionesItemsOperariosRunnerHelpers
{
    internal static async Task AssertDraftAndItemAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacion,
        int idItem,
        string conflictMessage,
        CancellationToken cancellationToken)
    {
        var (estado, _, _) = await AsignacionesRunnerHelpers.LoadEstadoAsync(
                connection, transaction, timeoutSeconds, idAsignacion, cancellationToken)
            .ConfigureAwait(false);

        if (estado != 0)
        {
            throw new AsignacionesRunnerHelpers.ConflictException(conflictMessage);
        }

        if (!await ItemBelongsAsync(connection, transaction, timeoutSeconds, idAsignacion, idItem, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new AsignacionesRunnerHelpers.NotFoundException("Tarea no encontrada");
        }
    }

    internal static async Task<bool> ItemBelongsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacion,
        int idItem,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
WHERE ID_ASIGNACION = @idAsignacion AND ID_ASIGNACION_ITEM = @idItem;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@idAsignacion", SqlDbType.Int).Value = idAsignacion;
        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static async Task<Dictionary<string, object?>> LoadItemAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        int idAsignacion,
        int idItem,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(i.ID_ASIGNACION_ITEM AS INT) AS id,
    CAST(i.ID_ASIGNACION AS INT) AS id_asignacion,
    CAST(i.ID_ORDEN_TRABAJO AS INT) AS id_orden_trabajo,
    CAST(i.ID_ARTICULO AS INT) AS id_articulo,
    CAST(i.ID_OPERACION AS INT) AS id_operacion,
    CAST(i.ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
    CAST(i.ID_MAQUINA AS INT) AS id_maquina,
    CAST(i.UNIDADES_HORA_STD AS FLOAT) AS unidades_hora_std,
    CAST(i.UNIDADES_PLAN AS FLOAT) AS unidades_plan,
    CAST(i.MINUTOS_PLAN AS INT) AS minutos_plan,
    CAST(i.NOTAS_PLAN AS NVARCHAR(2000)) AS notas_plan,
    CAST(i.PRIORIDAD AS INT) AS prioridad
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS i
WHERE i.ID_ASIGNACION = @idAsignacion AND i.ID_ASIGNACION_ITEM = @idItem;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@idAsignacion", SqlDbType.Int).Value = idAsignacion;
        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new AsignacionesRunnerHelpers.NotFoundException("Tarea no encontrada");
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = reader["id"] is DBNull ? 0 : Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
            ["id_asignacion"] = reader["id_asignacion"] is DBNull
                ? null
                : Convert.ToInt32(reader["id_asignacion"], CultureInfo.InvariantCulture),
            ["id_orden_trabajo"] = NullInt(reader["id_orden_trabajo"]),
            ["orden_trabajo_codigo"] = null,
            ["id_articulo"] = NullInt(reader["id_articulo"]),
            ["articulo_codigo"] = null,
            ["id_operacion"] = NullInt(reader["id_operacion"]),
            ["operacion_codigo"] = null,
            ["operacion_nombre"] = null,
            ["id_tipo_tarea"] = NullInt(reader["id_tipo_tarea"]),
            ["tipo_tarea_codigo"] = null,
            ["tipo_tarea_nombre"] = null,
            ["id_maquina"] = NullInt(reader["id_maquina"]),
            ["maquina_codigo"] = null,
            ["maquina_nombre"] = null,
            ["unidades_hora_std"] = NullDouble(reader["unidades_hora_std"]),
            ["unidades_plan"] = NullDouble(reader["unidades_plan"]),
            ["minutos_plan"] = NullInt(reader["minutos_plan"]),
            ["notas_plan"] = reader["notas_plan"] is DBNull ? null : Convert.ToString(reader["notas_plan"], CultureInfo.InvariantCulture),
            ["prioridad"] = NullInt(reader["prioridad"])
        };
    }

    internal static async Task<List<Dictionary<string, object?>>> LoadOperariosForItemAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        int idItem,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS", cancellationToken)
                .ConfigureAwait(false))
        {
            return [];
        }

        var hasLegajos = await AsignacionesRunnerHelpers.TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_SUELD_LEGAJOS", cancellationToken)
            .ConfigureAwait(false);

        var sql = hasLegajos
            ? @"
SELECT
    CAST(o.ID_ASIGITEM_OPERARIO AS INT) AS id,
    CAST(o.ID_OPERARIO AS INT) AS id_operario,
    CAST(l.NRO_LEGAJO AS NVARCHAR(50)) AS nro_legajo,
    LTRIM(RTRIM(ISNULL(l.APELLIDO, N'') + N', ' + ISNULL(l.NOMBRE, N''))) AS nombre_completo,
    CAST(o.ROL_PLAN AS NVARCHAR(50)) AS rol_plan
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS o
LEFT JOIN dbo.PQ_SUELD_LEGAJOS l ON l.ID = o.ID_OPERARIO
WHERE o.ID_ASIGNACION_ITEM = @idItem;"
            : @"
SELECT
    CAST(o.ID_ASIGITEM_OPERARIO AS INT) AS id,
    CAST(o.ID_OPERARIO AS INT) AS id_operario,
    CAST(NULL AS NVARCHAR(50)) AS nro_legajo,
    CAST(o.ID_OPERARIO AS NVARCHAR(50)) AS nombre_completo,
    CAST(o.ROL_PLAN AS NVARCHAR(50)) AS rol_plan
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS o
WHERE o.ID_ASIGNACION_ITEM = @idItem;";

        var items = new List<Dictionary<string, object?>>();
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@idItem", SqlDbType.Int).Value = idItem;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var nombre = reader["nombre_completo"] is DBNull
                ? Convert.ToString(reader["id_operario"], CultureInfo.InvariantCulture)
                : Convert.ToString(reader["nombre_completo"], CultureInfo.InvariantCulture)?.Trim().Trim(',');
            items.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                ["id_operario"] = Convert.ToInt32(reader["id_operario"], CultureInfo.InvariantCulture),
                ["nro_legajo"] = reader["nro_legajo"] is DBNull ? null : Convert.ToString(reader["nro_legajo"], CultureInfo.InvariantCulture),
                ["nombre_completo"] = string.IsNullOrWhiteSpace(nombre)
                    ? Convert.ToString(reader["id_operario"], CultureInfo.InvariantCulture)
                    : nombre,
                ["rol_plan"] = reader["rol_plan"] is DBNull ? null : Convert.ToString(reader["rol_plan"], CultureInfo.InvariantCulture)
            });
        }

        return items;
    }

    internal static List<int> ExtractIntList(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is IEnumerable<int> ints)
        {
            return ints.Where(x => x > 0).Distinct().ToList();
        }

        if (raw is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                var list = new List<int>();
                foreach (var el in je.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) && i > 0)
                    {
                        list.Add(i);
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

            if (je.ValueKind == JsonValueKind.String)
            {
                return ParseCsvInts(je.GetString());
            }
        }

        if (raw is string s)
        {
            return ParseCsvInts(s);
        }

        if (raw is IEnumerable<object> objs)
        {
            var list = new List<int>();
            foreach (var o in objs)
            {
                if (o is int i && i > 0)
                {
                    list.Add(i);
                }
                else if (o is long l && l > 0)
                {
                    list.Add((int)l);
                }
                else if (int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out var p) && p > 0)
                {
                    list.Add(p);
                }
            }

            return list.Distinct().ToList();
        }

        return [];
    }

    private static List<int> ParseCsvInts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var i) ? i : 0)
            .Where(x => x > 0)
            .Distinct()
            .ToList();
    }

    private static int? NullInt(object value) =>
        value is DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static double? NullDouble(object value) =>
        value is DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
}
