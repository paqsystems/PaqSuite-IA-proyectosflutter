using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.AsientosContables;

/// <summary>
/// D6.7.3 — PATCH AsientosContables orquestado (espejo PHP AsientosContablesPatchService).
/// Success = shape Get completo (sin creado) vía AsientosContables.Get.
/// </summary>
public sealed class AsientosContablesUpdateRunner
{
    public const string TerminalModificacion = AsientosContablesCreateRunner.TerminalIngreso;

    private readonly AsientosContablesGatewayRunner getRunner;

    public AsientosContablesUpdateRunner(AsientosContablesGatewayRunner getRunner)
    {
        this.getRunner = getRunner;
    }

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

        List<AsientosContablesCreateRunner.RenglonInput>? renglones = null;
        var hasRenglones = parameters.ContainsKey("renglones_json")
                          && !string.IsNullOrWhiteSpace(ExtractString(parameters, "renglones_json"));
        if (hasRenglones)
        {
            try
            {
                renglones = AsientosContablesCreateRunner.ParseRenglones(
                    ExtractString(parameters, "renglones_json")!);
            }
            catch (Exception ex)
            {
                return Fail("INVALID_PARAMETERS", "renglones_json invalido: " + ex.Message);
            }
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
                        nroInterno.Value,
                        rowVersion!,
                        renglones,
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

            if (!AsientosContablesCatalog.TryGet("AsientosContables.Get", out var getDef))
            {
                return Fail("INTERNAL_ERROR", "AsientosContables.Get no registrado en catalogo.");
            }

            return await getRunner
                .RunAsync(
                    getDef,
                    agentOptions,
                    new Dictionary<string, object?>
                    {
                        ["_database"] = database,
                        ["nro_interno_analitico"] = nroInterno.Value
                    },
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static async Task<AsientosContablesOutcome> ExecutePatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int nroInterno,
        string clientRowVersion,
        List<AsientosContablesCreateRunner.RenglonInput>? renglones,
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

        if (!AsientosContablesDeleteRunner.RowVersionMatches(clientRowVersion, cabecera.RowVersionBytes))
        {
            return Fail("CONFLICT", "Conflicto de concurrencia (rowVersion)");
        }

        if (string.Equals(cabecera.Estado, "Registrado", StringComparison.Ordinal))
        {
            return Fail("CONFLICT", "No se puede modificar un asiento Registrado.");
        }

        await AsientosContablesDeleteRunner
            .AssertEjercicioPeriodoAbiertosAsync(
                connection, transaction, cabecera.FechaAsiento, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var estadoActual = cabecera.Estado;
        var estadoNuevo = estadoActual;

        if (parameters.ContainsKey("estado_asiento_analitico")
            && ExtractString(parameters, "estado_asiento_analitico") is { } estadoRaw
            && !string.IsNullOrWhiteSpace(estadoRaw))
        {
            estadoNuevo = estadoRaw.Trim();
            if (estadoNuevo is not ("Borrador" or "Ingresado" or "Registrado"))
            {
                return Fail("VALIDATION", "estadoAsientoAnalitico inválido.");
            }

            if (!string.Equals(estadoNuevo, estadoActual, StringComparison.Ordinal)
                && !string.Equals(cabecera.EditaEstado, "S", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("VALIDATION", "El tipo de asiento no permite editar el estado.");
            }
        }

        if (renglones is not null)
        {
            if (AsientosContablesCreateRunner.ExigePartidaDoble(estadoNuevo)
                && !AsientosContablesCreateRunner.EstaBalanceado(renglones))
            {
                return Fail("VALIDATION", "El asiento no cumple partida doble.");
            }
        }
        else if (AsientosContablesCreateRunner.ExigePartidaDoble(estadoNuevo)
                 && !string.Equals(estadoNuevo, estadoActual, StringComparison.Ordinal))
        {
            var existentes = await LoadRenglonesBalanceAsync(
                    connection, transaction, cabecera.IdAsiento, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (!AsientosContablesCreateRunner.EstaBalanceado(existentes))
            {
                return Fail("VALIDATION", "El asiento no cumple partida doble.");
            }
        }

        // Opcionales: fecha / cod_moneda / nro_asiento (contrato D6.7.3).
        var fechaEfectiva = cabecera.FechaAsiento;
        var idPeriodoEfectivo = cabecera.IdPeriodo;
        var idMonedaEfectiva = cabecera.IdMoneda;
        double? nroAsientoNuevo = null;

        if (parameters.ContainsKey("fecha")
            && !string.IsNullOrWhiteSpace(ExtractString(parameters, "fecha")))
        {
            var fechaNorm = NormalizeDate(ExtractString(parameters, "fecha"));
            if (string.IsNullOrWhiteSpace(fechaNorm))
            {
                return Fail("INVALID_PARAMETERS", "fecha invalida.");
            }

            var ep = await AsientosContablesCreateRunner
                .ResolveEjercicioPeriodoForUpdateAsync(
                    connection, transaction, fechaNorm!, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            fechaEfectiva = fechaNorm!;
            idPeriodoEfectivo = ep.IdPeriodo;
            cabecera = cabecera with
            {
                FechaAsiento = fechaEfectiva,
                IdPeriodo = idPeriodoEfectivo,
                TipoNumeracion = ep.TipoNumeracion,
                IdEjercicio = ep.IdEjercicio
            };
        }

        if (parameters.ContainsKey("cod_moneda")
            && !string.IsNullOrWhiteSpace(ExtractString(parameters, "cod_moneda")))
        {
            idMonedaEfectiva = await ResolveMonedaIdAsync(
                    connection,
                    transaction,
                    ExtractString(parameters, "cod_moneda")!.Trim(),
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (parameters.ContainsKey("nro_asiento") && ExtractDouble(parameters, "nro_asiento") is { } nroInf)
        {
            var existe = await ExisteNroAsientoExcluyendoAsync(
                    connection,
                    transaction,
                    nroInf,
                    cabecera.TipoNumeracion,
                    fechaEfectiva,
                    cabecera.IdEjercicio,
                    idPeriodoEfectivo,
                    cabecera.IdAsiento,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existe)
            {
                return Fail("CONFLICT", "El nroAsiento ya existe según el criterio de numeración.");
            }

            nroAsientoNuevo = nroInf;
        }

        if (renglones is not null)
        {
            await AsientosContablesDeleteRunner
                .DeleteHijosAsync(
                    connection, transaction, cabecera.IdAsiento, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            await AsientosContablesCreateRunner
                .PersistRenglonesAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    cabecera.IdAsiento,
                    fechaEfectiva,
                    idMonedaEfectiva,
                    renglones,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var usuarioRaw = ExtractString(parameters, "usuario")?.Trim();
        var usuario = string.IsNullOrWhiteSpace(usuarioRaw) ? "api" : Truncate(usuarioRaw, 120)!;
        var now = DateTime.Now;

        var setParts = new List<string>
        {
            "USUARIO_ULTIMA_MODIFICACION = @usuario",
            "FECHA_ULTIMA_MODIFICACION = @fechaMod",
            "TERMINAL_ULTIMA_MODIFICACION = @terminal",
            "ESTADO_ASIENTO_ANALITICO = @estado"
        };

        await using var upd = CreateCommand(connection, transaction, timeoutSeconds, "SELECT 1");
        upd.Parameters.AddWithValue("@usuario", usuario);
        upd.Parameters.AddWithValue("@fechaMod", now);
        upd.Parameters.AddWithValue("@terminal", TerminalModificacion);
        upd.Parameters.AddWithValue("@estado", estadoNuevo);

        if (parameters.ContainsKey("leyenda"))
        {
            setParts.Add("DESC_ASIENTO_ANALITICO = @leyenda");
            upd.Parameters.AddWithValue(
                "@leyenda",
                (object?)Truncate(ExtractString(parameters, "leyenda"), 100) ?? DBNull.Value);
        }

        if (parameters.ContainsKey("observaciones"))
        {
            setParts.Add("OBSERVACIONES = @obs");
            upd.Parameters.AddWithValue(
                "@obs",
                (object?)Truncate(ExtractString(parameters, "observaciones"), 1000) ?? DBNull.Value);
        }

        if (!string.Equals(fechaEfectiva, cabecera.FechaAsientoOriginal, StringComparison.Ordinal)
            || idPeriodoEfectivo != cabecera.IdPeriodoOriginal)
        {
            setParts.Add("FECHA_ASIENTO = @fecha");
            setParts.Add("ID_PERIODO = @idPeriodo");
            upd.Parameters.AddWithValue("@fecha", fechaEfectiva);
            upd.Parameters.AddWithValue("@idPeriodo", idPeriodoEfectivo);
        }

        if (idMonedaEfectiva != cabecera.IdMoneda)
        {
            setParts.Add("ID_MONEDA_ASIENTO = @idMoneda");
            upd.Parameters.AddWithValue("@idMoneda", idMonedaEfectiva);
        }

        if (nroAsientoNuevo is not null)
        {
            setParts.Add("NRO_ASIENTO_ANALITICO = @nroAsiento");
            upd.Parameters.AddWithValue("@nroAsiento", nroAsientoNuevo.Value);
        }

        upd.CommandText = $"""
            UPDATE dbo.ASIENTO_ANALITICO_CN
            SET {string.Join(", ", setParts)}
            WHERE ID_ASIENTO_ANALITICO_CN = @id
            """;
        upd.Parameters.AddWithValue("@id", cabecera.IdAsiento);
        await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new Dictionary<string, object?> { ["patched"] = true });
    }

    private sealed record CabeceraInfo(
        int IdAsiento,
        string Estado,
        string FechaAsiento,
        string FechaAsientoOriginal,
        int IdPeriodo,
        int IdPeriodoOriginal,
        int IdMoneda,
        int IdEjercicio,
        string TipoNumeracion,
        string EditaEstado,
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
                      CAST(a.ID_ASIENTO_ANALITICO_CN AS INT),
                      LTRIM(RTRIM(CAST(ISNULL(a.ESTADO_ASIENTO_ANALITICO, '') AS NVARCHAR(50)))),
                      CONVERT(VARCHAR(10), a.FECHA_ASIENTO, 23),
                      CAST(a.ID_PERIODO AS INT),
                      CAST(a.ID_MONEDA_ASIENTO AS INT),
                      CAST(ISNULL(p.ID_EJERCICIO, 0) AS INT),
                      LTRIM(RTRIM(CAST(ISNULL(e.TIPO_NUMERACION_ASIENTOS, N'Por ejercicio') AS NVARCHAR(50)))),
                      LTRIM(RTRIM(CAST(ISNULL(t.EDITA_ESTADO_ASIENTOS, N'N') AS NVARCHAR(5)))),
                      a.ROW_VERSION
                  FROM dbo.ASIENTO_ANALITICO_CN a
                  LEFT JOIN dbo.PERIODO p ON p.ID_PERIODO = a.ID_PERIODO
                  LEFT JOIN dbo.EJERCICIO e ON e.ID_EJERCICIO = p.ID_EJERCICIO
                  LEFT JOIN dbo.TIPO_ASIENTO t ON t.ID_TIPO_ASIENTO = a.ID_TIPO_ASIENTO
                  WHERE a.NRO_INTERNO_ANALITICO = @nro
                  """
                : """
                  SELECT TOP 1
                      CAST(a.ID_ASIENTO_ANALITICO_CN AS INT),
                      LTRIM(RTRIM(CAST(ISNULL(a.ESTADO_ASIENTO_ANALITICO, '') AS NVARCHAR(50)))),
                      CONVERT(VARCHAR(10), a.FECHA_ASIENTO, 23),
                      CAST(a.ID_PERIODO AS INT),
                      CAST(a.ID_MONEDA_ASIENTO AS INT),
                      CAST(ISNULL(p.ID_EJERCICIO, 0) AS INT),
                      LTRIM(RTRIM(CAST(ISNULL(e.TIPO_NUMERACION_ASIENTOS, N'Por ejercicio') AS NVARCHAR(50)))),
                      LTRIM(RTRIM(CAST(ISNULL(t.EDITA_ESTADO_ASIENTOS, N'N') AS NVARCHAR(5))))
                  FROM dbo.ASIENTO_ANALITICO_CN a
                  LEFT JOIN dbo.PERIODO p ON p.ID_PERIODO = a.ID_PERIODO
                  LEFT JOIN dbo.EJERCICIO e ON e.ID_EJERCICIO = p.ID_EJERCICIO
                  LEFT JOIN dbo.TIPO_ASIENTO t ON t.ID_TIPO_ASIENTO = a.ID_TIPO_ASIENTO
                  WHERE a.NRO_INTERNO_ANALITICO = @nro
                  """);
        cmd.Parameters.AddWithValue("@nro", nroInterno);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var fecha = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        var idPeriodo = reader.GetInt32(3);
        byte[]? rv = null;
        if (hasRv && !reader.IsDBNull(8))
        {
            rv = (byte[])reader.GetValue(8);
        }

        return new CabeceraInfo(
            reader.GetInt32(0),
            reader.GetString(1),
            fecha,
            fecha,
            idPeriodo,
            idPeriodo,
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            rv);
    }

    private static async Task<List<AsientosContablesCreateRunner.RenglonInput>> LoadRenglonesBalanceAsync(
        SqlConnection c, SqlTransaction t, int idAsiento, int timeout, CancellationToken ct)
    {
        var list = new List<AsientosContablesCreateRunner.RenglonInput>();
        await using var cmd = CreateCommand(
            c, t, timeout,
            """
            SELECT
                CAST(ISNULL(r.NRO_RENGLON_ANALITICO, 0) AS INT),
                LTRIM(RTRIM(CAST(ISNULL(cu.COD_CUENTA, '') AS NVARCHAR(50)))),
                LTRIM(RTRIM(CAST(ISNULL(r.D_H, '') AS NVARCHAR(5)))),
                CAST(ISNULL(i.IMPORTE_RENGLON, 0) AS DECIMAL(18,4))
            FROM dbo.RENGLON_ANALITICO_CN r
            LEFT JOIN dbo.CUENTA cu ON cu.ID_CUENTA = r.ID_CUENTA
            LEFT JOIN dbo.RENGLON_IMPORTE_ANALITICO_CN i ON i.ID_RENGLON_ANALITICO_CN = r.ID_RENGLON_ANALITICO_CN
            WHERE r.ID_ASIENTO_ANALITICO_CN = @id
            """);
        cmd.Parameters.AddWithValue("@id", idAsiento);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new AsientosContablesCreateRunner.RenglonInput(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2).ToUpperInvariant(),
                reader.GetDecimal(3),
                null,
                new List<AsientosContablesCreateRunner.TipoAuxiliarInput>()));
        }

        return list;
    }

    private static async Task<int> ResolveMonedaIdAsync(
        SqlConnection c, SqlTransaction t, string codMoneda, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            c, t, timeout,
            "SELECT TOP 1 ID_MONEDA FROM dbo.MONEDA WHERE LTRIM(RTRIM(COD_MONEDA)) = @cod");
        cmd.Parameters.AddWithValue("@cod", codMoneda);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (o is null or DBNull)
        {
            throw new AsientosContablesCreateRunner.ValidationException("Moneda inexistente.");
        }

        return Convert.ToInt32(o);
    }

    private static async Task<bool> ExisteNroAsientoExcluyendoAsync(
        SqlConnection c,
        SqlTransaction t,
        double nroAsiento,
        string tipoNumeracion,
        string fecha,
        int idEjercicio,
        int idPeriodo,
        int idAsientoExcluir,
        int timeout,
        CancellationToken ct)
    {
        var (fromJoin, whereExtra) = tipoNumeracion switch
        {
            AsientosContablesCreateRunner.PorEjercicio => (
                "INNER JOIN dbo.PERIODO p ON p.ID_PERIODO = a.ID_PERIODO",
                "AND p.ID_EJERCICIO = @idEjercicio"),
            AsientosContablesCreateRunner.PorDia => (string.Empty, "AND CAST(a.FECHA_ASIENTO AS DATE) = @fecha"),
            AsientosContablesCreateRunner.PorPeriodo => (string.Empty, "AND a.ID_PERIODO = @idPeriodo"),
            _ => (string.Empty, string.Empty)
        };

        var sql = $"""
            SELECT TOP 1 1
            FROM dbo.ASIENTO_ANALITICO_CN a
            {fromJoin}
            WHERE a.NRO_ASIENTO_ANALITICO = @nro
              AND a.ID_ASIENTO_ANALITICO_CN <> @idExcluir
            {whereExtra}
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@nro", nroAsiento);
        cmd.Parameters.AddWithValue("@idExcluir", idAsientoExcluir);
        switch (tipoNumeracion)
        {
            case AsientosContablesCreateRunner.PorEjercicio:
                cmd.Parameters.AddWithValue("@idEjercicio", idEjercicio);
                break;
            case AsientosContablesCreateRunner.PorDia:
                cmd.Parameters.AddWithValue("@fecha", fecha);
                break;
            case AsientosContablesCreateRunner.PorPeriodo:
                cmd.Parameters.AddWithValue("@idPeriodo", idPeriodo);
                break;
        }

        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null and not DBNull;
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

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            || DateTime.TryParse(value, out dt))
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : (value.Length <= max ? value : value[..max]);

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

    private static double? ExtractDouble(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return ConvertValue(raw) switch
        {
            null => null,
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            string s when string.IsNullOrWhiteSpace(s) => null,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
                parsed,
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
