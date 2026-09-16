using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.MovimientosTesoreria;

/// <summary>
/// D6.6.3 — reversión MovimientosTesoreria orquestada (espejo PHP MovimientosTesoreriaReversionService).
/// </summary>
public sealed class MovimientosTesoreriaReversionRunner
{
    public async Task<MovimientosTesoreriaOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var codComp = ExtractString(parameters, "cod_comp")?.Trim().ToUpperInvariant();
        var nCompRaw = ExtractString(parameters, "n_comp");
        var rowVersion = ExtractString(parameters, "row_version")?.Trim();

        if (string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(codComp)
            || string.IsNullOrWhiteSpace(nCompRaw)
            || string.IsNullOrWhiteSpace(rowVersion))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "cod_comp, n_comp, row_version y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new MovimientosTesoreriaOutcome
            {
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        var barra = ExtractInt(parameters, "barra") ?? 0;
        var concepto = Truncate(ExtractString(parameters, "concepto"), 40);
        var observaciones = ExtractString(parameters, "observaciones");
        var usuarioRaw = ExtractString(parameters, "usuario")?.Trim();
        var usuario = string.IsNullOrWhiteSpace(usuarioRaw)
            ? "api"
            : Truncate(usuarioRaw, 120)!;

        string nComp;
        try
        {
            nComp = MovimientosTesoreriaCreateRunner.NormalizeNComp(nCompRaw);
        }
        catch (MovimientosTesoreriaCreateRunner.ValidationException vex)
        {
            return Fail("INVALID_PARAMETERS", vex.Message);
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
                var outcome = await ExecuteReversionAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        codComp,
                        nComp,
                        barra,
                        rowVersion,
                        concepto,
                        observaciones,
                        usuario,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.Status == JobStatuses.Success)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }

                return outcome;
            }
            catch (MovimientosTesoreriaCreateRunner.ValidationException vex)
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
        catch (Exception ex)
        {
            return Fail("SQL_ERROR", ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task<MovimientosTesoreriaOutcome> ExecuteReversionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string codComp,
        string nComp,
        int barra,
        string clientRowVersion,
        string? concepto,
        string? observaciones,
        string usuario,
        CancellationToken cancellationToken)
    {
        var origen = await LoadCabeceraAsync(
                connection, transaction, codComp, nComp, barra, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (origen is null)
        {
            return Fail("NOT_FOUND", "Movimiento de tesoreria no encontrado.");
        }

        var expectedRowVersion = MovimientosTesoreriaGatewayRunner.EncodeRowVersion(
            origen.IdSba04,
            origen.Situacion,
            origen.FechaUltimaModificacion,
            origen.HoraUltimaModificacion,
            origen.NInterno);
        if (!string.Equals(expectedRowVersion, clientRowVersion.Trim(), StringComparison.Ordinal))
        {
            return Fail("CONFLICT", "Conflicto de concurrencia (rowVersion)");
        }

        var situacion = origen.Situacion.Trim().ToUpperInvariant();
        if (situacion == "A")
        {
            return Fail("CONFLICT", "El comprobante ya está revertido");
        }

        if (situacion is not ("N" or "C"))
        {
            return Fail("VALIDATION", $"SITUACION={situacion} no admite reversión.");
        }

        if (origen.CodComp.Trim().ToUpperInvariant() == "REV")
        {
            return Fail("VALIDATION", "El comprobante REV es terminal.");
        }

        var tipoRev = await MovimientosTesoreriaCreateRunner
            .ResolveTipoAsync(connection, transaction, "REV", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (tipoRev is null)
        {
            return Fail("VALIDATION", "No existe SBA02.COD_COMP=REV.");
        }

        var renglonesOri = await LoadRenglonesAsync(
                connection, transaction, origen.IdSba04, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (renglonesOri.Count == 0)
        {
            return Fail("VALIDATION", "El comprobante origen no tiene renglones");
        }

        var ahora = DateTime.Now;
        var nCompRev = await MovimientosTesoreriaCreateRunner
            .NextNCompAsync(
                connection, transaction, "REV", tipoRev.TipoNumer, tipoRev.Edita, null,
                timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var nInternoRev = await MovimientosTesoreriaCreateRunner
            .NextNInternoAsync(connection, transaction, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        const int barraRev = 0;

        var conceptoFinal = concepto
            ?? Truncate($"REV {origen.CodComp.Trim()} {origen.NComp.Trim()}", 40);
        var claseRev = tipoRev.Clase > 0 ? tipoRev.Clase : origen.Clase;

        var idSba04Rev = await InsertCabeceraRevAsync(
                connection,
                transaction,
                timeoutSeconds,
                tipoRev,
                claseRev,
                nCompRev,
                nInternoRev,
                barraRev,
                conceptoFinal,
                observaciones,
                origen,
                ahora,
                usuario,
                cancellationToken)
            .ConfigureAwait(false);

        var asientoCuentas = new List<(MovimientosTesoreriaCreateRunner.RenglonInput Renglon,
            MovimientosTesoreriaCreateRunner.CuentaInfo Cuenta)>();

        foreach (var r in renglonesOri)
        {
            var dHOri = r.DH.Trim().ToUpperInvariant();
            var dHRev = dHOri == "D" ? "H" : "D";
            var claseLinea = tipoRev.Clase > 0 ? tipoRev.Clase : (r.Clase > 0 ? r.Clase : origen.Clase);

            await InsertRenglonRevAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    barraRev,
                    claseLinea,
                    tipoRev.IdSba02,
                    nCompRev,
                    idSba04Rev,
                    ahora.Date,
                    r,
                    dHRev,
                    origen.Cotizacion,
                    cancellationToken)
                .ConfigureAwait(false);

            var signo = dHRev == "D" ? 1m : -1m;
            await MovimientosTesoreriaCreateRunner
                .UpdateSaldoAsync(
                    connection, transaction, r.CodCta, signo * r.Monto, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            var idSba01 = r.IdSba01;
            MovimientosTesoreriaCreateRunner.CuentaInfo? cuenta = null;
            if (idSba01 <= 0)
            {
                cuenta = await MovimientosTesoreriaCreateRunner
                    .ResolveCuentaAsync(connection, transaction, r.CodCta, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
                if (cuenta is null)
                {
                    return Fail("VALIDATION", $"No existe SBA01.COD_CTA={r.CodCta}.");
                }

                idSba01 = cuenta.IdSba01;
            }

            cuenta ??= new MovimientosTesoreriaCreateRunner.CuentaInfo(
                r.CodCta, string.Empty, null, idSba01, 0m);

            asientoCuentas.Add((
                new MovimientosTesoreriaCreateRunner.RenglonInput(
                    r.Renglon, r.CodCta, dHRev, r.Monto, r.Leyenda),
                cuenta));
        }

        await InsertVinculoSba27Async(
                connection,
                transaction,
                timeoutSeconds,
                origen,
                tipoRev.IdSba02,
                nCompRev,
                barraRev,
                cancellationToken)
            .ConfigureAwait(false);

        await UpdateOrigenSituacionAsync(
                connection,
                transaction,
                timeoutSeconds,
                origen.IdSba04,
                ahora,
                usuario,
                cancellationToken)
            .ConfigureAwait(false);

        if (tipoRev.AfectaCon)
        {
            await MovimientosTesoreriaCreateRunner
                .GenerarAsientoSiCorrespondeAsync(
                    connection, transaction, timeoutSeconds, nInternoRev, asientoCuentas, cancellationToken)
                .ConfigureAwait(false);
        }

        var cabeceraRev = await LoadCabeceraAsync(
                connection, transaction, "REV", nCompRev, barraRev, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (cabeceraRev is null)
        {
            return Fail("VALIDATION", "No se pudo recuperar el REV generado");
        }

        return Ok(BuildResumen(cabeceraRev, origen));
    }

    private static async Task<OrigenCabecera?> LoadCabeceraAsync(
        SqlConnection c, SqlTransaction t, string codComp, string nComp, int barra, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1
                COD_COMP, N_COMP, BARRA, N_INTERNO, ID_SBA04, CLASE, SITUACION, EXTERNO,
                COTIZACION, FECHA, ID_SBA02, COD_GVA14, COD_CPA01,
                TOTAL_IMPORTE_CTE, TOTAL_IMPORTE_EXT, CONCEPTO,
                FECHA_ULTIMA_MODIFICACION, HORA_ULTIMA_MODIFICACION
            FROM SBA04
            WHERE COD_COMP = @codComp AND N_COMP = @nComp AND BARRA = @barra
            """;
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codComp", codComp);
        cmd.Parameters.AddWithValue("@nComp", nComp);
        cmd.Parameters.AddWithValue("@barra", barra);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new OrigenCabecera(
            reader.GetString(reader.GetOrdinal("COD_COMP")).Trim(),
            reader.GetString(reader.GetOrdinal("N_COMP")),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("BARRA"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("N_INTERNO"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_SBA04"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("CLASE"))),
            (MovimientosTesoreriaCreateRunner.ReadString(reader, "SITUACION") ?? string.Empty).Trim(),
            MovimientosTesoreriaCreateRunner.ReadBool(reader, "EXTERNO"),
            Convert.ToDecimal(
                reader.IsDBNull(reader.GetOrdinal("COTIZACION"))
                    ? 1m
                    : reader.GetValue(reader.GetOrdinal("COTIZACION"))),
            reader.IsDBNull(reader.GetOrdinal("FECHA"))
                ? null
                : Convert.ToDateTime(reader.GetValue(reader.GetOrdinal("FECHA"))),
            reader.IsDBNull(reader.GetOrdinal("ID_SBA02"))
                ? 0
                : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_SBA02"))),
            MovimientosTesoreriaCreateRunner.ReadString(reader, "COD_GVA14"),
            MovimientosTesoreriaCreateRunner.ReadString(reader, "COD_CPA01"),
            reader.IsDBNull(reader.GetOrdinal("TOTAL_IMPORTE_CTE"))
                ? null
                : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("TOTAL_IMPORTE_CTE"))),
            reader.IsDBNull(reader.GetOrdinal("TOTAL_IMPORTE_EXT"))
                ? null
                : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("TOTAL_IMPORTE_EXT"))),
            MovimientosTesoreriaCreateRunner.FormatDateTime(reader, "FECHA_ULTIMA_MODIFICACION"),
            (MovimientosTesoreriaCreateRunner.ReadString(reader, "HORA_ULTIMA_MODIFICACION") ?? string.Empty)
                .Trim());
    }

    private static async Task<List<RenglonOrigen>> LoadRenglonesAsync(
        SqlConnection c, SqlTransaction t, int idSba04, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT RENGLON, COD_CTA, D_H, MONTO, LEYENDA, CLASE, UNIDADES, COTIZ_MONE,
                   ID_SBA01, EFECTIVO, CHEQUES
            FROM SBA05
            WHERE ID_SBA04 = @idSba04
            ORDER BY RENGLON
            """;
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@idSba04", idSba04);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<RenglonOrigen>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new RenglonOrigen(
                Convert.ToInt32(reader.GetValue(reader.GetOrdinal("RENGLON"))),
                Convert.ToInt32(reader.GetValue(reader.GetOrdinal("COD_CTA"))),
                (MovimientosTesoreriaCreateRunner.ReadString(reader, "D_H") ?? string.Empty).Trim(),
                Convert.ToDecimal(reader.IsDBNull(reader.GetOrdinal("MONTO"))
                    ? 0m
                    : reader.GetValue(reader.GetOrdinal("MONTO"))),
                Truncate(MovimientosTesoreriaCreateRunner.ReadString(reader, "LEYENDA"), 40),
                reader.IsDBNull(reader.GetOrdinal("CLASE"))
                    ? 0
                    : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("CLASE"))),
                reader.IsDBNull(reader.GetOrdinal("UNIDADES"))
                    ? null
                    : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("UNIDADES"))),
                reader.IsDBNull(reader.GetOrdinal("COTIZ_MONE"))
                    ? null
                    : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("COTIZ_MONE"))),
                reader.IsDBNull(reader.GetOrdinal("ID_SBA01"))
                    ? 0
                    : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_SBA01"))),
                reader.IsDBNull(reader.GetOrdinal("EFECTIVO"))
                    ? 0m
                    : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("EFECTIVO"))),
                reader.IsDBNull(reader.GetOrdinal("CHEQUES"))
                    ? 0m
                    : Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("CHEQUES")))));
        }

        return list;
    }

    private static async Task<int> InsertCabeceraRevAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        MovimientosTesoreriaCreateRunner.TipoComprobante tipoRev,
        int claseRev,
        string nCompRev,
        int nInternoRev,
        int barraRev,
        string? concepto,
        string? observaciones,
        OrigenCabecera origen,
        DateTime ahora,
        string usuario,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO SBA04 (
                BARRA, CERRADO, CLASE, COD_COMP, CONCEPTO, COTIZACION, EXPORTADO, EXTERNO,
                FECHA, FECHA_ING, HORA_ING, N_COMP, N_INTERNO, PASE, SITUACION, TERMINAL, USUARIO,
                FECHA_ORI, C_COMP_ORI, N_COMP_ORI, BARRA_ORI, FECHA_EMIS, GENERA_ASIENTO,
                FECHA_ULTIMA_MODIFICACION, HORA_ULTIMA_MODIFICACION,
                USUA_ULTIMA_MODIFICACION, TERM_ULTIMA_MODIFICACION,
                ID_SBA02, ID_SBA02_C_COMP_ORI, COD_GVA14, COD_CPA01, OBSERVACIONES,
                TOTAL_IMPORTE_CTE, TOTAL_IMPORTE_EXT)
            OUTPUT INSERTED.ID_SBA04
            VALUES (
                @barra, 0, @clase, N'REV', @concepto, @cotizacion, 0, @externo,
                @fecha, @fechaIng, @horaIng, @nComp, @nInterno, 0, N'N', @terminal, @usuario,
                @fechaOri, @cCompOri, @nCompOri, @barraOri, @fechaEmis, @generaAsiento,
                @fechaUlt, @horaUlt, @usuario, @terminal,
                @idSba02, @idSba02Ori, @codClient, @codProvee, @observaciones,
                @totalCte, @totalExt)
            """;
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@barra", barraRev);
        cmd.Parameters.AddWithValue("@clase", claseRev);
        cmd.Parameters.AddWithValue("@concepto", (object?)concepto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cotizacion", origen.Cotizacion <= 0m ? 1m : origen.Cotizacion);
        cmd.Parameters.AddWithValue("@externo", origen.Externo ? 1 : 0);
        cmd.Parameters.AddWithValue("@fecha", ahora.Date);
        cmd.Parameters.AddWithValue("@fechaIng", ahora);
        cmd.Parameters.AddWithValue("@horaIng", ahora.ToString("HHmmss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@nComp", nCompRev);
        cmd.Parameters.AddWithValue("@nInterno", nInternoRev);
        cmd.Parameters.AddWithValue("@terminal", MovimientosTesoreriaCreateRunner.TerminalIngreso);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@fechaOri", (object?)origen.Fecha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cCompOri", origen.CodComp);
        cmd.Parameters.AddWithValue("@nCompOri", origen.NComp);
        cmd.Parameters.AddWithValue("@barraOri", origen.Barra);
        cmd.Parameters.AddWithValue("@fechaEmis", ahora.Date);
        cmd.Parameters.AddWithValue("@generaAsiento", tipoRev.AfectaCon ? "S" : "N");
        cmd.Parameters.AddWithValue("@fechaUlt", ahora);
        cmd.Parameters.AddWithValue("@horaUlt", ahora.ToString("HHmmss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@idSba02", tipoRev.IdSba02);
        cmd.Parameters.AddWithValue("@idSba02Ori", origen.IdSba02);
        cmd.Parameters.AddWithValue(
            "@codClient",
            string.IsNullOrWhiteSpace(origen.CodClient) ? DBNull.Value : origen.CodClient);
        cmd.Parameters.AddWithValue(
            "@codProvee",
            string.IsNullOrWhiteSpace(origen.CodProvee) ? DBNull.Value : origen.CodProvee);
        cmd.Parameters.AddWithValue("@observaciones", (object?)observaciones ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@totalCte", (object?)origen.TotalImporteCte ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@totalExt", (object?)origen.TotalImporteExt ?? DBNull.Value);
        var id = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(id);
    }

    private static async Task InsertRenglonRevAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int barraRev,
        int clase,
        int idSba02,
        string nCompRev,
        int idSba04Rev,
        DateTime fecha,
        RenglonOrigen r,
        string dHRev,
        decimal cotizacionOrigen,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO SBA05 (
                BARRA, CLASE, COD_COMP, COD_CTA, D_H, FECHA, LEYENDA, MONTO, N_COMP, RENGLON,
                UNIDADES, COTIZ_MONE, ID_SBA02, ID_SBA04, ID_SBA01, CONCILIADO, EFECTIVO, CHEQUES)
            VALUES (
                @barra, @clase, N'REV', @codCta, @dH, @fecha, @leyenda, @monto, @nComp, @renglon,
                @unidades, @cotiz, @idSba02, @idSba04, @idSba01, 0, @efectivo, @cheques)
            """;
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@barra", barraRev);
        cmd.Parameters.AddWithValue("@clase", clase);
        cmd.Parameters.AddWithValue("@codCta", r.CodCta);
        cmd.Parameters.AddWithValue("@dH", dHRev);
        cmd.Parameters.AddWithValue("@fecha", fecha);
        cmd.Parameters.AddWithValue("@leyenda", (object?)r.Leyenda ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@monto", r.Monto);
        cmd.Parameters.AddWithValue("@nComp", nCompRev);
        cmd.Parameters.AddWithValue("@renglon", r.Renglon);
        cmd.Parameters.AddWithValue("@unidades", r.Unidades ?? r.Monto);
        cmd.Parameters.AddWithValue(
            "@cotiz",
            r.CotizMone ?? (cotizacionOrigen <= 0m ? 1m : cotizacionOrigen));
        cmd.Parameters.AddWithValue("@idSba02", idSba02);
        cmd.Parameters.AddWithValue("@idSba04", idSba04Rev);
        cmd.Parameters.AddWithValue("@idSba01", r.IdSba01 > 0 ? r.IdSba01 : DBNull.Value);
        cmd.Parameters.AddWithValue("@efectivo", r.Efectivo);
        cmd.Parameters.AddWithValue("@cheques", r.Cheques);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertVinculoSba27Async(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        OrigenCabecera origen,
        int idSba02Rev,
        string nCompRev,
        int barraRev,
        CancellationToken ct)
    {
        if (!await MovimientosTesoreriaCreateRunner
                .TableExistsAsync(c, t, "SBA27", timeout, ct)
                .ConfigureAwait(false))
        {
            return;
        }

        var tCompOri = await ResolveSba27CompColumnAsync(c, t, timeout, "T_COMP_ORI", "COD_COMP_ORI", ct)
            .ConfigureAwait(false);
        var tCompRev = await ResolveSba27CompColumnAsync(c, t, timeout, "T_COMP_REV", "COD_COMP_REV", ct)
            .ConfigureAwait(false);
        if (tCompOri is null || tCompRev is null)
        {
            return;
        }

        var sql = $"""
            INSERT INTO SBA27 (
                [{tCompOri}], N_COMP_ORI, BARRA_ORI,
                [{tCompRev}], N_COMP_REV, BARRA_REV,
                ID_SBA02_ORI, ID_SBA02_REV)
            VALUES (
                @codOri, @nCompOri, @barraOri,
                N'REV', @nCompRev, @barraRev,
                @idSba02Ori, @idSba02Rev)
            """;
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codOri", origen.CodComp);
        cmd.Parameters.AddWithValue("@nCompOri", origen.NComp);
        cmd.Parameters.AddWithValue("@barraOri", origen.Barra);
        cmd.Parameters.AddWithValue("@nCompRev", nCompRev);
        cmd.Parameters.AddWithValue("@barraRev", barraRev);
        cmd.Parameters.AddWithValue("@idSba02Ori", origen.IdSba02);
        cmd.Parameters.AddWithValue("@idSba02Rev", idSba02Rev);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string?> ResolveSba27CompColumnAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        string preferred,
        string fallback,
        CancellationToken ct)
    {
        if (await ColumnExistsAsync(c, t, "SBA27", preferred, timeout, ct).ConfigureAwait(false))
        {
            return preferred;
        }

        if (await ColumnExistsAsync(c, t, "SBA27", fallback, timeout, ct).ConfigureAwait(false))
        {
            return fallback;
        }

        return null;
    }

    private static async Task UpdateOrigenSituacionAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idSba04,
        DateTime ahora,
        string usuario,
        CancellationToken ct)
    {
        const string sql = """
            UPDATE SBA04
            SET SITUACION = N'A',
                FECHA_ULTIMA_MODIFICACION = @fechaUlt,
                HORA_ULTIMA_MODIFICACION = @horaUlt,
                USUA_ULTIMA_MODIFICACION = @usuario,
                TERM_ULTIMA_MODIFICACION = @terminal
            WHERE ID_SBA04 = @idSba04
            """;
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@fechaUlt", ahora);
        cmd.Parameters.AddWithValue("@horaUlt", ahora.ToString("HHmmss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@terminal", MovimientosTesoreriaCreateRunner.TerminalIngreso);
        cmd.Parameters.AddWithValue("@idSba04", idSba04);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static Dictionary<string, object?> BuildResumen(OrigenCabecera rev, OrigenCabecera origen) =>
        new(StringComparer.Ordinal)
        {
            ["codComp"] = rev.CodComp,
            ["nComp"] = rev.NComp,
            ["barra"] = rev.Barra,
            ["nInterno"] = rev.NInterno,
            ["idSba04"] = rev.IdSba04,
            ["clase"] = rev.Clase,
            ["situacion"] = rev.Situacion,
            ["externo"] = rev.Externo,
            ["rowVersion"] = MovimientosTesoreriaGatewayRunner.EncodeRowVersion(
                rev.IdSba04,
                rev.Situacion,
                rev.FechaUltimaModificacion,
                rev.HoraUltimaModificacion,
                rev.NInterno),
            ["reversionDe"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["codComp"] = origen.CodComp,
                ["nComp"] = origen.NComp,
                ["barra"] = origen.Barra
            }
        };

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = MovimientosTesoreriaCreateRunner.CreateCommand(
            c,
            t,
            timeout,
            "SELECT 1 FROM sys.columns sc INNER JOIN sys.tables st ON st.object_id=sc.object_id WHERE st.name=@t AND sc.name=@c");
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
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

    private static MovimientosTesoreriaOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static MovimientosTesoreriaOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };

    private sealed record OrigenCabecera(
        string CodComp,
        string NComp,
        int Barra,
        int NInterno,
        int IdSba04,
        int Clase,
        string Situacion,
        bool Externo,
        decimal Cotizacion,
        DateTime? Fecha,
        int IdSba02,
        string? CodClient,
        string? CodProvee,
        decimal? TotalImporteCte,
        decimal? TotalImporteExt,
        string FechaUltimaModificacion,
        string HoraUltimaModificacion);

    private sealed record RenglonOrigen(
        int Renglon,
        int CodCta,
        string DH,
        decimal Monto,
        string? Leyenda,
        int Clase,
        decimal? Unidades,
        decimal? CotizMone,
        int IdSba01,
        decimal Efectivo,
        decimal Cheques);
}
