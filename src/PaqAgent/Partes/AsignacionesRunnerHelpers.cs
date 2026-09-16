using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PaqAgent.Partes;

/// <summary>Helpers compartidos D6.9 Asignaciones (Update/Publicar/Cerrar/Cancelar + reuso Create).</summary>
internal static class AsignacionesRunnerHelpers
{
    internal sealed class ValidationException(string message) : Exception(message);

    internal sealed class ConflictException(string message) : Exception(message);

    internal sealed class NotFoundException(string message) : Exception(message);

    internal static async Task<Dictionary<string, object?>> LoadCabeceraAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        int idAsignacion,
        bool includeObservaciones,
        CancellationToken cancellationToken)
    {
        return await AsignacionesCreateRunner.LoadCabeceraAsync(
                connection, transaction, timeoutSeconds, idAsignacion, includeObservaciones, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<(int Estado, int? IdTipoTarea, string? FechaAsignacion)> LoadEstadoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacion,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ESTADO AS INT) AS estado,
    CAST(ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
    CAST(FECHA_ASIGNACION AS DATE) AS fecha_asignacion
FROM dbo.PQ_PRD_ASIGNACIONES
WHERE ID_ASIGNACION = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException("Asignación no encontrada");
        }

        var estado = reader["estado"] is DBNull ? 0 : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
        int? idTipo = reader["id_tipo_tarea"] is DBNull
            ? null
            : Convert.ToInt32(reader["id_tipo_tarea"], CultureInfo.InvariantCulture);
        var fecha = FormatDateOnly(reader["fecha_asignacion"]);

        return (estado, idTipo, fecha);
    }

    internal static async Task<bool> TableExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT OBJECT_ID(@name, N'U');";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 256).Value = "dbo." + tableName;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static async Task<bool> ColumnExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT COL_LENGTH(@table, @column);";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@table", SqlDbType.NVarChar, 256).Value = "dbo." + tableName;
        cmd.Parameters.Add("@column", SqlDbType.NVarChar, 128).Value = columnName;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static async Task<bool> TipoTareaExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idTipoTarea,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP 1 1 FROM dbo.PQ_PRD_TIPOS_TAREA WHERE ID_TIPO_TAREA = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idTipoTarea;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static async Task<bool> TurnoExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idTurno,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP 1 1 FROM dbo.PQ_PRD_TURNOS WHERE ID_TURNO = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idTurno;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static async Task<bool> HasItemsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsignacion,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP 1 1 FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS WHERE ID_ASIGNACION = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static string? NormalizeDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateOnly.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            return DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw.Trim();
    }

    internal static bool TryParseDateOnly(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

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

        if (value is DateOnly d)
        {
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
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
}
