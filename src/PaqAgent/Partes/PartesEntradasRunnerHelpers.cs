using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>Helpers D6.11 PartesOperario.Entradas (List SP + Create/Update/Delete/Reclasificar).</summary>
internal static class PartesEntradasRunnerHelpers
{
    internal const string NotFoundEditableMessage = "Parte no encontrado o no editable";

    internal const string EntradaNotFoundMessage = "Entrada no encontrada";

    internal const string ReclasificarNotFoundMessage = "Parte no encontrado o no en estado Revisado";

    internal const string TableMissingMessage = "Tabla no disponible";

    internal const string ConceptosMissingMessage = "Catálogo conceptos tiempo no disponible";

    internal const string ConflictCircuitoMessage = "El circuito de autorización está desactivado.";

    internal static async Task<int> AssertParteOpenPropietarioAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idParteOperario,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        if (!await AsignacionesRunnerHelpers.TableExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_PARTES_OPERARIO", cancellationToken)
                .ConfigureAwait(false))
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(NotFoundEditableMessage);
        }

        var idOperario = await PartesOperarioRunnerHelpers.ResolveIdOperarioAsync(
                connection, transaction, timeoutSeconds, usuarioId, cancellationToken)
            .ConfigureAwait(false);
        if (idOperario is null)
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(NotFoundEditableMessage);
        }

        int estado;
        int idOperarioParte;
        try
        {
            (estado, idOperarioParte) = await PartesOperarioRunnerHelpers.LoadEstadoAsync(
                    connection, transaction, timeoutSeconds, idParteOperario, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PartesOperarioRunnerHelpers.NotFoundException)
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(NotFoundEditableMessage);
        }
        if (idOperarioParte != idOperario.Value || estado != PartesOperarioRunnerHelpers.EstadoOpen)
        {
            throw new PartesOperarioRunnerHelpers.NotFoundException(NotFoundEditableMessage);
        }

        return idOperario.Value;
    }

    [DoesNotReturn]
    internal static void ThrowField(string field, string message, string? respuesta = null)
    {
        var suffix = string.IsNullOrWhiteSpace(respuesta) ? string.Empty : "|" + respuesta;
        throw new PartesOperarioRunnerHelpers.ValidationException("FIELD:" + field + "|" + message + suffix);
    }

    internal static double? ExtractDouble(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number => je.GetDouble(),
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                && double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null
        };
    }

    internal static DateTime? ParseDateTime(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw is DateTime dt)
        {
            return dt;
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : null;
    }

    internal static object DbValue(object? value) => value ?? DBNull.Value;

    internal static PartesOutcome MapMutationException(Exception ex)
    {
        return ex switch
        {
            PartesOperarioRunnerHelpers.NotFoundException nfex => Fail("NOT_FOUND", nfex.Message),
            PartesOperarioRunnerHelpers.NoLegajoException nex => Fail("NO_LEGAJO", nex.Message),
            PartesOperarioRunnerHelpers.ConflictException cex => Fail("CONFLICT", cex.Message),
            PartesOperarioRunnerHelpers.ValidationException vex => Fail("VALIDATION", vex.Message),
            InvalidOperationException iex => Fail("SQL_ERROR", iex.Message),
            _ => Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message)
        };
    }

    internal static PartesOutcome Ok(object? data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    internal static PartesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };

    internal static PartesOutcome Degraded() =>
        new()
        {
            Status = JobStatuses.Degraded,
            ErrorCode = "SQL_NOT_CONFIGURED",
            ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
        };

    internal static async Task<(bool EsProductivo, bool Exists)> LoadConceptoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idConcepto,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1 CAST(ES_PRODUCTIVO AS INT) AS es_productivo
FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
WHERE ID_CONCEPTO_TIEMPO = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idConcepto;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (true, false);
        }

        var raw = reader["es_productivo"];
        var esProductivo = raw is not DBNull && Convert.ToInt32(raw, CultureInfo.InvariantCulture) != 0;
        return (esProductivo, true);
    }

    internal static async Task<bool> MaquinaExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idMaquina,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP 1 1 FROM dbo.PQ_PRD_MAQUINAS WHERE ID_MAQUINA = @id;";
        await using var cmd = new SqlCommand(sql, connection, transaction) { CommandTimeout = timeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = idMaquina;
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is not null && scalar is not DBNull;
    }
}
