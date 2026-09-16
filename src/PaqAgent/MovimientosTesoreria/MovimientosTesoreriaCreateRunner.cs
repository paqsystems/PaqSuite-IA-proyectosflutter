using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.MovimientosTesoreria;

/// <summary>
/// D6.6.2 — alta MovimientosTesoreria orquestada en agente (espejo PHP MovimientosTesoreriaAltaService).
/// </summary>
public sealed class MovimientosTesoreriaCreateRunner
{
    public const string TerminalIngreso = "Api-PaqSuiteWeb";

    public async Task<MovimientosTesoreriaOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var codComp = ExtractString(parameters, "cod_comp")?.Trim().ToUpperInvariant();
        var renglonesJson = ExtractString(parameters, "renglones_json");

        if (string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(codComp)
            || string.IsNullOrWhiteSpace(renglonesJson))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "cod_comp, renglones_json y _database son obligatorios.");
        }

        if (codComp.Length != 3)
        {
            return Fail("INVALID_PARAMETERS", "cod_comp debe tener 3 caracteres.");
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

        List<RenglonInput> renglones;
        try
        {
            renglones = ParseRenglones(renglonesJson);
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", "renglones_json invalido: " + ex.Message);
        }

        CotizacionInput? cotizacion;
        try
        {
            cotizacion = ParseCotizacion(ExtractString(parameters, "cotizacion_json"));
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", "cotizacion_json invalido: " + ex.Message);
        }

        var asientoJson = ExtractString(parameters, "asiento_json");
        if (!string.IsNullOrWhiteSpace(asientoJson) && asientoJson.Trim() is not ("[]" or "null" or "{}"))
        {
            try
            {
                using var doc = JsonDocument.Parse(asientoJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    return Fail("VALIDATION", "En v1 el asiento se genera automaticamente; no enviar asiento[].");
                }
            }
            catch
            {
                return Fail("VALIDATION", "En v1 el asiento se genera automaticamente; no enviar asiento[].");
            }
        }

        var structuralError = ValidateRenglonesEstructura(renglones);
        if (structuralError is not null)
        {
            return Fail("VALIDATION", structuralError);
        }

        var barra = ExtractInt(parameters, "barra") ?? 0;
        var forceExterno = ExtractBool(parameters, "force_externo") ?? false;
        var externo = forceExterno || (ExtractBool(parameters, "externo") ?? false);
        var fecha = NormalizeDate(ExtractString(parameters, "fecha")) ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fechaEmis = NormalizeDate(ExtractString(parameters, "fecha_emis")) ?? fecha;
        var concepto = Truncate(ExtractString(parameters, "concepto"), 40);
        var observaciones = ExtractString(parameters, "observaciones");
        var codClient = Truncate(ExtractString(parameters, "cod_client")?.Trim(), 10);
        var codProvee = Truncate(ExtractString(parameters, "cod_provee")?.Trim(), 10);
        var usuarioRaw = ExtractString(parameters, "usuario")?.Trim();
        var usuario = string.IsNullOrWhiteSpace(usuarioRaw)
            ? "api"
            : Truncate(usuarioRaw, 120)!;
        var nCompInformado = ExtractString(parameters, "n_comp");
        if (string.IsNullOrWhiteSpace(nCompInformado))
        {
            nCompInformado = null;
        }

        var claseBody = ExtractInt(parameters, "clase");
        var cotizacionValor = cotizacion?.Cotizacion ?? 1m;
        if (cotizacionValor <= 0m)
        {
            cotizacionValor = 1m;
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
                var outcome = await ExecuteCreateAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        codComp,
                        barra,
                        nCompInformado,
                        fecha,
                        fechaEmis,
                        concepto,
                        observaciones,
                        codClient,
                        codProvee,
                        externo,
                        usuario,
                        cotizacion,
                        cotizacionValor,
                        claseBody,
                        renglones,
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
            catch (ValidationException vex)
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

    private static async Task<MovimientosTesoreriaOutcome> ExecuteCreateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string codComp,
        int barra,
        string? nCompInformado,
        string fecha,
        string fechaEmis,
        string? concepto,
        string? observaciones,
        string? codClient,
        string? codProvee,
        bool externo,
        string usuario,
        CotizacionInput? cotizacion,
        decimal cotizacionValor,
        int? claseBody,
        IReadOnlyList<RenglonInput> renglones,
        CancellationToken cancellationToken)
    {
        var tipo = await ResolveTipoAsync(connection, transaction, codComp, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (tipo is null)
        {
            return Fail("VALIDATION", $"No existe SBA02.COD_COMP={codComp}.");
        }

        if (claseBody is not null && claseBody.Value != tipo.Clase)
        {
            return Fail("VALIDATION", "La clase del body no coincide con SBA02.CLASE del tipo.");
        }

        if (!MovimientosTesoreriaMatrizClases.ClaseSoportada(tipo.Clase))
        {
            return Fail("VALIDATION", $"Clase {tipo.Clase} no soportada (Must-Have 1–9).");
        }

        var cuentas = new List<(RenglonInput Renglon, CuentaInfo Cuenta)>();
        foreach (var renglon in renglones)
        {
            var cuenta = await ResolveCuentaAsync(
                    connection, transaction, renglon.CodCta, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (cuenta is null)
            {
                return Fail("VALIDATION", $"No existe SBA01.COD_CTA={renglon.CodCta}.");
            }

            var rol = renglon.Renglon == 0
                ? MovimientosTesoreriaMatrizClases.RolPrincipal
                : MovimientosTesoreriaMatrizClases.RolFondos;

            if (!MovimientosTesoreriaMatrizClases.Admite(tipo.Clase, rol, renglon.DH, cuenta.Tipo, cuenta.CcCa)
                && renglon.Renglon != 0
                && MovimientosTesoreriaMatrizClases.Admite(
                    tipo.Clase,
                    MovimientosTesoreriaMatrizClases.RolOpcional,
                    renglon.DH,
                    cuenta.Tipo,
                    cuenta.CcCa))
            {
                rol = MovimientosTesoreriaMatrizClases.RolOpcional;
            }

            if (!MovimientosTesoreriaMatrizClases.Admite(tipo.Clase, rol, renglon.DH, cuenta.Tipo, cuenta.CcCa))
            {
                return Fail(
                    "VALIDATION",
                    $"Cuenta tipo={cuenta.Tipo} ccCa={cuenta.CcCa ?? "null"} dH={renglon.DH} no admitida como {rol} en clase {tipo.Clase}.");
            }

            cuentas.Add((renglon, cuenta));
        }

        string nComp;
        try
        {
            nComp = await NextNCompAsync(
                    connection, transaction, codComp, tipo.TipoNumer, tipo.Edita, nCompInformado,
                    timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }

        if (await CabeceraExisteAsync(connection, transaction, codComp, nComp, barra, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return Fail("CONFLICT", "Ya existe COD_COMP+N_COMP+BARRA.");
        }

        var nInterno = await NextNInternoAsync(connection, transaction, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var ahora = DateTime.Now;
        var conceptoFinal = concepto ?? Truncate(tipo.ConcepAsi, 40);
        var totalImporte = renglones.Where(r => r.DH == "D").Sum(r => r.Monto);

        var idSba04 = await InsertCabeceraAsync(
                connection,
                transaction,
                timeoutSeconds,
                barra,
                tipo,
                nComp,
                nInterno,
                conceptoFinal,
                cotizacionValor,
                externo,
                fecha,
                fechaEmis,
                ahora,
                usuario,
                codClient,
                codProvee,
                observaciones,
                totalImporte,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var (renglon, cuenta) in cuentas)
        {
            await InsertRenglonAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    barra,
                    tipo,
                    nComp,
                    idSba04,
                    fecha,
                    cotizacionValor,
                    renglon,
                    cuenta,
                    cancellationToken)
                .ConfigureAwait(false);

            var signo = renglon.DH == "D" ? 1m : -1m;
            await UpdateSaldoAsync(
                    connection, transaction, cuenta.CodCta, signo * renglon.Monto, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        if (tipo.AfectaCon)
        {
            await GenerarAsientoSiCorrespondeAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    nInterno,
                    cuentas,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (cotizacion is { IdMoneda: > 0 })
        {
            await InsertCotizacionSiCorrespondeAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    cotizacion,
                    cotizacionValor,
                    tipo.IdSba02,
                    nComp,
                    barra,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var cabecera = await ReloadCabeceraAsync(
                connection, transaction, codComp, nComp, barra, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (cabecera is null)
        {
            return Fail("VALIDATION", "No se pudo recuperar el movimiento creado.");
        }

        return Ok(BuildResumen(cabecera));
    }

    internal static List<RenglonInput> ParseRenglones(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Se esperaba un array JSON.");
        }

        var list = new List<RenglonInput>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var renglon = ReadIntProp(el, "renglon") ?? ReadIntProp(el, "Renglon") ?? -1;
            var codCta = ReadIntProp(el, "codCta") ?? ReadIntProp(el, "cod_cta") ?? 0;
            var dH = (ReadStringProp(el, "dH") ?? ReadStringProp(el, "d_h") ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
            var monto = ReadDecimalProp(el, "monto") ?? 0m;
            var leyenda = Truncate(ReadStringProp(el, "leyenda"), 40);
            list.Add(new RenglonInput(renglon, codCta, dH, monto, leyenda));
        }

        return list;
    }

    internal static string? ValidateRenglonesEstructura(IReadOnlyList<RenglonInput> renglones)
    {
        if (renglones.Count < 2)
        {
            return "Debe informar al menos dos renglones (partida doble).";
        }

        var principales = 0;
        var tieneFondos = false;
        decimal sumaD = 0m;
        decimal sumaH = 0m;

        foreach (var r in renglones)
        {
            if (r.Renglon < 0)
            {
                return "renglon es obligatorio (>= 0).";
            }

            if (r.DH is not ("D" or "H"))
            {
                return "dH debe ser D o H.";
            }

            if (r.Monto <= 0m)
            {
                return "monto debe ser > 0.";
            }

            if (r.Renglon == 0)
            {
                principales++;
            }
            else
            {
                tieneFondos = true;
            }

            if (r.DH == "D")
            {
                sumaD += r.Monto;
            }
            else
            {
                sumaH += r.Monto;
            }
        }

        if (principales != 1)
        {
            return "Debe haber exactamente un renglón con renglon=0 (cuenta principal).";
        }

        if (!tieneFondos)
        {
            return "Debe haber al menos un renglón de fondos (renglon >= 1).";
        }

        if (Math.Round(sumaD, 4) != Math.Round(sumaH, 4))
        {
            return $"Σ D ({sumaD}) debe igualar Σ H ({sumaH}).";
        }

        return null;
    }

    private static CotizacionInput? ParseCotizacion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var idMoneda = ReadIntProp(root, "idMoneda") ?? ReadIntProp(root, "id_moneda") ?? 0;
        var idTipo = ReadIntProp(root, "idTipoCotizacion") ?? ReadIntProp(root, "id_tipo_cotizacion");
        var cotiz = ReadDecimalProp(root, "cotizacion") ?? 1m;
        return new CotizacionInput(idMoneda, idTipo, cotiz);
    }

    internal static async Task<TipoComprobante?> ResolveTipoAsync(
        SqlConnection c, SqlTransaction t, string codComp, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 ID_SBA02, CLASE, TIPO_NUMER, EDITA, AFECTA_CON, CONCEP_ASI, COD_COMP
            FROM SBA02 WHERE COD_COMP = @codComp
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codComp", codComp);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new TipoComprobante(
            reader.GetInt32(reader.GetOrdinal("ID_SBA02")),
            reader.GetString(reader.GetOrdinal("COD_COMP")).Trim(),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("CLASE"))),
            (ReadString(reader, "TIPO_NUMER") ?? "A").Trim().ToUpperInvariant(),
            ReadBool(reader, "EDITA"),
            ReadBool(reader, "AFECTA_CON"),
            ReadString(reader, "CONCEP_ASI"));
    }

    internal static async Task<CuentaInfo?> ResolveCuentaAsync(
        SqlConnection c, SqlTransaction t, int codCta, int timeout, CancellationToken ct)
    {
        if (codCta <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT TOP 1 COD_CTA, TIPO, CC_CA, ID_SBA01, SALDO
            FROM SBA01
            WHERE COD_CTA = @codCta OR CAST(COD_CTA AS FLOAT) = @codCtaFloat
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codCta", codCta);
        cmd.Parameters.AddWithValue("@codCtaFloat", (double)codCta);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var ccCaRaw = ReadString(reader, "CC_CA");
        var ccCa = string.IsNullOrWhiteSpace(ccCaRaw) ? null : ccCaRaw.Trim().ToUpperInvariant();
        return new CuentaInfo(
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("COD_CTA"))),
            (ReadString(reader, "TIPO") ?? string.Empty).Trim().ToUpperInvariant(),
            ccCa,
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_SBA01"))),
            Convert.ToDecimal(reader.IsDBNull(reader.GetOrdinal("SALDO")) ? 0m : reader.GetValue(reader.GetOrdinal("SALDO"))));
    }

    internal static async Task<string> NextNCompAsync(
        SqlConnection c,
        SqlTransaction t,
        string codComp,
        string tipoNumer,
        bool edita,
        string? nCompInformado,
        int timeout,
        CancellationToken ct)
    {
        var informado = !string.IsNullOrWhiteSpace(nCompInformado);
        if (informado)
        {
            if (tipoNumer == "A" && !edita)
            {
                throw new ValidationException(
                    "SBA02.EDITA debe ser true para informar nComp con numeración automática.");
            }

            return NormalizeNComp(nCompInformado!);
        }

        if (tipoNumer == "M")
        {
            throw new ValidationException("El tipo de comprobante tiene numeración manual (TIPO_NUMER=M).");
        }

        const string sql = """
            SELECT MAX(TRY_CAST(LTRIM(RTRIM(N_COMP)) AS BIGINT)) AS max_n
            FROM SBA04 WHERE COD_COMP = @codComp
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codComp", codComp);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var next = (o is null or DBNull ? 0L : Convert.ToInt64(o)) + 1;
        if (next < 1)
        {
            next = 1;
        }

        return FormatNComp((int)next);
    }

    internal static string NormalizeNComp(string nComp)
    {
        var trimmed = nComp.Trim();
        if (trimmed.Length == 0)
        {
            throw new ValidationException("nComp no puede estar vacío.");
        }

        if (nComp.Length == 14)
        {
            return nComp;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            digits = "0";
        }

        return FormatNComp(int.Parse(digits, CultureInfo.InvariantCulture));
    }

    internal static string FormatNComp(int numero)
    {
        if (numero < 0)
        {
            numero = 0;
        }

        return " " + numero.ToString(CultureInfo.InvariantCulture).PadLeft(13, '0');
    }

    private static async Task<bool> CabeceraExisteAsync(
        SqlConnection c, SqlTransaction t, string codComp, string nComp, int barra, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 1 FROM SBA04
            WHERE COD_COMP = @codComp AND N_COMP = @nComp AND BARRA = @barra
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codComp", codComp);
        cmd.Parameters.AddWithValue("@nComp", nComp);
        cmd.Parameters.AddWithValue("@barra", barra);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    internal static async Task<int> NextNInternoAsync(
        SqlConnection c, SqlTransaction t, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, "SELECT MAX(N_INTERNO) FROM SBA04");
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var max = o is null or DBNull ? 0m : Convert.ToDecimal(o);
        return (int)(max + 1m);
    }

    private static async Task<int> InsertCabeceraAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int barra,
        TipoComprobante tipo,
        string nComp,
        int nInterno,
        string? concepto,
        decimal cotizacionValor,
        bool externo,
        string fecha,
        string fechaEmis,
        DateTime ahora,
        string usuario,
        string? codClient,
        string? codProvee,
        string? observaciones,
        decimal totalImporte,
        CancellationToken ct)
    {
        var totalExt = totalImporte / Math.Max(cotizacionValor, 0.0001m);
        const string sql = """
            INSERT INTO SBA04 (
                BARRA, CERRADO, CLASE, COD_COMP, CONCEPTO, COTIZACION, EXPORTADO, EXTERNO,
                FECHA, FECHA_ING, HORA_ING, N_COMP, N_INTERNO, PASE, SITUACION, TERMINAL, USUARIO,
                FECHA_EMIS, GENERA_ASIENTO, FECHA_ULTIMA_MODIFICACION, HORA_ULTIMA_MODIFICACION,
                USUA_ULTIMA_MODIFICACION, TERM_ULTIMA_MODIFICACION, ID_SBA02,
                COD_GVA14, COD_CPA01, OBSERVACIONES, TOTAL_IMPORTE_CTE, TOTAL_IMPORTE_EXT)
            OUTPUT INSERTED.ID_SBA04
            VALUES (
                @barra, 0, @clase, @codComp, @concepto, @cotizacion, 0, @externo,
                @fecha, @fechaIng, @horaIng, @nComp, @nInterno, 0, N'N', @terminal, @usuario,
                @fechaEmis, @generaAsiento, @fechaUlt, @horaUlt,
                @usuario, @terminal, @idSba02,
                @codClient, @codProvee, @observaciones, @totalCte, @totalExt)
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@barra", barra);
        cmd.Parameters.AddWithValue("@clase", tipo.Clase);
        cmd.Parameters.AddWithValue("@codComp", tipo.CodComp);
        cmd.Parameters.AddWithValue("@concepto", (object?)concepto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cotizacion", cotizacionValor);
        cmd.Parameters.AddWithValue("@externo", externo ? 1 : 0);
        cmd.Parameters.AddWithValue("@fecha", DateTime.Parse(fecha, CultureInfo.InvariantCulture).Date);
        cmd.Parameters.AddWithValue("@fechaIng", ahora);
        cmd.Parameters.AddWithValue("@horaIng", ahora.ToString("HHmmss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@nComp", nComp);
        cmd.Parameters.AddWithValue("@nInterno", nInterno);
        cmd.Parameters.AddWithValue("@terminal", TerminalIngreso);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@fechaEmis", DateTime.Parse(fechaEmis, CultureInfo.InvariantCulture).Date);
        cmd.Parameters.AddWithValue("@generaAsiento", tipo.AfectaCon ? "S" : "N");
        cmd.Parameters.AddWithValue("@fechaUlt", ahora);
        cmd.Parameters.AddWithValue("@horaUlt", ahora.ToString("HHmmss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@idSba02", tipo.IdSba02);
        cmd.Parameters.AddWithValue("@codClient", string.IsNullOrWhiteSpace(codClient) ? DBNull.Value : codClient);
        cmd.Parameters.AddWithValue("@codProvee", string.IsNullOrWhiteSpace(codProvee) ? DBNull.Value : codProvee);
        cmd.Parameters.AddWithValue("@observaciones", (object?)observaciones ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@totalCte", totalImporte);
        cmd.Parameters.AddWithValue("@totalExt", totalExt);
        var id = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(id);
    }

    private static async Task InsertRenglonAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int barra,
        TipoComprobante tipo,
        string nComp,
        int idSba04,
        string fecha,
        decimal cotizacionValor,
        RenglonInput renglon,
        CuentaInfo cuenta,
        CancellationToken ct)
    {
        var unidades = cotizacionValor > 0m ? renglon.Monto / cotizacionValor : renglon.Monto;
        var efectivo = cuenta.Tipo == "O" ? renglon.Monto : 0m;
        var cheques = cuenta.Tipo is "C" or "B" ? renglon.Monto : 0m;
        const string sql = """
            INSERT INTO SBA05 (
                BARRA, CLASE, COD_COMP, COD_CTA, D_H, FECHA, LEYENDA, MONTO, N_COMP, RENGLON,
                UNIDADES, COTIZ_MONE, ID_SBA02, ID_SBA04, ID_SBA01, CONCILIADO, EFECTIVO, CHEQUES)
            VALUES (
                @barra, @clase, @codComp, @codCta, @dH, @fecha, @leyenda, @monto, @nComp, @renglon,
                @unidades, @cotiz, @idSba02, @idSba04, @idSba01, 0, @efectivo, @cheques)
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@barra", barra);
        cmd.Parameters.AddWithValue("@clase", tipo.Clase);
        cmd.Parameters.AddWithValue("@codComp", tipo.CodComp);
        cmd.Parameters.AddWithValue("@codCta", cuenta.CodCta);
        cmd.Parameters.AddWithValue("@dH", renglon.DH);
        cmd.Parameters.AddWithValue("@fecha", DateTime.Parse(fecha, CultureInfo.InvariantCulture).Date);
        cmd.Parameters.AddWithValue("@leyenda", (object?)renglon.Leyenda ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@monto", renglon.Monto);
        cmd.Parameters.AddWithValue("@nComp", nComp);
        cmd.Parameters.AddWithValue("@renglon", renglon.Renglon);
        cmd.Parameters.AddWithValue("@unidades", unidades);
        cmd.Parameters.AddWithValue("@cotiz", cotizacionValor);
        cmd.Parameters.AddWithValue("@idSba02", tipo.IdSba02);
        cmd.Parameters.AddWithValue("@idSba04", idSba04);
        cmd.Parameters.AddWithValue("@idSba01", cuenta.IdSba01);
        cmd.Parameters.AddWithValue("@efectivo", efectivo);
        cmd.Parameters.AddWithValue("@cheques", cheques);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task UpdateSaldoAsync(
        SqlConnection c, SqlTransaction t, int codCta, decimal delta, int timeout, CancellationToken ct)
    {
        const string sql = "UPDATE SBA01 SET SALDO = ISNULL(SALDO, 0) + @delta WHERE COD_CTA = @codCta";
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@delta", delta);
        cmd.Parameters.AddWithValue("@codCta", codCta);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    internal static async Task GenerarAsientoSiCorrespondeAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int nInterno,
        IReadOnlyList<(RenglonInput Renglon, CuentaInfo Cuenta)> cuentas,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(c, t, "ASIENTO_COMPROBANTE_SB", timeout, ct).ConfigureAwait(false)
            || !await TableExistsAsync(c, t, "ASIENTO_SB", timeout, ct).ConfigureAwait(false))
        {
            return;
        }

        int idAsiento;
        var isIdentityCab = await ColumnIsIdentityAsync(
                c, t, "ASIENTO_COMPROBANTE_SB", "ID_ASIENTO_COMPROBANTE_SB", timeout, ct)
            .ConfigureAwait(false);

        if (isIdentityCab)
        {
            const string insertCab = """
                INSERT INTO ASIENTO_COMPROBANTE_SB (
                    N_INTERNO, ASIENTO_ANULACION, CONTABILIZADO, USUARIO_CONTABILIZACION,
                    FECHA_CONTABILIZACION, TERMINAL_CONTABILIZACION, TRANSFERIDO_CN)
                OUTPUT INSERTED.ID_ASIENTO_COMPROBANTE_SB
                VALUES (@nInterno, N'N', N'N', NULL, NULL, NULL, N'N')
                """;
            await using var cmd = CreateCommand(c, t, timeout, insertCab);
            cmd.Parameters.AddWithValue("@nInterno", nInterno);
            idAsiento = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }
        else
        {
            idAsiento = await NextSequenceIdAsync(
                    c, t, "SEQUENCE_ASIENTO_COMPROBANTE_SB", "ASIENTO_COMPROBANTE_SB",
                    "ID_ASIENTO_COMPROBANTE_SB", timeout, ct)
                .ConfigureAwait(false);
            const string insertCab = """
                INSERT INTO ASIENTO_COMPROBANTE_SB (
                    ID_ASIENTO_COMPROBANTE_SB, N_INTERNO, ASIENTO_ANULACION, CONTABILIZADO,
                    USUARIO_CONTABILIZACION, FECHA_CONTABILIZACION, TERMINAL_CONTABILIZACION, TRANSFERIDO_CN)
                VALUES (@id, @nInterno, N'N', N'N', NULL, NULL, NULL, N'N')
                """;
            await using var cmd = CreateCommand(c, t, timeout, insertCab);
            cmd.Parameters.AddWithValue("@id", idAsiento);
            cmd.Parameters.AddWithValue("@nInterno", nInterno);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var isIdentityLinea = await ColumnIsIdentityAsync(c, t, "ASIENTO_SB", "ID_ASIENTO_SB", timeout, ct)
            .ConfigureAwait(false);

        foreach (var (renglon, cuenta) in cuentas)
        {
            var idCuenta = await ResolveIdCuentaContableAsync(
                    c, t, cuenta.IdSba01, timeout, ct)
                .ConfigureAwait(false);
            if (idCuenta is null)
            {
                throw new ValidationException($"No hay CUENTA_SB.ID_CUENTA para ID_SBA01={cuenta.IdSba01}.");
            }

            if (isIdentityLinea)
            {
                const string insertLinea = """
                    INSERT INTO ASIENTO_SB (
                        ID_ASIENTO_COMPROBANTE_SB, NRO_RENGLON_ASIENTO_SB, ID_CUENTA, D_H,
                        IMPORTE_RENGLON_BASE_SB, IMPORTE_RENGLON_ALTER_SB, DESC_LEYENDA, EDITA_CUENTA)
                    VALUES (
                        @idAsiento, @nro, @idCuenta, @dH, @monto, @monto, @leyenda, N'N')
                    """;
                await using var cmd = CreateCommand(c, t, timeout, insertLinea);
                cmd.Parameters.AddWithValue("@idAsiento", idAsiento);
                cmd.Parameters.AddWithValue("@nro", renglon.Renglon + 1);
                cmd.Parameters.AddWithValue("@idCuenta", idCuenta.Value);
                cmd.Parameters.AddWithValue("@dH", renglon.DH);
                cmd.Parameters.AddWithValue("@monto", renglon.Monto);
                cmd.Parameters.AddWithValue("@leyenda", (object?)renglon.Leyenda ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var idLinea = await NextSequenceIdAsync(
                        c, t, "SEQUENCE_ASIENTO_SB", "ASIENTO_SB", "ID_ASIENTO_SB", timeout, ct)
                    .ConfigureAwait(false);
                const string insertLinea = """
                    INSERT INTO ASIENTO_SB (
                        ID_ASIENTO_SB, ID_ASIENTO_COMPROBANTE_SB, NRO_RENGLON_ASIENTO_SB, ID_CUENTA, D_H,
                        IMPORTE_RENGLON_BASE_SB, IMPORTE_RENGLON_ALTER_SB, DESC_LEYENDA, EDITA_CUENTA)
                    VALUES (
                        @idLinea, @idAsiento, @nro, @idCuenta, @dH, @monto, @monto, @leyenda, N'N')
                    """;
                await using var cmd = CreateCommand(c, t, timeout, insertLinea);
                cmd.Parameters.AddWithValue("@idLinea", idLinea);
                cmd.Parameters.AddWithValue("@idAsiento", idAsiento);
                cmd.Parameters.AddWithValue("@nro", renglon.Renglon + 1);
                cmd.Parameters.AddWithValue("@idCuenta", idCuenta.Value);
                cmd.Parameters.AddWithValue("@dH", renglon.DH);
                cmd.Parameters.AddWithValue("@monto", renglon.Monto);
                cmd.Parameters.AddWithValue("@leyenda", (object?)renglon.Leyenda ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task InsertCotizacionSiCorrespondeAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        CotizacionInput cotizacion,
        decimal cotizacionValor,
        int idSba02,
        string nComp,
        int barra,
        CancellationToken ct)
    {
        if (!await TableExistsAsync(c, t, "COMPROBANTE_COTIZACION_SB", timeout, ct).ConfigureAwait(false))
        {
            return;
        }

        const string sql = """
            INSERT INTO COMPROBANTE_COTIZACION_SB (
                ID_MONEDA, ID_TIPO_COTIZACION, COTIZACION, ID_SBA02, N_COMP, BARRA)
            VALUES (@idMoneda, @idTipo, @cotizacion, @idSba02, @nComp, @barra)
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@idMoneda", cotizacion.IdMoneda);
        cmd.Parameters.AddWithValue("@idTipo", (object?)cotizacion.IdTipoCotizacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cotizacion", cotizacionValor);
        cmd.Parameters.AddWithValue("@idSba02", idSba02);
        cmd.Parameters.AddWithValue("@nComp", nComp);
        cmd.Parameters.AddWithValue("@barra", barra);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<CabeceraRow?> ReloadCabeceraAsync(
        SqlConnection c, SqlTransaction t, string codComp, string nComp, int barra, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 COD_COMP, N_COMP, BARRA, N_INTERNO, ID_SBA04, CLASE, SITUACION, EXTERNO,
                   FECHA_ULTIMA_MODIFICACION, HORA_ULTIMA_MODIFICACION
            FROM SBA04
            WHERE COD_COMP = @codComp AND N_COMP = @nComp AND BARRA = @barra
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@codComp", codComp);
        cmd.Parameters.AddWithValue("@nComp", nComp);
        cmd.Parameters.AddWithValue("@barra", barra);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new CabeceraRow(
            reader.GetString(reader.GetOrdinal("COD_COMP")).Trim(),
            reader.GetString(reader.GetOrdinal("N_COMP")),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("BARRA"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("N_INTERNO"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_SBA04"))),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("CLASE"))),
            (ReadString(reader, "SITUACION") ?? string.Empty).Trim(),
            ReadBool(reader, "EXTERNO"),
            FormatDateTime(reader, "FECHA_ULTIMA_MODIFICACION"),
            (ReadString(reader, "HORA_ULTIMA_MODIFICACION") ?? string.Empty).Trim());
    }

    private static Dictionary<string, object?> BuildResumen(CabeceraRow cabecera) =>
        new(StringComparer.Ordinal)
        {
            ["codComp"] = cabecera.CodComp,
            ["nComp"] = cabecera.NComp,
            ["barra"] = cabecera.Barra,
            ["nInterno"] = cabecera.NInterno,
            ["idSba04"] = cabecera.IdSba04,
            ["clase"] = cabecera.Clase,
            ["situacion"] = cabecera.Situacion,
            ["externo"] = cabecera.Externo,
            ["rowVersion"] = MovimientosTesoreriaGatewayRunner.EncodeRowVersion(
                cabecera.IdSba04,
                cabecera.Situacion,
                cabecera.FechaUltimaModificacion,
                cabecera.HoraUltimaModificacion,
                cabecera.NInterno),
            ["creado"] = true
        };

    internal static async Task<int?> ResolveIdCuentaContableAsync(
        SqlConnection c, SqlTransaction t, int idSba01, int timeout, CancellationToken ct)
    {
        if (!await TableExistsAsync(c, t, "CUENTA_SB", timeout, ct).ConfigureAwait(false))
        {
            return null;
        }

        const string sql = "SELECT TOP 1 ID_CUENTA FROM CUENTA_SB WHERE ID_SBA01 = @idSba01";
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@idSba01", idSba01);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (o is null or DBNull)
        {
            return null;
        }

        var id = Convert.ToInt32(o);
        return id > 0 ? id : null;
    }

    internal static async Task<int> NextSequenceIdAsync(
        SqlConnection c,
        SqlTransaction t,
        string sequence,
        string table,
        string idColumn,
        int timeout,
        CancellationToken ct)
    {
        try
        {
            await using var cmd = CreateCommand(c, t, timeout, $"SELECT NEXT VALUE FOR dbo.{sequence} AS id");
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (o is not null and not DBNull)
            {
                return Convert.ToInt32(o);
            }
        }
        catch
        {
            // fallback MAX+1
        }

        await using var maxCmd = CreateCommand(c, t, timeout, $"SELECT MAX([{idColumn}]) FROM [{table}]");
        var max = await maxCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return (max is null or DBNull ? 0 : Convert.ToInt32(max)) + 1;
    }

    internal static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    internal static async Task<bool> ColumnIsIdentityAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            c,
            t,
            timeout,
            "SELECT COLUMNPROPERTY(OBJECT_ID(@obj), @col, 'IsIdentity')");
        cmd.Parameters.AddWithValue("@obj", "dbo." + table);
        cmd.Parameters.AddWithValue("@col", column);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null and not DBNull && Convert.ToInt32(o) == 1;
    }

    internal static SqlCommand CreateCommand(
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

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }

    internal static string? ReadString(SqlDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        return reader.IsDBNull(ord) ? null : reader.GetValue(ord)?.ToString();
    }

    internal static bool ReadBool(SqlDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        if (reader.IsDBNull(ord))
        {
            return false;
        }

        var v = reader.GetValue(ord);
        return v switch
        {
            bool b => b,
            byte by => by != 0,
            short s => s != 0,
            int i => i != 0,
            long l => l != 0,
            string s when bool.TryParse(s, out var p) => p,
            string s when int.TryParse(s, out var n) => n != 0,
            _ => Convert.ToBoolean(v)
        };
    }

    internal static string FormatDateTime(SqlDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        if (reader.IsDBNull(ord))
        {
            return string.Empty;
        }

        var v = reader.GetValue(ord);
        return v switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => v.ToString() ?? string.Empty
        };
    }

    private static int? ReadIntProp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
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

    private static decimal? ReadDecimalProp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                p.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private static string? ReadStringProp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
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

    private static bool? ExtractBool(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s when bool.TryParse(s, out var p) => p,
            string s when s is "1" or "true" or "True" => true,
            string s when s is "0" or "false" or "False" => false,
            JsonElement je when je.ValueKind is JsonValueKind.True => true,
            JsonElement je when je.ValueKind is JsonValueKind.False => false,
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

    internal sealed record RenglonInput(int Renglon, int CodCta, string DH, decimal Monto, string? Leyenda);

    private sealed record CotizacionInput(int IdMoneda, int? IdTipoCotizacion, decimal Cotizacion);

    internal sealed record TipoComprobante(
        int IdSba02,
        string CodComp,
        int Clase,
        string TipoNumer,
        bool Edita,
        bool AfectaCon,
        string? ConcepAsi);

    internal sealed record CuentaInfo(int CodCta, string Tipo, string? CcCa, int IdSba01, decimal Saldo);

    private sealed record CabeceraRow(
        string CodComp,
        string NComp,
        int Barra,
        int NInterno,
        int IdSba04,
        int Clase,
        string Situacion,
        bool Externo,
        string FechaUltimaModificacion,
        string HoraUltimaModificacion);

    internal sealed class ValidationException(string message) : Exception(message);
}
