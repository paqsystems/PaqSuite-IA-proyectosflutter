using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace PaqAgent.Partes;

/// <summary>Helpers compartidos D6.10 PartesOperario (Create/Update/Enviar + transiciones supervisor).</summary>
internal static class PartesOperarioRunnerHelpers
{
    internal sealed class ValidationException(string message) : Exception(message);

    internal sealed class ConflictException(string message) : Exception(message);

    internal sealed class NotFoundException(string message) : Exception(message);

    internal sealed class NoLegajoException(string message) : Exception(message);

    internal const int EstadoOpen = 0;

    internal const int EstadoSubmitted = 1;

    internal const int EstadoReviewed = 2;

    internal const int EstadoLocked = 3;

    internal const string DuplicateMessage = "Ya existe un parte para esa fecha y turno.";

    internal const string ConflictEditMessage = "Solo se puede editar un parte en estado Abierto.";

    internal const string ConflictEnviarMessage = "Solo se puede enviar un parte en estado Abierto.";

    internal const string ConflictAprobarMessage = "Solo se puede aprobar un parte en estado Enviado.";

    internal const string ConflictDevolverMessage = "Solo se puede devolver un parte en estado Enviado.";

    internal const string ConflictCerrarMessage = "Solo se puede cerrar un parte en estado Revisado.";

    internal const string ConflictCircuitoEnviarMessage =
        "El circuito de autorización está desactivado. Los partes permanecen en estado Abierto.";

    internal const string ConflictCircuitoSupervisorMessage = "El circuito de autorización está desactivado.";

    internal const string EntradasRequiredMessage =
        "FIELD:entradas|Agregue al menos una entrada de tiempo.|El parte debe tener al menos una entrada para enviar.";

    internal const string EntradasInvalidMessage =
        "FIELD:entradas|Revise que cada entrada tenga concepto y duración.|Todas las entradas deben tener concepto y minutos válidos.";

    internal const string EntradasLibreMessage =
        "FIELD:entradas|Revise las entradas marcadas como carga libre (OT obligatoria).|Las líneas de carga libre requieren orden de trabajo.";

    internal const string NoLegajoMessage = "Usuario sin legajo";

    internal const string NoLegajoCreateMessage = "Usuario sin legajo asociado. No puede crear partes.";

    internal const string NotFoundMessage = "Parte no encontrado";

    internal const string TableMissingMessage = "Tabla no disponible";

    internal static async Task<int?> ResolveIdOperarioAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_SUELD_LEGAJOS", cancellationToken)
                .ConfigureAwait(false)
            || !await AsignacionesRunnerHelpers.ColumnExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_SUELD_LEGAJOS", "ID_USUARIO", cancellationToken)
                .ConfigureAwait(false)
            || !await AsignacionesRunnerHelpers.ColumnExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_SUELD_LEGAJOS", "ID", cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        const string sql = "SELECT TOP 1 CAST(ID AS INT) FROM dbo.PQ_SUELD_LEGAJOS WHERE ID_USUARIO = @usuarioId;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@usuarioId", SqlDbType.Int).Value = usuarioId;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (scalar is null or DBNull)
        {
            return null;
        }

        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    internal static async Task<bool> DuplicateExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string fechaParte,
        int? idTurno,
        int idOperario,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_PARTES_OPERARIO
WHERE FECHA_PARTE = CONVERT(date, @fecha, 23)
  AND ID_OPERARIO = @idOperario
  AND ((@idTurno IS NULL AND ID_TURNO IS NULL) OR ID_TURNO = @idTurno);";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@fecha", SqlDbType.NVarChar, 10).Value = fechaParte;
        cmd.Parameters.Add("@idTurno", SqlDbType.Int).Value = idTurno.HasValue ? idTurno.Value : DBNull.Value;
        cmd.Parameters.Add("@idOperario", SqlDbType.Int).Value = idOperario;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }

    internal static bool IsUniqueViolation(SqlException exception) =>
        exception.Number is 2601 or 2627;

    internal static async Task<bool> CircuitoAutorizacionActivoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PARAMETROS_GRAL", cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        const string sql = @"
SELECT TOP 1 Valor_Bool, Valor_Int, Valor_String, Valor_Text
FROM dbo.PQ_PARAMETROS_GRAL
WHERE LOWER([Programa]) = LOWER(N'PartesProduccion')
  AND Clave = N'circuito_autorizacion_activo';";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (reader["Valor_Bool"] is not DBNull && reader["Valor_Bool"] is not null)
        {
            return Convert.ToBoolean(reader["Valor_Bool"], CultureInfo.InvariantCulture);
        }

        if (reader["Valor_Int"] is not DBNull && reader["Valor_Int"] is not null)
        {
            return Convert.ToInt32(reader["Valor_Int"], CultureInfo.InvariantCulture) != 0;
        }

        var raw = (Convert.ToString(reader["Valor_String"] ?? reader["Valor_Text"], CultureInfo.InvariantCulture)
                ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return raw is "1" or "true" or "si" or "sí" or "yes";
    }

    internal static async Task<(int Estado, int IdOperario)> LoadEstadoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ESTADO AS INT) AS estado,
    CAST(ID_OPERARIO AS INT) AS id_operario
FROM dbo.PQ_PRD_PARTES_OPERARIO
WHERE ID_PARTE_OPERARIO = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        var estado = reader["estado"] is DBNull
            ? 0
            : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
        var idOperario = reader["id_operario"] is DBNull
            ? 0
            : Convert.ToInt32(reader["id_operario"], CultureInfo.InvariantCulture);

        return (estado, idOperario);
    }

    internal static async Task<Dictionary<string, object?>> LoadIdEstadoPayloadAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1
    CAST(ID_PARTE_OPERARIO AS INT) AS id,
    CAST(ESTADO AS INT) AS estado
FROM dbo.PQ_PRD_PARTES_OPERARIO
WHERE ID_PARTE_OPERARIO = @id;";

        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = reader["id"] is DBNull ? 0 : Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
            ["estado"] = reader["estado"] is DBNull
                ? 0
                : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture)
        };
    }

    internal static async Task EnsureEntradasValidasParaEnviarAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_ENTRADAS", cancellationToken)
                .ConfigureAwait(false))
        {
            throw new ValidationException(EntradasRequiredMessage);
        }

        const string existsSql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_PARTES_ENTRADAS
WHERE ID_PARTE_OPERARIO = @id;";
        await using (var existsCmd = new SqlCommand(existsSql, connection, transaction)
        {
            CommandTimeout = timeoutSeconds
        })
        {
            existsCmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
            var existsScalar = await existsCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (existsScalar is null or DBNull)
            {
                throw new ValidationException(EntradasRequiredMessage);
            }
        }

        const string invalidSql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_PARTES_ENTRADAS
WHERE ID_PARTE_OPERARIO = @id
  AND (ID_CONCEPTO_TIEMPO IS NULL OR MINUTOS IS NULL OR MINUTOS < 1);";
        await using (var invalidCmd = new SqlCommand(invalidSql, connection, transaction)
        {
            CommandTimeout = timeoutSeconds
        })
        {
            invalidCmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
            var invalidScalar = await invalidCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (invalidScalar is not null && invalidScalar is not DBNull)
            {
                throw new ValidationException(EntradasInvalidMessage);
            }
        }

        if (!await AsignacionesRunnerHelpers.ColumnExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_ENTRADAS", "ORIGEN_CARGA", cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        const string libreSql = @"
SELECT TOP 1 1
FROM dbo.PQ_PRD_PARTES_ENTRADAS
WHERE ID_PARTE_OPERARIO = @id
  AND ORIGEN_CARGA = N'libre'
  AND (ID_ORDEN_TRABAJO IS NULL OR ID_ORDEN_TRABAJO < 1);";
        await using var libreCmd = new SqlCommand(libreSql, connection, transaction)
        {
            CommandTimeout = timeoutSeconds
        };
        libreCmd.Parameters.Add("@id", SqlDbType.Int).Value = idParteOperario;
        var libreScalar = await libreCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (libreScalar is not null && libreScalar is not DBNull)
        {
            throw new ValidationException(EntradasLibreMessage);
        }
    }

    internal static string NowSql() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
