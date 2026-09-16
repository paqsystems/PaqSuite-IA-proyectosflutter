using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.PedidosVenta;

/// <summary>
/// D6.3b MVP — DELETE PedidosVenta (mirror PedidosVentaDeleteService).
/// Precondiciones estados 1/7/2; mantPed → anula (estado 5) o borrado físico; libera STA19.
/// </summary>
public sealed class PedidosVentaDeleteRunner
{
    public async Task<PedidosVentaOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonPed = ExtractInt(parameters, "talon_ped");
        var nroPedido = ExtractString(parameters, "nro_pedido")?.Trim();

        if (string.IsNullOrWhiteSpace(database) || talonPed is null or <= 0 || string.IsNullOrWhiteSpace(nroPedido))
        {
            return Fail("INVALID_PARAMETERS", "talon_ped, nro_pedido y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new PedidosVentaOutcome
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
                        talonPed.Value,
                        nroPedido,
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

    private static async Task<PedidosVentaOutcome> ExecuteDeleteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonPed,
        string nroPedido,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var hasRowVersion = await ColumnExistsAsync(
                connection, transaction, "GVA21", "ROW_VERSION", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasRowVersion
                ? """
                  SELECT TOP 1
                      CAST(ID_GVA21 AS INT) AS idGva21,
                      CAST(ESTADO AS INT) AS estado,
                      CAST(ISNULL(COMP_STK, 0) AS INT) AS compStk,
                      ROW_VERSION AS rowVersion
                  FROM dbo.GVA21
                  WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                  """
                : """
                  SELECT TOP 1
                      CAST(ID_GVA21 AS INT) AS idGva21,
                      CAST(ESTADO AS INT) AS estado,
                      CAST(ISNULL(COMP_STK, 0) AS INT) AS compStk
                  FROM dbo.GVA21
                  WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                  """);
        load.Parameters.AddWithValue("@t", talonPed);
        load.Parameters.AddWithValue("@n", nroPedido);

        int idGva21;
        int estado;
        bool compStk;
        byte[]? rowVersionBytes = null;

        await using (var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return Fail("NOT_FOUND", "Pedido no encontrado.");
            }

            idGva21 = reader.GetInt32(0);
            estado = reader.GetInt32(1);
            compStk = reader.GetInt32(2) != 0;
            if (hasRowVersion && !reader.IsDBNull(3))
            {
                rowVersionBytes = (byte[])reader.GetValue(3);
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

        var renglones = await LoadRenglonesPrecondicionAsync(
                connection, transaction, idGva21, talonPed, nroPedido, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (!PuedeEliminar(estado, renglones))
        {
            return Fail("PRECONDICION_DELETE", "El pedido no cumple la precondicion de eliminacion.");
        }

        var mantPed = await ReadMantPedAsync(connection, transaction, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (compStk && estado < 3)
        {
            await LiberarSta19Async(
                    connection, transaction, idGva21, talonPed, nroPedido, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        if (mantPed)
        {
            await using var anular = CreateCommand(
                connection, transaction, timeoutSeconds,
                """
                UPDATE dbo.GVA21
                SET ESTADO = 5
                WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                """);
            anular.Parameters.AddWithValue("@t", talonPed);
            anular.Parameters.AddWithValue("@n", nroPedido);
            await anular.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return Ok(new Dictionary<string, object?>
            {
                ["idGva21"] = idGva21,
                ["talonPed"] = talonPed,
                ["nroPedido"] = nroPedido,
                ["estado"] = 5,
                ["eliminado"] = false,
                ["anulado"] = true
            });
        }

        await DeleteFisicoAsync(connection, transaction, idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new Dictionary<string, object?>
        {
            ["idGva21"] = idGva21,
            ["talonPed"] = talonPed,
            ["nroPedido"] = nroPedido,
            ["eliminado"] = true,
            ["anulado"] = false
        });
    }

    private static bool PuedeEliminar(int estado, IReadOnlyList<RenglonPrecondicion> renglones)
    {
        if (estado is 1 or 7)
        {
            return true;
        }

        if (estado == 2)
        {
            return CumpleEstadoAprobado(renglones);
        }

        return false;
    }

    private static bool CumpleEstadoAprobado(IReadOnlyList<RenglonPrecondicion> renglones)
    {
        foreach (var r in renglones)
        {
            if (string.IsNullOrWhiteSpace(r.CodArticu))
            {
                continue;
            }

            if (r.CantADes > 0 || r.CantAFac > 0)
            {
                return false;
            }

            if (Math.Abs(r.CantPenD - r.CantPedid) > 0.0001m
                || Math.Abs(r.CantPenF - r.CantPedid) > 0.0001m)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<List<RenglonPrecondicion>> LoadRenglonesPrecondicionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idGva21,
        int talonPed,
        string nroPedido,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var list = new List<RenglonPrecondicion>();
        if (!await TableExistsAsync(connection, transaction, "GVA03", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return list;
        }

        var hasIdPed = await ColumnExistsAsync(
                connection, transaction, "GVA03", "ID_GVA21", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var hasCantAFac = await ColumnExistsAsync(
                connection, transaction, "GVA03", "CANT_A_FAC", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCantADes = await ColumnExistsAsync(
                connection, transaction, "GVA03", "CANT_A_DES", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasCodArticu = await ColumnExistsAsync(
                connection, transaction, "GVA03", "COD_ARTICU", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var cantADesExpr = hasCantADes ? "CAST(ISNULL(CANT_A_DES, 0) AS FLOAT)" : "CAST(0 AS FLOAT)";
        var cantAFacExpr = hasCantAFac ? "CAST(ISNULL(CANT_A_FAC, 0) AS FLOAT)" : "CAST(0 AS FLOAT)";
        var codArticuExpr = hasCodArticu
            ? "LTRIM(RTRIM(CAST(ISNULL(COD_ARTICU, '') AS NVARCHAR(50))))"
            : "CAST('' AS NVARCHAR(50))";

        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasIdPed && idGva21 > 0
                ? $"""
                  SELECT
                      CAST(ISNULL(CANT_PEDID, 0) AS FLOAT),
                      CAST(ISNULL(CANT_PEN_D, 0) AS FLOAT),
                      CAST(ISNULL(CANT_PEN_F, 0) AS FLOAT),
                      {cantADesExpr},
                      {cantAFacExpr},
                      {codArticuExpr}
                  FROM dbo.GVA03
                  WHERE ID_GVA21 = @id
                  """
                : $"""
                  SELECT
                      CAST(ISNULL(CANT_PEDID, 0) AS FLOAT),
                      CAST(ISNULL(CANT_PEN_D, 0) AS FLOAT),
                      CAST(ISNULL(CANT_PEN_F, 0) AS FLOAT),
                      {cantADesExpr},
                      {cantAFacExpr},
                      {codArticuExpr}
                  FROM dbo.GVA03
                  WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                  """);

        if (hasIdPed && idGva21 > 0)
        {
            cmd.Parameters.AddWithValue("@id", idGva21);
        }
        else
        {
            cmd.Parameters.AddWithValue("@t", talonPed);
            cmd.Parameters.AddWithValue("@n", nroPedido);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new RenglonPrecondicion(
                Convert.ToDecimal(reader.GetDouble(0)),
                Convert.ToDecimal(reader.GetDouble(1)),
                Convert.ToDecimal(reader.GetDouble(2)),
                Convert.ToDecimal(reader.GetDouble(3)),
                Convert.ToDecimal(reader.GetDouble(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return list;
    }

    private static async Task<bool> ReadMantPedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "GVA16", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        if (!await ColumnExistsAsync(connection, transaction, "GVA16", "MANT_PED", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return true;
        }

        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            "SELECT TOP 1 CAST(ISNULL(MANT_PED, 1) AS INT) FROM dbo.GVA16");
        var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (raw is null or DBNull)
        {
            return true;
        }

        return Convert.ToInt32(raw, CultureInfo.InvariantCulture) != 0;
    }

    private static async Task LiberarSta19Async(
        SqlConnection connection,
        SqlTransaction transaction,
        int idGva21,
        int talonPed,
        string nroPedido,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "STA19", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            || !await TableExistsAsync(connection, transaction, "GVA03", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var hasIdPed = await ColumnExistsAsync(
                connection, transaction, "GVA03", "ID_GVA21", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            hasIdPed && idGva21 > 0
                ? """
                  SELECT
                      CAST(ISNULL(ID_STA11, 0) AS INT),
                      CAST(ISNULL(ID_STA22, 0) AS INT),
                      CAST(ISNULL(CANT_PEN_D, 0) AS FLOAT)
                  FROM dbo.GVA03
                  WHERE ID_GVA21 = @id
                  """
                : """
                  SELECT
                      CAST(ISNULL(ID_STA11, 0) AS INT),
                      CAST(ISNULL(ID_STA22, 0) AS INT),
                      CAST(ISNULL(CANT_PEN_D, 0) AS FLOAT)
                  FROM dbo.GVA03
                  WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                  """);

        if (hasIdPed && idGva21 > 0)
        {
            load.Parameters.AddWithValue("@id", idGva21);
        }
        else
        {
            load.Parameters.AddWithValue("@t", talonPed);
            load.Parameters.AddWithValue("@n", nroPedido);
        }

        var rows = new List<(int IdSta11, int IdSta22, decimal CantPenD)>();
        await using (var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    Convert.ToDecimal(reader.GetDouble(2))));
            }
        }

        foreach (var (idSta11, idSta22, cantPenD) in rows)
        {
            if (idSta11 <= 0 || idSta22 <= 0 || cantPenD <= 0)
            {
                continue;
            }

            await using var upd = CreateCommand(
                connection, transaction, timeoutSeconds,
                """
                UPDATE dbo.STA19
                SET CANT_COMP = CASE WHEN ISNULL(CANT_COMP,0) >= @d THEN ISNULL(CANT_COMP,0)-@d ELSE 0 END
                WHERE ID_STA11=@a AND ID_STA22=@b
                """);
            upd.Parameters.AddWithValue("@d", cantPenD);
            upd.Parameters.AddWithValue("@a", idSta11);
            upd.Parameters.AddWithValue("@b", idSta22);
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeleteFisicoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idGva21,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var lineIds = new List<int>();
        if (await TableExistsAsync(connection, transaction, "GVA03", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            && await ColumnExistsAsync(connection, transaction, "GVA03", "ID_GVA03", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            && await ColumnExistsAsync(connection, transaction, "GVA03", "ID_GVA21", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            await using var lines = CreateCommand(
                connection, transaction, timeoutSeconds,
                "SELECT CAST(ID_GVA03 AS INT) FROM dbo.GVA03 WHERE ID_GVA21 = @id");
            lines.Parameters.AddWithValue("@id", idGva21);
            await using var reader = await lines.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lineIds.Add(reader.GetInt32(0));
            }
        }

        await DeleteByIdAsync(connection, transaction, "PEDIDO_CUOTA", "ID_GVA21", idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        await DeleteByIdAsync(connection, transaction, "PEDIDO_IMPUESTO", "ID_GVA21", idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        await DeleteByIdAsync(connection, transaction, "GVA126", "ID_GVA21", idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (lineIds.Count > 0
            && await TableExistsAsync(connection, transaction, "GVA45", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            && await ColumnExistsAsync(connection, transaction, "GVA45", "ID_GVA03", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            foreach (var id in lineIds)
            {
                await using var del = CreateCommand(
                    connection, transaction, timeoutSeconds,
                    "DELETE FROM dbo.GVA45 WHERE ID_GVA03 = @id");
                del.Parameters.AddWithValue("@id", id);
                await del.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await DeleteByIdAsync(connection, transaction, "GVA03", "ID_GVA21", idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        await DeleteByIdAsync(connection, transaction, "GVA38", "ID_GVA21", idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        await DeleteByIdAsync(connection, transaction, "GVA21", "ID_GVA21", idGva21, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task DeleteByIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        string column,
        int id,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, table, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false)
            || !await ColumnExistsAsync(connection, transaction, table, column, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            $"DELETE FROM dbo.{table} WHERE {column} = @id");
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record RenglonPrecondicion(
        decimal CantPedid,
        decimal CantPenD,
        decimal CantPenF,
        decimal CantADes,
        decimal CantAFac,
        string? CodArticu);

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

    private static PedidosVentaOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static PedidosVentaOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
