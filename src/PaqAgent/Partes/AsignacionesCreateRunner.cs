using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.9.3 — alta Asignaciones Draft orquestada en agente (espejo PHP AsignacionController::store / AsignacionesService::storeLocal).
/// Sin SP monolítico.
/// </summary>
public sealed class AsignacionesCreateRunner
{
    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var fechaAsignacion = NormalizeDate(ExtractString(parameters, "fecha_asignacion"));
        var idTipoTarea = ExtractInt(parameters, "id_tipo_tarea");

        if (string.IsNullOrWhiteSpace(database) || fechaAsignacion is null || idTipoTarea is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "fecha_asignacion, id_tipo_tarea y _database son obligatorios.");
        }

        if (!TryParseDateOnly(fechaAsignacion, out var fechaOnly))
        {
            return Fail("VALIDATION", "La fecha de asignación debe tener formato AAAA-MM-DD.");
        }

        if (fechaOnly < DateOnly.FromDateTime(DateTime.Today))
        {
            return Fail("VALIDATION", "La fecha de asignación no puede ser anterior al día actual.");
        }

        var idTurno = ExtractInt(parameters, "id_turno");
        if (idTurno is <= 0)
        {
            idTurno = null;
        }

        var observaciones = ExtractString(parameters, "observaciones")?.Trim();
        if (observaciones is { Length: > 2000 })
        {
            return Fail("VALIDATION", "observaciones no puede superar 2000 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(observaciones))
        {
            observaciones = null;
        }

        var usuarioId = ExtractInt(parameters, "usuario_id") ?? 0;

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
                if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new ValidationException(
                        "No existe la tabla PQ_PRD_ASIGNACIONES en la base de datos de la empresa.");
                }

                if (!await ColumnExistsAsync(
                        connection, transaction, timeoutSeconds, "PQ_PRD_ASIGNACIONES", "ID_TIPO_TAREA", cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new ValidationException(
                        "Falta la columna ID_TIPO_TAREA en PQ_PRD_ASIGNACIONES.");
                }

                if (await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_TIPOS_TAREA", cancellationToken)
                        .ConfigureAwait(false)
                    && !await TipoTareaExistsAsync(
                        connection, transaction, timeoutSeconds, idTipoTarea.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new ValidationException($"El tipo de tarea {idTipoTarea.Value} no existe.");
                }

                if (idTurno is not null
                    && await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_TURNOS", cancellationToken)
                        .ConfigureAwait(false)
                    && !await TurnoExistsAsync(
                        connection, transaction, timeoutSeconds, idTurno.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new ValidationException($"El turno {idTurno.Value} no existe.");
                }

                var newId = await InsertAsignacionAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        fechaAsignacion,
                        idTurno,
                        idTipoTarea.Value,
                        usuarioId,
                        observaciones,
                        cancellationToken)
                    .ConfigureAwait(false);

                var payload = await LoadCabeceraAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        newId,
                        includeObservaciones: false,
                        cancellationToken)
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
        catch (ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<int> InsertAsignacionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string fechaAsignacion,
        int? idTurno,
        int idTipoTarea,
        int usuarioId,
        string? observaciones,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO dbo.PQ_PRD_ASIGNACIONES
    (FECHA_ASIGNACION, ID_TURNO, ID_TIPO_TAREA, ID_USUARIO_SUPERVISOR, OBSERVACIONES, ESTADO, FECHA_ALTA, USUARIO_ALTA)
OUTPUT INSERTED.ID_ASIGNACION AS id
VALUES
    (CONVERT(date, @fecha, 23), @idTurno, @idTipoTarea, @usuarioId, @obs, 0, CONVERT(datetime, @fechaAlta, 120), @usuarioId);";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 10).Value = fechaAsignacion;
        cmd.Parameters.Add("@idTurno", SqlDbType.Int).Value = idTurno.HasValue ? idTurno.Value : DBNull.Value;
        cmd.Parameters.Add("@idTipoTarea", SqlDbType.Int).Value = idTipoTarea;
        cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@obs", SqlDbType.NVarChar, 2000).Value =
            observaciones is null ? DBNull.Value : observaciones;
        cmd.Parameters.Add("@fechaAlta", SqlDbType.NVarChar, 19).Value =
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    internal static async Task<Dictionary<string, object?>> LoadCabeceraAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        int idAsignacion,
        bool includeObservaciones,
        CancellationToken cancellationToken)
    {
        var hasTurnos = await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_TURNOS", cancellationToken)
            .ConfigureAwait(false);
        var hasTipos = await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_TIPOS_TAREA", cancellationToken)
            .ConfigureAwait(false);

        var turnoSelect = hasTurnos
            ? "LTRIM(RTRIM(CAST(t.CODIGO_TURNO AS NVARCHAR(50)))) AS turno_codigo"
            : "CAST(NULL AS NVARCHAR(50)) AS turno_codigo";
        var tipoSelect = hasTipos
            ? @"LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))) AS tipo_tarea_codigo,
               LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100)))) AS tipo_tarea_nombre"
            : @"CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo,
               CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre";
        var turnoJoin = hasTurnos ? "LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = a.ID_TURNO" : string.Empty;
        var tipoJoin = hasTipos ? "LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA tt ON tt.ID_TIPO_TAREA = a.ID_TIPO_TAREA" : string.Empty;
        var obsSelect = includeObservaciones
            ? ", CAST(a.OBSERVACIONES AS NVARCHAR(2000)) AS observaciones"
            : string.Empty;

        var sql = $@"
SELECT TOP 1
    CAST(a.ID_ASIGNACION AS INT) AS id,
    CAST(a.FECHA_ASIGNACION AS DATE) AS fecha_asignacion,
    CAST(a.ID_TURNO AS INT) AS id_turno,
    {turnoSelect},
    CAST(a.ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
    {tipoSelect},
    CAST(a.ID_USUARIO_SUPERVISOR AS INT) AS supervisor_id,
    CAST(NULL AS NVARCHAR(50)) AS supervisor_codigo,
    CAST(NULL AS NVARCHAR(100)) AS supervisor_nombre,
    CAST(NULL AS NVARCHAR(160)) AS supervisor_label,
    CAST(a.ESTADO AS INT) AS estado,
    CASE CAST(a.ESTADO AS INT)
        WHEN 0 THEN N'Borrador'
        WHEN 1 THEN N'Publicada'
        WHEN 2 THEN N'Cerrada'
        WHEN 3 THEN N'Anulada'
        ELSE N'Desconocido'
    END AS estado_label,
    CAST(a.FECHA_PUBLICACION AS DATETIME) AS fecha_publicacion,
    CAST(a.FECHA_CIERRE AS DATETIME) AS fecha_cierre
    {obsSelect}
FROM dbo.PQ_PRD_ASIGNACIONES a
{turnoJoin}
{tipoJoin}
WHERE a.ID_ASIGNACION = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idAsignacion;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ValidationException("Asignación no encontrada tras el alta.");
        }

        return MapCabeceraFromReader(reader, includeObservaciones);
    }

    internal static Dictionary<string, object?> MapCabeceraFromReader(SqlDataReader reader, bool includeObservaciones)
    {
        var estado = reader["estado"] is DBNull ? 0 : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
        var payload = new Dictionary<string, object?>
        {
            ["id"] = reader["id"] is DBNull ? 0 : Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
            ["fecha_asignacion"] = FormatDateOnly(reader["fecha_asignacion"]),
            ["id_turno"] = reader["id_turno"] is DBNull ? null : Convert.ToInt32(reader["id_turno"], CultureInfo.InvariantCulture),
            ["turno_codigo"] = reader["turno_codigo"] is DBNull ? null : Convert.ToString(reader["turno_codigo"], CultureInfo.InvariantCulture),
            ["id_tipo_tarea"] = reader["id_tipo_tarea"] is DBNull ? null : Convert.ToInt32(reader["id_tipo_tarea"], CultureInfo.InvariantCulture),
            ["tipo_tarea_codigo"] = reader["tipo_tarea_codigo"] is DBNull ? null : Convert.ToString(reader["tipo_tarea_codigo"], CultureInfo.InvariantCulture),
            ["tipo_tarea_nombre"] = reader["tipo_tarea_nombre"] is DBNull ? null : Convert.ToString(reader["tipo_tarea_nombre"], CultureInfo.InvariantCulture),
            ["supervisor_id"] = reader["supervisor_id"] is DBNull ? null : Convert.ToInt32(reader["supervisor_id"], CultureInfo.InvariantCulture),
            ["supervisor_codigo"] = null,
            ["supervisor_nombre"] = null,
            ["supervisor_label"] = null,
            ["estado"] = estado,
            ["estado_label"] = reader["estado_label"] is DBNull
                ? estado switch
                {
                    0 => "Borrador",
                    1 => "Publicada",
                    2 => "Cerrada",
                    3 => "Anulada",
                    _ => "Desconocido"
                }
                : Convert.ToString(reader["estado_label"], CultureInfo.InvariantCulture),
            ["fecha_publicacion"] = FormatDateTime(reader["fecha_publicacion"]),
            ["fecha_cierre"] = FormatDateTime(reader["fecha_cierre"])
        };

        if (includeObservaciones)
        {
            payload["observaciones"] = reader["observaciones"] is DBNull
                ? null
                : Convert.ToString(reader["observaciones"], CultureInfo.InvariantCulture);
        }

        return payload;
    }

    private static async Task<bool> TipoTareaExistsAsync(
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

    private static async Task<bool> TurnoExistsAsync(
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

    private static async Task<bool> TableExistsAsync(
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

    private static async Task<bool> ColumnExistsAsync(
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

    private static string? NormalizeDate(string? raw)
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

    private static bool TryParseDateOnly(string value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static string? FormatDateOnly(object? value)
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

    private static string? FormatDateTime(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, string key)
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

    private static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, string key)
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

    private sealed class ValidationException(string message) : Exception(message);
}
