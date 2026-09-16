using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.AsientosContables;

/// <summary>
/// D6.7.4 — DELETE AsientosContables orquestado (espejo PHP AsientosContablesDeleteService).
/// </summary>
public sealed class AsientosContablesDeleteRunner
{
    public async Task<AsientosContablesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var nroInterno = ExtractInt(parameters, "nro_interno_analitico");
        var rowVersion = ExtractString(parameters, "row_version")?.Trim();

        if (string.IsNullOrWhiteSpace(database)
            || nroInterno is null or <= 0
            || string.IsNullOrWhiteSpace(rowVersion))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "nro_interno_analitico, row_version y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new AsientosContablesOutcome
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
                        nroInterno.Value,
                        rowVersion!,
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
            catch (AsientosContablesCreateRunner.ConflictException cex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Fail("CONFLICT", cex.Message);
            }
            catch (AsientosContablesCreateRunner.ValidationException vex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Fail("VALIDATION", vex.Message);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (AsientosContablesCreateRunner.ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
        }
        catch (AsientosContablesCreateRunner.ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<AsientosContablesOutcome> ExecuteDeleteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int nroInterno,
        string clientRowVersion,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var cabecera = await LoadCabeceraAsync(
                connection, transaction, nroInterno, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (cabecera is null)
        {
            return Fail("NOT_FOUND", "Asiento contable no encontrado.");
        }

        if (!RowVersionMatches(clientRowVersion, cabecera.RowVersionBytes))
        {
            return Fail("CONFLICT", "Conflicto de concurrencia (rowVersion)");
        }

        if (string.Equals(cabecera.Estado, "Registrado", StringComparison.Ordinal))
        {
            return Fail("CONFLICT", "No se puede eliminar un asiento Registrado.");
        }

        await AssertEjercicioPeriodoAbiertosAsync(
                connection, transaction, cabecera.FechaAsiento, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await DeleteHijosAsync(
                connection, transaction, cabecera.IdAsiento, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        await using var delCab = CreateCommand(
            connection, transaction, timeoutSeconds,
            "DELETE FROM dbo.ASIENTO_ANALITICO_CN WHERE ID_ASIENTO_ANALITICO_CN = @id");
        delCab.Parameters.AddWithValue("@id", cabecera.IdAsiento);
        await delCab.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new Dictionary<string, object?>
        {
            ["nroInternoAnalitico"] = nroInterno,
            ["eliminado"] = true
        });
    }

    internal static async Task DeleteHijosAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idAsiento,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var renglonIds = await LoadIdsAsync(
                connection,
                transaction,
                timeoutSeconds,
                "SELECT CAST(ID_RENGLON_ANALITICO_CN AS INT) FROM dbo.RENGLON_ANALITICO_CN WHERE ID_ASIENTO_ANALITICO_CN = @id",
                idAsiento,
                cancellationToken)
            .ConfigureAwait(false);

        if (renglonIds.Count == 0)
        {
            return;
        }

        var importeIds = await LoadIdsInAsync(
                connection,
                transaction,
                timeoutSeconds,
                "ID_RENGLON_IMPORTE_ANALITICO_CN",
                "RENGLON_IMPORTE_ANALITICO_CN",
                "ID_RENGLON_ANALITICO_CN",
                renglonIds,
                cancellationToken)
            .ConfigureAwait(false);

        if (importeIds.Count > 0
            && await TableExistsAsync(connection, transaction, "AUXILIAR_ANALITICO_CN", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            var auxIds = await LoadIdsInAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    "ID_AUXILIAR_ANALITICO_CN",
                    "AUXILIAR_ANALITICO_CN",
                    "ID_RENGLON_IMPORTE_ANALITICO_CN",
                    importeIds,
                    cancellationToken)
                .ConfigureAwait(false);

            if (auxIds.Count > 0)
            {
                if (await TableExistsAsync(
                            connection, transaction, "SUBAUXILIAR_ADICIONAL_ANALITICO_CN", timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false)
                    && await TableExistsAsync(
                            connection, transaction, "SUBAUXILIAR_ANALITICO_CN", timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    var subIds = await LoadIdsInAsync(
                            connection,
                            transaction,
                            timeoutSeconds,
                            "ID_SUBAUXILIAR_ANALITICO_CN",
                            "SUBAUXILIAR_ANALITICO_CN",
                            "ID_AUXILIAR_ANALITICO_CN",
                            auxIds,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (subIds.Count > 0)
                    {
                        await DeleteWhereInAsync(
                                connection,
                                transaction,
                                timeoutSeconds,
                                "SUBAUXILIAR_ADICIONAL_ANALITICO_CN",
                                "ID_SUBAUXILIAR_ANALITICO_CN",
                                subIds,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (await TableExistsAsync(
                            connection, transaction, "SUBAUXILIAR_ANALITICO_CN", timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    await DeleteWhereInAsync(
                            connection,
                            transaction,
                            timeoutSeconds,
                            "SUBAUXILIAR_ANALITICO_CN",
                            "ID_AUXILIAR_ANALITICO_CN",
                            auxIds,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (await TableExistsAsync(
                            connection, transaction, "AUXILIAR_ADICIONAL_ANALITICO_CN", timeoutSeconds, cancellationToken)
                        .ConfigureAwait(false))
                {
                    await DeleteWhereInAsync(
                            connection,
                            transaction,
                            timeoutSeconds,
                            "AUXILIAR_ADICIONAL_ANALITICO_CN",
                            "ID_AUXILIAR_ANALITICO_CN",
                            auxIds,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await DeleteWhereInAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        "AUXILIAR_ANALITICO_CN",
                        "ID_AUXILIAR_ANALITICO_CN",
                        auxIds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (await TableExistsAsync(
                    connection, transaction, "RENGLON_ADICIONAL_ANALITICO_CN", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            await DeleteWhereInAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    "RENGLON_ADICIONAL_ANALITICO_CN",
                    "ID_RENGLON_ANALITICO_CN",
                    renglonIds,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (importeIds.Count > 0)
        {
            await DeleteWhereInAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    "RENGLON_IMPORTE_ANALITICO_CN",
                    "ID_RENGLON_IMPORTE_ANALITICO_CN",
                    importeIds,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await DeleteWhereInAsync(
                connection,
                transaction,
                timeoutSeconds,
                "RENGLON_ANALITICO_CN",
                "ID_RENGLON_ANALITICO_CN",
                renglonIds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record CabeceraInfo(
        int IdAsiento,
        string Estado,
        string FechaAsiento,
        byte[]? RowVersionBytes);

    private static async Task<CabeceraInfo?> LoadCabeceraAsync(
        SqlConnection c, SqlTransaction t, int nroInterno, int timeout, CancellationToken ct)
    {
        var hasRv = await ColumnExistsAsync(c, t, "ASIENTO_ANALITICO_CN", "ROW_VERSION", timeout, ct)
            .ConfigureAwait(false);

        await using var cmd = CreateCommand(
            c, t, timeout,
            hasRv
                ? """
                  SELECT TOP 1
                      CAST(ID_ASIENTO_ANALITICO_CN AS INT),
                      LTRIM(RTRIM(CAST(ISNULL(ESTADO_ASIENTO_ANALITICO, '') AS NVARCHAR(50)))),
                      CONVERT(VARCHAR(10), FECHA_ASIENTO, 23),
                      ROW_VERSION
                  FROM dbo.ASIENTO_ANALITICO_CN
                  WHERE NRO_INTERNO_ANALITICO = @nro
                  """
                : """
                  SELECT TOP 1
                      CAST(ID_ASIENTO_ANALITICO_CN AS INT),
                      LTRIM(RTRIM(CAST(ISNULL(ESTADO_ASIENTO_ANALITICO, '') AS NVARCHAR(50)))),
                      CONVERT(VARCHAR(10), FECHA_ASIENTO, 23)
                  FROM dbo.ASIENTO_ANALITICO_CN
                  WHERE NRO_INTERNO_ANALITICO = @nro
                  """);
        cmd.Parameters.AddWithValue("@nro", nroInterno);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        byte[]? rv = null;
        if (hasRv && !reader.IsDBNull(3))
        {
            rv = (byte[])reader.GetValue(3);
        }

        return new CabeceraInfo(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            rv);
    }

    internal static async Task AssertEjercicioPeriodoAbiertosAsync(
        SqlConnection c, SqlTransaction t, string fecha, int timeout, CancellationToken ct)
    {
        // Reusa la misma resolución CONFLICT del Create (ejercicio/período abiertos).
        _ = await AsientosContablesCreateRunner
            .ResolveEjercicioPeriodoForUpdateAsync(c, t, fecha, timeout, ct)
            .ConfigureAwait(false);
    }

    internal static bool RowVersionMatches(string? clientRowVersion, byte[]? dbBytes)
    {
        if (string.IsNullOrWhiteSpace(clientRowVersion) || dbBytes is null || dbBytes.Length == 0)
        {
            return false;
        }

        return string.Equals(
            Convert.ToBase64String(dbBytes),
            clientRowVersion.Trim(),
            StringComparison.Ordinal);
    }

    private static async Task<List<int>> LoadIdsAsync(
        SqlConnection c, SqlTransaction t, int timeout, string sql, int id, CancellationToken ct)
    {
        var list = new List<int>();
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(reader.GetInt32(0));
        }

        return list;
    }

    private static async Task<List<int>> LoadIdsInAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        string idColumn,
        string table,
        string whereColumn,
        IReadOnlyList<int> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new List<int>();
        }

        var list = new List<int>();
        // Chunk to avoid huge IN lists.
        const int chunkSize = 200;
        for (var offset = 0; offset < ids.Count; offset += chunkSize)
        {
            var chunk = ids.Skip(offset).Take(chunkSize).ToList();
            var names = chunk.Select((_, i) => "@p" + i).ToArray();
            var sql = $"SELECT CAST([{idColumn}] AS INT) FROM dbo.[{table}] WHERE [{whereColumn}] IN ({string.Join(",", names)})";
            await using var cmd = CreateCommand(c, t, timeout, sql);
            for (var i = 0; i < chunk.Count; i++)
            {
                cmd.Parameters.AddWithValue(names[i], chunk[i]);
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(reader.GetInt32(0));
            }
        }

        return list;
    }

    private static async Task DeleteWhereInAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        string table,
        string column,
        IReadOnlyList<int> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return;
        }

        const int chunkSize = 200;
        for (var offset = 0; offset < ids.Count; offset += chunkSize)
        {
            var chunk = ids.Skip(offset).Take(chunkSize).ToList();
            var names = chunk.Select((_, i) => "@p" + i).ToArray();
            var sql = $"DELETE FROM dbo.[{table}] WHERE [{column}] IN ({string.Join(",", names)})";
            await using var cmd = CreateCommand(c, t, timeout, sql);
            for (var i = 0; i < chunk.Count; i++)
            {
                cmd.Parameters.AddWithValue(names[i], chunk[i]);
            }

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            c, t, timeout,
            "SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@obj) AND name = @col");
        cmd.Parameters.AddWithValue("@obj", "dbo." + table);
        cmd.Parameters.AddWithValue("@col", column);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection, SqlTransaction transaction, int timeoutSeconds, string sql) =>
        new(sql, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };

    private static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return ConvertValue(raw)?.ToString();
    }

    private static int? ExtractInt(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return ConvertValue(raw) switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            decimal d => (int)d,
            double dbl => (int)dbl,
            _ => null
        };
    }

    private static object? ConvertValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number => element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        return value;
    }

    private static AsientosContablesOutcome Ok(object data) =>
        new() { Status = JobStatuses.Success, Data = data };

    private static AsientosContablesOutcome Fail(string code, string message) =>
        new() { Status = JobStatuses.Failed, ErrorCode = code, ErrorMessage = message };
}
