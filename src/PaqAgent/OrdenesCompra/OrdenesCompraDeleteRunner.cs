using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.OrdenesCompra;

/// <summary>
/// D6.4.4 MVP — anulación Órdenes de compra (mirror OrdenesCompraDeleteService).
/// Precondición estados 1/2/3 → estado+4; libera STA19; no borrado físico.
/// </summary>
public sealed class OrdenesCompraDeleteRunner
{
    private const string TerminalAnulacion = "Api-PaqSuiteWeb";

    public async Task<OrdenesCompraOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonOc = ExtractInt(parameters, "talon_oc");
        var nOrdenCo = ExtractString(parameters, "n_orden_co")?.Trim();

        if (string.IsNullOrWhiteSpace(database) || talonOc is null or <= 0 || string.IsNullOrWhiteSpace(nOrdenCo))
        {
            return Fail("INVALID_PARAMETERS", "talon_oc, n_orden_co y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new OrdenesCompraOutcome
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
                var outcome = await ExecuteDeleteAsync(
                        connection,
                        transaction,
                        parameters,
                        talonOc.Value,
                        nOrdenCo,
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.Status != JobStatuses.Success)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return outcome;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return outcome;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<OrdenesCompraOutcome> ExecuteDeleteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonOc,
        string nOrdenCo,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var hasRowVersion = await ColumnExistsAsync(
                connection, transaction, "CPA35", "ROW_VERSION", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasRowVersion
                ? """
                  SELECT TOP 1
                      CAST(ID_CPA35 AS INT) AS idCpa35,
                      CAST(ESTADO AS INT) AS estado,
                      ROW_VERSION AS rowVersion
                  FROM dbo.CPA35
                  WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
                  """
                : """
                  SELECT TOP 1
                      CAST(ID_CPA35 AS INT) AS idCpa35,
                      CAST(ESTADO AS INT) AS estado
                  FROM dbo.CPA35
                  WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
                  """);
        load.Parameters.AddWithValue("@t", talonOc);
        load.Parameters.AddWithValue("@n", nOrdenCo);

        int idCpa35;
        int estado;
        byte[]? rowVersionBytes = null;

        await using (var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return Fail("NOT_FOUND", "Orden de compra no encontrada.");
            }

            idCpa35 = reader.GetInt32(0);
            estado = reader.GetInt32(1);
            if (hasRowVersion && !reader.IsDBNull(2))
            {
                rowVersionBytes = (byte[])reader.GetValue(2);
            }
        }

        if (hasRowVersion)
        {
            var clientRv = ExtractString(parameters, "row_version");
            if (string.IsNullOrWhiteSpace(clientRv)
                || rowVersionBytes is null
                || !string.Equals(Convert.ToBase64String(rowVersionBytes), clientRv.Trim(), StringComparison.Ordinal))
            {
                return Fail("ROW_VERSION_CONFLICT", "Conflicto de concurrencia (rowVersion).");
            }
        }

        if (estado is not (1 or 2 or 3))
        {
            return Fail("PRECONDICION_DELETE", "La orden no cumple la precondicion de anulacion.");
        }

        var nuevoEstado = estado + 4;
        var usuario = ExtractString(parameters, "usuario_anulacion")?.Trim() ?? "api";
        var now = DateTime.Now;
        var hora = now.ToString("HHmmss", CultureInfo.InvariantCulture);

        await LiberarSta19Async(connection, transaction, idCpa35, talonOc, nOrdenCo, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var setParts = new List<string> { "ESTADO = @estado" };
        await using var updCab = CreateCommand(connection, transaction, timeoutSeconds, "SELECT 1");
        updCab.Parameters.AddWithValue("@estado", nuevoEstado);

        if (await ColumnExistsAsync(connection, transaction, "CPA35", "FECHA_ANULACION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("FECHA_ANULACION = @fechaAnul");
            updCab.Parameters.AddWithValue("@fechaAnul", now);
        }

        if (await ColumnExistsAsync(connection, transaction, "CPA35", "HORA_ANULACION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("HORA_ANULACION = @horaAnul");
            updCab.Parameters.AddWithValue("@horaAnul", hora);
        }

        if (await ColumnExistsAsync(connection, transaction, "CPA35", "USUARIO_ANULACION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("USUARIO_ANULACION = @userAnul");
            updCab.Parameters.AddWithValue("@userAnul", usuario);
        }

        if (await ColumnExistsAsync(connection, transaction, "CPA35", "TERMINAL_ANULACION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            setParts.Add("TERMINAL_ANULACION = @termAnul");
            updCab.Parameters.AddWithValue("@termAnul", TerminalAnulacion);
        }

        if (await ColumnExistsAsync(connection, transaction, "CPA35", "ID_ESTADO_ORDEN_COMPRA", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            && await TableExistsAsync(connection, transaction, "ESTADO_ORDEN_COMPRA", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            var idEstado = await LookupIdAsync(
                    connection, transaction, timeoutSeconds, cancellationToken,
                    "SELECT TOP 1 ID_ESTADO_ORDEN_COMPRA FROM dbo.ESTADO_ORDEN_COMPRA WHERE CAST(COD_ESTADO AS INT)=@e OR CAST(ESTADO AS INT)=@e",
                    ("@e", nuevoEstado))
                .ConfigureAwait(false);
            if (idEstado is > 0)
            {
                setParts.Add("ID_ESTADO_ORDEN_COMPRA = @idEstado");
                updCab.Parameters.AddWithValue("@idEstado", idEstado.Value);
            }
        }

        updCab.CommandText = $"""
            UPDATE dbo.CPA35
            SET {string.Join(", ", setParts)}
            WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
            """;
        updCab.Parameters.AddWithValue("@t", talonOc);
        updCab.Parameters.AddWithValue("@n", nOrdenCo);
        await updCab.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await AnularRenglonesAsync(
                connection, transaction, idCpa35, talonOc, nOrdenCo, nuevoEstado, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new Dictionary<string, object?>
        {
            ["idCpa35"] = idCpa35,
            ["talonOc"] = talonOc,
            ["nOrdenCo"] = nOrdenCo,
            ["estado"] = nuevoEstado,
            ["eliminado"] = false,
            ["anulado"] = true
        });
    }

    private static async Task AnularRenglonesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        int nuevoEstado,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "CPA36", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            || !await ColumnExistsAsync(connection, transaction, "CPA36", "ESTADO", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasIdCab = await ColumnExistsAsync(
                connection, transaction, "CPA36", "ID_CPA35", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasIdCab && idCpa35 > 0
                ? "UPDATE dbo.CPA36 SET ESTADO = @e WHERE ID_CPA35 = @id"
                : """
                  UPDATE dbo.CPA36 SET ESTADO = @e
                  WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
                  """);
        upd.Parameters.AddWithValue("@e", nuevoEstado);
        if (hasIdCab && idCpa35 > 0)
        {
            upd.Parameters.AddWithValue("@id", idCpa35);
        }
        else
        {
            upd.Parameters.AddWithValue("@t", talonOc);
            upd.Parameters.AddWithValue("@n", nOrdenCo);
        }

        await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task LiberarSta19Async(
        SqlConnection connection,
        SqlTransaction transaction,
        int idCpa35,
        int talonOc,
        string nOrdenCo,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "CPA36", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            || !await TableExistsAsync(connection, transaction, "STA19", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasIdCab = await ColumnExistsAsync(
                connection, transaction, "CPA36", "ID_CPA35", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasEstado = await ColumnExistsAsync(
                connection, transaction, "CPA36", "ESTADO", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCant2 = await ColumnExistsAsync(
                connection, transaction, "CPA36", "CAN_PEDIDA_2", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasRec2 = await ColumnExistsAsync(
                connection, transaction, "CPA36", "CAN_RECIBI_2", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var cant2Expr = hasCant2 ? "CAST(ISNULL(CAN_PEDIDA_2, 0) AS FLOAT)" : "CAST(0 AS FLOAT)";
        var rec2Expr = hasRec2 ? "CAST(ISNULL(CAN_RECIBI_2, 0) AS FLOAT)" : "CAST(0 AS FLOAT)";
        var estadoExpr = hasEstado ? "CAST(ISNULL(ESTADO, 1) AS INT)" : "CAST(1 AS INT)";

        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasIdCab && idCpa35 > 0
                ? $"""
                  SELECT
                      LTRIM(RTRIM(CAST(ISNULL(COD_ARTICU, '') AS NVARCHAR(50)))),
                      LTRIM(RTRIM(CAST(ISNULL(COD_DEPOSI, '') AS NVARCHAR(20)))),
                      CAST(ISNULL(CAN_PEDIDA, 0) AS FLOAT),
                      CAST(ISNULL(CAN_RECIBI, 0) AS FLOAT),
                      {cant2Expr},
                      {rec2Expr},
                      {estadoExpr}
                  FROM dbo.CPA36
                  WHERE ID_CPA35 = @id
                  """
                : $"""
                  SELECT
                      LTRIM(RTRIM(CAST(ISNULL(COD_ARTICU, '') AS NVARCHAR(50)))),
                      LTRIM(RTRIM(CAST(ISNULL(COD_DEPOSI, '') AS NVARCHAR(20)))),
                      CAST(ISNULL(CAN_PEDIDA, 0) AS FLOAT),
                      CAST(ISNULL(CAN_RECIBI, 0) AS FLOAT),
                      {cant2Expr},
                      {rec2Expr},
                      {estadoExpr}
                  FROM dbo.CPA36
                  WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(N_ORDEN_CO AS NVARCHAR(50)))) = @n
                  """);

        if (hasIdCab && idCpa35 > 0)
        {
            load.Parameters.AddWithValue("@id", idCpa35);
        }
        else
        {
            load.Parameters.AddWithValue("@t", talonOc);
            load.Parameters.AddWithValue("@n", nOrdenCo);
        }

        var rows = new List<(string CodArt, string CodDep, decimal Pend, decimal Pend2, int Estado)>();
        await using (var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var codArt = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var codDep = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var cantPed = Convert.ToDecimal(reader.GetDouble(2));
                var cantRec = Convert.ToDecimal(reader.GetDouble(3));
                var cantPed2 = Convert.ToDecimal(reader.GetDouble(4));
                var cantRec2 = Convert.ToDecimal(reader.GetDouble(5));
                var estadoLinea = reader.GetInt32(6);
                rows.Add((codArt, codDep, cantPed - cantRec, cantPed2 - cantRec2, estadoLinea));
            }
        }

        foreach (var (codArt, codDep, pend, pend2, estadoLinea) in rows)
        {
            if (estadoLinea is not (1 or 2 or 3) || string.IsNullOrWhiteSpace(codArt) || string.IsNullOrWhiteSpace(codDep) || pend <= 0)
            {
                continue;
            }

            var idSta11 = await LookupIdAsync(
                    connection, transaction, timeoutSeconds, cancellationToken,
                    "SELECT TOP 1 ID_STA11 FROM dbo.STA11 WHERE LTRIM(RTRIM(COD_ARTICU))=@a",
                    ("@a", codArt))
                .ConfigureAwait(false);
            var idSta22 = await LookupIdAsync(
                    connection, transaction, timeoutSeconds, cancellationToken,
                    "SELECT TOP 1 ID_STA22 FROM dbo.STA22 WHERE LTRIM(RTRIM(COD_DEPOSI))=@d",
                    ("@d", codDep))
                .ConfigureAwait(false);

            if (idSta11 is null or <= 0 || idSta22 is null or <= 0)
            {
                continue;
            }

            await RestarSta19Async(
                    connection, transaction, idSta11.Value, idSta22.Value, pend, pend2, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RestarSta19Async(
        SqlConnection connection,
        SqlTransaction transaction,
        int idSta11,
        int idSta22,
        decimal cant,
        decimal cant2,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await ColumnExistsAsync(connection, transaction, "STA19", "CANT_PEND", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasCant2 = await ColumnExistsAsync(
                connection, transaction, "STA19", "CANT_PEND_2", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var set2 = hasCant2
            ? ", CANT_PEND_2 = CASE WHEN ISNULL(CANT_PEND_2, 0) - @cant2 < 0 THEN 0 ELSE ISNULL(CANT_PEND_2, 0) - @cant2 END"
            : string.Empty;

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            $"""
            UPDATE dbo.STA19
            SET CANT_PEND = CASE WHEN ISNULL(CANT_PEND, 0) - @cant < 0 THEN 0 ELSE ISNULL(CANT_PEND, 0) - @cant END
                {set2}
            WHERE ID_STA11 = @a AND ID_STA22 = @d
            """);
        upd.Parameters.AddWithValue("@cant", cant);
        if (hasCant2)
        {
            upd.Parameters.AddWithValue("@cant2", Math.Max(0m, cant2));
        }

        upd.Parameters.AddWithValue("@a", idSta11);
        upd.Parameters.AddWithValue("@d", idSta22);
        await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int?> LookupIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        string sql,
        (string name, object value) param)
    {
        try
        {
            await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
            cmd.Parameters.AddWithValue(param.name, param.value);
            var o = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return o is null or DBNull ? null : Convert.ToInt32(o, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT 1 FROM sys.columns sc INNER JOIN sys.tables st ON st.object_id=sc.object_id WHERE st.name=@t AND sc.name=@c");
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static SqlCommand CreateCommand(SqlConnection c, SqlTransaction t, int timeout, string sql) =>
        new(sql, c, t) { CommandType = CommandType.Text, CommandTimeout = Math.Max(1, timeout) };

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
            string s when int.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) => i,
            JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var p) => p,
            _ => null
        };
    }

    private static OrdenesCompraOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static OrdenesCompraOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
