using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.PedidosVenta;

/// <summary>
/// D6.3 MVP — PATCH PedidosVenta (observaciones/fechaEntr/estado + cantPedid por idGva03).
/// Sin Delta6 reemplazar. Respuesta = pedido completo vía Get.
/// </summary>
public sealed class PedidosVentaUpdateRunner
{
    private static readonly HashSet<int> EstadosEditables = [1, 2, 6];

    private readonly PedidosVentaGatewayRunner getRunner;

    public PedidosVentaUpdateRunner(PedidosVentaGatewayRunner getRunner)
    {
        this.getRunner = getRunner;
    }

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
                var patchOutcome = await ExecutePatchAsync(
                        connection,
                        transaction,
                        parameters,
                        talonPed.Value,
                        nroPedido,
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (patchOutcome.Status != JobStatuses.Success)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return patchOutcome;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            if (!PedidosVentaCatalog.TryGet("PedidosVenta.Get", out var getDef))
            {
                return Fail("INTERNAL_ERROR", "PedidosVenta.Get no registrado en catalogo.");
            }

            return await getRunner
                .RunAsync(
                    getDef,
                    agentOptions,
                    new Dictionary<string, object?>
                    {
                        ["_database"] = database,
                        ["talon_ped"] = talonPed.Value,
                        ["nro_pedido"] = nroPedido
                    },
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<PedidosVentaOutcome> ExecutePatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonPed,
        string nroPedido,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1
                CAST(ID_GVA21 AS INT) AS idGva21,
                CAST(ESTADO AS INT) AS estado,
                CAST(ISNULL(COMP_STK, 0) AS INT) AS compStk,
                ROW_VERSION AS rowVersion
            FROM dbo.GVA21
            WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
            """);

        // ROW_VERSION may be missing — fallback query without it
        var hasRowVersion = await ColumnExistsAsync(connection, transaction, "GVA21", "ROW_VERSION", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (!hasRowVersion)
        {
            load.CommandText = """
                SELECT TOP 1
                    CAST(ID_GVA21 AS INT) AS idGva21,
                    CAST(ESTADO AS INT) AS estado,
                    CAST(ISNULL(COMP_STK, 0) AS INT) AS compStk
                FROM dbo.GVA21
                WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                """;
        }

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

        if (!EstadosEditables.Contains(estado))
        {
            return Fail("ESTADO_NO_EDITABLE", "Estado del pedido no permite modificacion.");
        }

        var nuevoEstado = ExtractInt(parameters, "estado");
        if (nuevoEstado is not null && !EstadosEditables.Contains(nuevoEstado.Value))
        {
            return Fail("ESTADO_NO_PERMITIDO", "Estado solicitado no permitido.");
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

        var setParts = new List<string>();
        await using var updCab = CreateCommand(connection, transaction, timeoutSeconds, "SELECT 1");

        if (parameters.ContainsKey("observaciones"))
        {
            setParts.Add("OBSERVACIONES = @obs");
            updCab.Parameters.AddWithValue("@obs", (object?)ExtractString(parameters, "observaciones") ?? DBNull.Value);
        }

        if (parameters.ContainsKey("fecha_entr"))
        {
            setParts.Add("FECHA_ENTR = @fechaEntr");
            var fecha = ExtractDate(parameters, "fecha_entr");
            updCab.Parameters.AddWithValue("@fechaEntr", (object?)fecha?.Date ?? DBNull.Value);
        }

        if (nuevoEstado is not null)
        {
            setParts.Add("ESTADO = @estado");
            updCab.Parameters.AddWithValue("@estado", nuevoEstado.Value);
        }

        if (setParts.Count > 0)
        {
            updCab.CommandText = $"""
                UPDATE dbo.GVA21
                SET {string.Join(", ", setParts)}
                WHERE TALON_PED = @t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @n
                """;
            updCab.Parameters.AddWithValue("@t", talonPed);
            updCab.Parameters.AddWithValue("@n", nroPedido);
            await updCab.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var renglonesJson = ExtractString(parameters, "renglones_json");
        if (!string.IsNullOrWhiteSpace(renglonesJson))
        {
            var patchLines = ParseRenglonesPatch(renglonesJson);
            foreach (var line in patchLines)
            {
                var lineOutcome = await PatchRenglonAsync(
                        connection, transaction, line, compStk, estado, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
                if (lineOutcome is not null)
                {
                    return lineOutcome;
                }
            }
        }

        return Ok(new Dictionary<string, object?> { ["patched"] = true });
    }

    private static async Task<PedidosVentaOutcome?> PatchRenglonAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        RenglonPatch line,
        bool compStk,
        int estadoActual,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (line.IdGva03 <= 0 || line.CantPedid is null)
        {
            return null;
        }

        await using var load = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1
                CAST(ISNULL(CANT_PEDID, 0) AS FLOAT) AS cantPed,
                CAST(ISNULL(CANT_PEN_D, 0) AS FLOAT) AS cantPenD,
                CAST(ISNULL(CANT_PEN_F, 0) AS FLOAT) AS cantPenF,
                CAST(ISNULL(CANT_A_DES, 0) AS FLOAT) AS cantADes,
                CAST(ISNULL(ID_STA11, 0) AS INT) AS idSta11,
                CAST(ISNULL(ID_STA22, 0) AS INT) AS idSta22
            FROM dbo.GVA03
            WHERE ID_GVA03 = @id
            """);
        load.Parameters.AddWithValue("@id", line.IdGva03);

        decimal cantAnterior;
        decimal cantPenD;
        decimal cantPenF;
        decimal cantADes;
        int idSta11;
        int idSta22;

        await using (var reader = await load.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            cantAnterior = Convert.ToDecimal(reader.GetDouble(0));
            cantPenD = Convert.ToDecimal(reader.GetDouble(1));
            cantPenF = Convert.ToDecimal(reader.GetDouble(2));
            cantADes = Convert.ToDecimal(reader.GetDouble(3));
            idSta11 = reader.GetInt32(4);
            idSta22 = reader.GetInt32(5);
        }

        var nuevaCant = line.CantPedid.Value;
        if (cantADes > 0 && nuevaCant < cantAnterior)
        {
            return Fail("CANT_DESPACHADA", "cantPedid no puede disminuir en renglon despachado.");
        }

        var delta = nuevaCant - cantAnterior;
        if (Math.Abs(delta) < 0.0001m)
        {
            return null;
        }

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            UPDATE dbo.GVA03
            SET CANT_PEDID = @cant,
                CANT_PEN_D = @penD,
                CANT_PEN_F = @penF
            WHERE ID_GVA03 = @id
            """);
        upd.Parameters.AddWithValue("@cant", nuevaCant);
        upd.Parameters.AddWithValue("@penD", cantPenD + delta);
        upd.Parameters.AddWithValue("@penF", cantPenF + delta);
        upd.Parameters.AddWithValue("@id", line.IdGva03);
        await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (compStk && estadoActual < 3 && idSta11 > 0 && idSta22 > 0)
        {
            await AjustarSta19Async(connection, transaction, idSta11, idSta22, delta, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    private static async Task AjustarSta19Async(
        SqlConnection connection,
        SqlTransaction transaction,
        int idSta11,
        int idSta22,
        decimal delta,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "STA19", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        if (delta > 0)
        {
            await using var upd = CreateCommand(
                connection, transaction, timeoutSeconds,
                """
                UPDATE dbo.STA19 SET CANT_COMP = ISNULL(CANT_COMP,0)+@d
                WHERE ID_STA11=@a AND ID_STA22=@b
                """);
            upd.Parameters.AddWithValue("@d", delta);
            upd.Parameters.AddWithValue("@a", idSta11);
            upd.Parameters.AddWithValue("@b", idSta22);
            if (await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                await using var ins = CreateCommand(
                    connection, transaction, timeoutSeconds,
                    "INSERT INTO dbo.STA19 (ID_STA11, ID_STA22, CANT_COMP) VALUES (@a,@b,@d)");
                ins.Parameters.AddWithValue("@a", idSta11);
                ins.Parameters.AddWithValue("@b", idSta22);
                ins.Parameters.AddWithValue("@d", delta);
                await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            var cant = Math.Abs(delta);
            await using var upd = CreateCommand(
                connection, transaction, timeoutSeconds,
                """
                UPDATE dbo.STA19
                SET CANT_COMP = CASE WHEN ISNULL(CANT_COMP,0) >= @d THEN ISNULL(CANT_COMP,0)-@d ELSE 0 END
                WHERE ID_STA11=@a AND ID_STA22=@b
                """);
            upd.Parameters.AddWithValue("@d", cant);
            upd.Parameters.AddWithValue("@a", idSta11);
            upd.Parameters.AddWithValue("@b", idSta22);
            await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record RenglonPatch(int IdGva03, decimal? CantPedid);

    private static List<RenglonPatch> ParseRenglonesPatch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<RenglonPatch>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = GetJsonInt(el, "idGva03") ?? GetJsonInt(el, "id_gva03") ?? 0;
            var cant = GetJsonDecimal(el, "cantPedid") ?? GetJsonDecimal(el, "cant_pedid");
            list.Add(new RenglonPatch(id, cant));
        }

        return list;
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

    private static DateTime? ExtractDate(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        var s = ExtractString(parameters, key);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt) ? dt : null;
    }

    private static int? GetJsonInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(p.GetString(), out var i) => i,
            _ => null
        };
    }

    private static decimal? GetJsonDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static PedidosVentaOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static PedidosVentaOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
