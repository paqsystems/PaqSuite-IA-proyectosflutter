using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.AsientosContables;

/// <summary>
/// D6.7.2 — alta AsientosContables orquestada en agente (espejo PHP AsientosContablesAltaService).
/// </summary>
public sealed class AsientosContablesCreateRunner
{
    public const string TerminalIngreso = "Api-PaqSuiteWeb";
    public const string CodSinAsignar = "SinAsignar";
    public const string PorEjercicio = "Por ejercicio";
    public const string Correlativo = "Correlativo";
    public const string PorDia = "Por día";
    public const string PorPeriodo = "Por período";
    private const decimal ToleranciaPartida = 0.01m;

    private readonly AsientosContablesGatewayRunner getRunner;

    public AsientosContablesCreateRunner(AsientosContablesGatewayRunner getRunner)
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
        var codTipoAsiento = ExtractString(parameters, "cod_tipo_asiento")?.Trim();
        var fecha = NormalizeDate(ExtractString(parameters, "fecha"));
        var renglonesJson = ExtractString(parameters, "renglones_json");

        if (string.IsNullOrWhiteSpace(database)
            || nroInterno is null or <= 0
            || string.IsNullOrWhiteSpace(codTipoAsiento)
            || string.IsNullOrWhiteSpace(fecha)
            || string.IsNullOrWhiteSpace(renglonesJson))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "nro_interno_analitico, cod_tipo_asiento, fecha, renglones_json y _database son obligatorios.");
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

        List<RenglonInput> renglones;
        try
        {
            renglones = ParseRenglones(renglonesJson);
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", "renglones_json invalido: " + ex.Message);
        }

        if (renglones.Count == 0)
        {
            return Fail("INVALID_PARAMETERS", "renglones es obligatorio y no puede estar vacío.");
        }

        var codMoneda = ExtractString(parameters, "cod_moneda")?.Trim();
        var nroAsientoInformado = ExtractDouble(parameters, "nro_asiento");
        var leyenda = Truncate(ExtractString(parameters, "leyenda"), 100);
        var observaciones = Truncate(ExtractString(parameters, "observaciones"), 1000);
        var usuarioRaw = ExtractString(parameters, "usuario")?.Trim();
        var usuario = string.IsNullOrWhiteSpace(usuarioRaw) ? "api" : Truncate(usuarioRaw, 120)!;

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
                await ExecuteCreateAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        nroInterno.Value,
                        codTipoAsiento!,
                        fecha!,
                        codMoneda,
                        nroAsientoInformado,
                        leyenda,
                        observaciones,
                        usuario,
                        renglones,
                        cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ConflictException cex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Fail("CONFLICT", cex.Message);
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

            if (!AsientosContablesCatalog.TryGet("AsientosContables.Get", out var getDef))
            {
                return Fail("INTERNAL_ERROR", "AsientosContables.Get no registrado en catalogo.");
            }

            var getOutcome = await getRunner
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

            if (getOutcome.Status != JobStatuses.Success || getOutcome.Data is not Dictionary<string, object?> data)
            {
                return getOutcome.Status == JobStatuses.Success
                    ? Fail("VALIDATION", "No se pudo recuperar el asiento creado.")
                    : getOutcome;
            }

            data["creado"] = true;
            return Ok(data);
        }
        catch (ConflictException cex)
        {
            return Fail("CONFLICT", cex.Message);
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

    private static async Task ExecuteCreateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int nroInterno,
        string codTipoAsiento,
        string fecha,
        string? codMoneda,
        double? nroAsientoInformado,
        string? leyenda,
        string? observaciones,
        string usuario,
        IReadOnlyList<RenglonInput> renglones,
        CancellationToken cancellationToken)
    {
        if (await ExistsNroInternoAsync(connection, transaction, nroInterno, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new ConflictException("Ya existe un asiento con ese nroInternoAnalitico.");
        }

        var tipo = await ResolveTipoAsientoAsync(
                connection, transaction, codTipoAsiento, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var estado = tipo.EstadoInicial;
        var estadoResumen = EstadoResumenFromGenera(tipo.GeneraResumen);
        var ejercicioPeriodo = await ResolveEjercicioPeriodoAsync(
                connection, transaction, fecha, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var idMoneda = await ResolveMonedaAsync(
                connection, transaction, codMoneda, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        double nroAsiento;
        if (nroAsientoInformado is not null)
        {
            if (await ExisteNroAsientoAsync(
                        connection,
                        transaction,
                        nroAsientoInformado.Value,
                        ejercicioPeriodo.TipoNumeracion,
                        fecha,
                        ejercicioPeriodo.IdEjercicio,
                        ejercicioPeriodo.IdPeriodo,
                        timeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new ConflictException("El nroAsiento ya existe según el criterio de numeración.");
            }

            nroAsiento = nroAsientoInformado.Value;
        }
        else
        {
            nroAsiento = await SiguienteNroAsientoAsync(
                    connection,
                    transaction,
                    ejercicioPeriodo.TipoNumeracion,
                    fecha,
                    ejercicioPeriodo.IdEjercicio,
                    ejercicioPeriodo.IdPeriodo,
                    ejercicioPeriodo.NroAsientoDesde,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (ExigePartidaDoble(estado) && !EstaBalanceado(renglones))
        {
            throw new ValidationException("El asiento no cumple partida doble.");
        }

        var now = DateTime.Now;
        var idAsiento = await InsertCabeceraAsync(
                connection,
                transaction,
                timeoutSeconds,
                ejercicioPeriodo.IdPeriodo,
                tipo.IdTipoAsiento,
                idMoneda,
                nroInterno,
                nroAsiento,
                leyenda,
                fecha,
                estado,
                estadoResumen,
                observaciones,
                usuario,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        for (var idx = 0; idx < renglones.Count; idx++)
        {
            await InsertRenglonAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    idAsiento,
                    fecha,
                    idMoneda,
                    renglones[idx],
                    idx,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal static List<RenglonInput> ParseRenglones(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Se esperaba un array JSON.");
        }

        var list = new List<RenglonInput>();
        var idx = 0;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var renglon = ReadIntProp(el, "renglon") ?? (idx + 1);
            var codCuenta = (ReadStringProp(el, "codCuenta") ?? ReadStringProp(el, "cod_cuenta") ?? string.Empty)
                .Trim();
            var dH = (ReadStringProp(el, "dH") ?? ReadStringProp(el, "d_h") ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
            var importe = ReadDecimalProp(el, "importe") ?? 0m;
            var leyenda = Truncate(ReadStringProp(el, "leyenda"), 100);
            var tipos = ParseTiposAuxiliar(el);
            list.Add(new RenglonInput(renglon, codCuenta, dH, importe, leyenda, tipos));
            idx++;
        }

        return list;
    }

    private static List<TipoAuxiliarInput> ParseTiposAuxiliar(JsonElement el)
    {
        if (!TryGetProperty(el, "tiposAuxiliar", out var tiposEl)
            && !TryGetProperty(el, "tipos_auxiliar", out tiposEl))
        {
            return new List<TipoAuxiliarInput>();
        }

        if (tiposEl.ValueKind != JsonValueKind.Array)
        {
            return new List<TipoAuxiliarInput>();
        }

        var tipos = new List<TipoAuxiliarInput>();
        foreach (var tipoEl in tiposEl.EnumerateArray())
        {
            var codTipo = (ReadStringProp(tipoEl, "codTipoAuxiliar")
                           ?? ReadStringProp(tipoEl, "cod_tipo_auxiliar")
                           ?? string.Empty).Trim();
            var auxiliares = new List<AuxiliarInput>();
            if (TryGetProperty(tipoEl, "auxiliares", out var auxArr) && auxArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var auxEl in auxArr.EnumerateArray())
                {
                    var codAux = (ReadStringProp(auxEl, "codAuxiliar")
                                  ?? ReadStringProp(auxEl, "cod_auxiliar")
                                  ?? string.Empty).Trim();
                    var importe = ReadDecimalProp(auxEl, "importe");
                    var porcentaje = ReadDecimalProp(auxEl, "porcentaje");
                    var subs = ParseSubauxiliares(auxEl);
                    auxiliares.Add(new AuxiliarInput(codAux, importe, porcentaje, subs));
                }
            }

            tipos.Add(new TipoAuxiliarInput(codTipo, auxiliares));
        }

        return tipos;
    }

    private static List<SubauxiliarInput> ParseSubauxiliares(JsonElement auxEl)
    {
        if (!TryGetProperty(auxEl, "subauxiliares", out var subArr) || subArr.ValueKind != JsonValueKind.Array)
        {
            return new List<SubauxiliarInput>();
        }

        var list = new List<SubauxiliarInput>();
        foreach (var subEl in subArr.EnumerateArray())
        {
            var cod = (ReadStringProp(subEl, "codSubauxiliar")
                       ?? ReadStringProp(subEl, "cod_subauxiliar")
                       ?? string.Empty).Trim();
            list.Add(new SubauxiliarInput(
                cod,
                ReadDecimalProp(subEl, "importe"),
                ReadDecimalProp(subEl, "porcentaje")));
        }

        return list;
    }

    internal static bool EstaBalanceado(IReadOnlyList<RenglonInput> renglones)
    {
        decimal debe = 0m;
        decimal haber = 0m;
        foreach (var r in renglones)
        {
            if (r.DH == "D")
            {
                debe += r.Importe;
            }
            else if (r.DH == "H")
            {
                haber += r.Importe;
            }
        }

        return Math.Abs(debe - haber) <= ToleranciaPartida;
    }

    internal static bool ExigePartidaDoble(string estadoAsientoAnalitico) =>
        estadoAsientoAnalitico is "Ingresado" or "Registrado";

    internal static string EstadoResumenFromGenera(string generaAsientoResumen) =>
        string.Equals(generaAsientoResumen?.Trim(), "S", StringComparison.OrdinalIgnoreCase)
            ? "Pendiente"
            : "No genera";

    private static async Task<bool> ExistsNroInternoAsync(
        SqlConnection c, SqlTransaction t, int nroInterno, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            c, t, timeout,
            "SELECT TOP 1 1 FROM dbo.ASIENTO_ANALITICO_CN WHERE NRO_INTERNO_ANALITICO = @nro");
        cmd.Parameters.AddWithValue("@nro", nroInterno);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null and not DBNull;
    }

    private static async Task<TipoAsientoInfo> ResolveTipoAsientoAsync(
        SqlConnection c, SqlTransaction t, string codTipoAsiento, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 ID_TIPO_ASIENTO, ESTADO_INICIAL_ASIENTOS, GENERA_ASIENTO_RESUMEN,
                   EDITA_ESTADO_ASIENTOS, COD_TIPO_ASIENTO, HABILITADO
            FROM dbo.TIPO_ASIENTO
            WHERE LTRIM(RTRIM(COD_TIPO_ASIENTO)) = @cod
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@cod", codTipoAsiento);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new ValidationException("Tipo de asiento inexistente.");
        }

        var habilitado = (ReadString(reader, "HABILITADO") ?? "S").Trim().ToUpperInvariant();
        if (habilitado != "S")
        {
            throw new ValidationException("Tipo de asiento no habilitado.");
        }

        return new TipoAsientoInfo(
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_TIPO_ASIENTO"))),
            (ReadString(reader, "ESTADO_INICIAL_ASIENTOS") ?? string.Empty).Trim(),
            (ReadString(reader, "GENERA_ASIENTO_RESUMEN") ?? "N").Trim(),
            (ReadString(reader, "EDITA_ESTADO_ASIENTOS") ?? "N").Trim(),
            (ReadString(reader, "COD_TIPO_ASIENTO") ?? codTipoAsiento).Trim());
    }

    /// <summary>Usado por Update/Delete para revalidar ejercicio/período abiertos.</summary>
    internal static Task<EjercicioPeriodoInfo> ResolveEjercicioPeriodoForUpdateAsync(
        SqlConnection c, SqlTransaction t, string fecha, int timeout, CancellationToken ct) =>
        ResolveEjercicioPeriodoAsync(c, t, fecha, timeout, ct);

    /// <summary>Persistencia de renglones (Create + replace en Update).</summary>
    internal static async Task PersistRenglonesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idAsiento,
        string fecha,
        int idMoneda,
        IReadOnlyList<RenglonInput> renglones,
        CancellationToken cancellationToken)
    {
        for (var idx = 0; idx < renglones.Count; idx++)
        {
            await InsertRenglonAsync(
                    connection,
                    transaction,
                    timeoutSeconds,
                    idAsiento,
                    fecha,
                    idMoneda,
                    renglones[idx],
                    idx,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<EjercicioPeriodoInfo> ResolveEjercicioPeriodoAsync(
        SqlConnection c, SqlTransaction t, string fecha, int timeout, CancellationToken ct)
    {
        const string sqlEj = """
            SELECT TOP 1 ID_EJERCICIO, ESTADO_EJERCICIO, HABILITADO, TIPO_NUMERACION_ASIENTOS, NRO_ASIENTO_DESDE
            FROM dbo.EJERCICIO
            WHERE CAST(DESFE_EJERCICIO AS DATE) <= @fecha AND CAST(HASFE_EJERCICIO AS DATE) >= @fecha
            """;
        await using var cmdEj = CreateCommand(c, t, timeout, sqlEj);
        cmdEj.Parameters.AddWithValue("@fecha", fecha);
        await using var readerEj = await cmdEj.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await readerEj.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new ConflictException("No hay ejercicio contable para la fecha.");
        }

        var idEjercicio = Convert.ToInt32(readerEj.GetValue(readerEj.GetOrdinal("ID_EJERCICIO")));
        var estado = (ReadString(readerEj, "ESTADO_EJERCICIO") ?? string.Empty).Trim();
        var habilitado = (ReadString(readerEj, "HABILITADO") ?? "S").Trim().ToUpperInvariant();
        var tipoNumeracion = (ReadString(readerEj, "TIPO_NUMERACION_ASIENTOS") ?? PorEjercicio).Trim();
        double? nroDesde = null;
        var ordDesde = readerEj.GetOrdinal("NRO_ASIENTO_DESDE");
        if (!readerEj.IsDBNull(ordDesde))
        {
            nroDesde = Convert.ToDouble(readerEj.GetValue(ordDesde));
        }

        await readerEj.DisposeAsync().ConfigureAwait(false);

        if (estado != "Abierto" || habilitado != "S")
        {
            throw new ConflictException("El ejercicio contable no está abierto o habilitado.");
        }

        var hasHab = await ColumnExistsAsync(c, t, "PERIODO", "DESFE_HABILITADO", timeout, ct).ConfigureAwait(false)
                     && await ColumnExistsAsync(c, t, "PERIODO", "HASFE_HABILITADO", timeout, ct).ConfigureAwait(false);

        var sqlPer = hasHab
            ? """
              SELECT TOP 1 ID_PERIODO
              FROM dbo.PERIODO
              WHERE ID_EJERCICIO = @idEj
                AND CAST(DESFE_PERIODO AS DATE) <= @fecha AND CAST(HASFE_PERIODO AS DATE) >= @fecha
                AND CAST(DESFE_HABILITADO AS DATE) <= @fecha AND CAST(HASFE_HABILITADO AS DATE) >= @fecha
              """
            : """
              SELECT TOP 1 ID_PERIODO
              FROM dbo.PERIODO
              WHERE ID_EJERCICIO = @idEj
                AND CAST(DESFE_PERIODO AS DATE) <= @fecha AND CAST(HASFE_PERIODO AS DATE) >= @fecha
              """;
        await using var cmdPer = CreateCommand(c, t, timeout, sqlPer);
        cmdPer.Parameters.AddWithValue("@idEj", idEjercicio);
        cmdPer.Parameters.AddWithValue("@fecha", fecha);
        var idPeriodoObj = await cmdPer.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (idPeriodoObj is null or DBNull)
        {
            throw new ConflictException("No hay período contable habilitado para la fecha.");
        }

        return new EjercicioPeriodoInfo(
            idEjercicio,
            Convert.ToInt32(idPeriodoObj),
            tipoNumeracion,
            nroDesde);
    }

    private static async Task<int> ResolveMonedaAsync(
        SqlConnection c, SqlTransaction t, string? codMoneda, int timeout, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(codMoneda))
        {
            await using var cmd = CreateCommand(
                c, t, timeout,
                "SELECT TOP 1 ID_MONEDA FROM dbo.MONEDA WHERE LTRIM(RTRIM(COD_MONEDA)) = @cod");
            cmd.Parameters.AddWithValue("@cod", codMoneda.Trim());
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (o is null or DBNull)
            {
                throw new ValidationException("Moneda inexistente.");
            }

            return Convert.ToInt32(o);
        }

        await using var first = CreateCommand(
            c, t, timeout, "SELECT TOP 1 ID_MONEDA FROM dbo.MONEDA ORDER BY ID_MONEDA");
        var firstId = await first.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (firstId is null or DBNull)
        {
            throw new ValidationException("No hay monedas configuradas.");
        }

        return Convert.ToInt32(firstId);
    }

    private static async Task<CuentaInfo> ResolveCuentaAsync(
        SqlConnection c, SqlTransaction t, string codCuenta, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 ID_CUENTA, COD_CUENTA, HABILITADO
            FROM dbo.CUENTA
            WHERE LTRIM(RTRIM(COD_CUENTA)) = @cod
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@cod", codCuenta);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new ValidationException($"Cuenta inexistente: {codCuenta}.");
        }

        if (HasColumn(reader, "HABILITADO"))
        {
            var hab = (ReadString(reader, "HABILITADO") ?? "S").Trim().ToUpperInvariant();
            if (hab != "S")
            {
                throw new ValidationException($"Cuenta no habilitada: {codCuenta}.");
            }
        }

        return new CuentaInfo(
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_CUENTA"))),
            (ReadString(reader, "COD_CUENTA") ?? codCuenta).Trim());
    }

    private static async Task<List<TipoAuxiliarRequerido>> ResolveTiposAuxiliarRequeridosAsync(
        SqlConnection c, SqlTransaction t, int idCuenta, int timeout, CancellationToken ct)
    {
        if (!await TableExistsAsync(c, t, "CUENTA_TIPO_AUXILIAR", timeout, ct).ConfigureAwait(false))
        {
            return new List<TipoAuxiliarRequerido>();
        }

        const string sql = """
            SELECT cta.ID_TIPO_AUXILIAR, ta.COD_TIPO_AUXILIAR, cta.VALIDA_APROPIACION_TOTAL
            FROM dbo.CUENTA_TIPO_AUXILIAR cta
            INNER JOIN dbo.TIPO_AUXILIAR ta ON ta.ID_TIPO_AUXILIAR = cta.ID_TIPO_AUXILIAR
            WHERE cta.ID_CUENTA = @idCuenta
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@idCuenta", idCuenta);
        var list = new List<TipoAuxiliarRequerido>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var valida = string.Equals(
                (ReadString(reader, "VALIDA_APROPIACION_TOTAL") ?? "N").Trim(),
                "S",
                StringComparison.OrdinalIgnoreCase);
            list.Add(new TipoAuxiliarRequerido(
                Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_TIPO_AUXILIAR"))),
                (ReadString(reader, "COD_TIPO_AUXILIAR") ?? string.Empty).Trim(),
                valida));
        }

        return list;
    }

    private static async Task<(int IdAuxiliar, string CodAuxiliar, int IdTipoAuxiliar)> ResolveAuxiliarAsync(
        SqlConnection c, SqlTransaction t, string codAuxiliar, int? idTipoAuxiliar, int timeout, CancellationToken ct)
    {
        var sql = idTipoAuxiliar is null
            ? """
              SELECT TOP 1 ID_AUXILIAR, COD_AUXILIAR, ID_TIPO_AUXILIAR
              FROM dbo.AUXILIAR WHERE LTRIM(RTRIM(COD_AUXILIAR)) = @cod
              """
            : """
              SELECT TOP 1 ID_AUXILIAR, COD_AUXILIAR, ID_TIPO_AUXILIAR
              FROM dbo.AUXILIAR
              WHERE LTRIM(RTRIM(COD_AUXILIAR)) = @cod AND ID_TIPO_AUXILIAR = @idTipo
              """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@cod", codAuxiliar);
        if (idTipoAuxiliar is not null)
        {
            cmd.Parameters.AddWithValue("@idTipo", idTipoAuxiliar.Value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new ValidationException($"Auxiliar inexistente: {codAuxiliar}.");
        }

        return (
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_AUXILIAR"))),
            (ReadString(reader, "COD_AUXILIAR") ?? codAuxiliar).Trim(),
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_TIPO_AUXILIAR"))));
    }

    private static async Task<(int IdSubauxiliar, string CodSubauxiliar)> ResolveSubauxiliarAsync(
        SqlConnection c, SqlTransaction t, string codSubauxiliar, int idAuxiliar, int timeout, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 ID_SUBAUXILIAR, COD_SUBAUXILIAR
            FROM dbo.SUBAUXILIAR
            WHERE ID_AUXILIAR = @idAux AND LTRIM(RTRIM(COD_SUBAUXILIAR)) = @cod
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@idAux", idAuxiliar);
        cmd.Parameters.AddWithValue("@cod", codSubauxiliar);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new ValidationException($"Subauxiliar inexistente: {codSubauxiliar}.");
        }

        return (
            Convert.ToInt32(reader.GetValue(reader.GetOrdinal("ID_SUBAUXILIAR"))),
            (ReadString(reader, "COD_SUBAUXILIAR") ?? codSubauxiliar).Trim());
    }

    private static async Task<bool> ExisteNroAsientoAsync(
        SqlConnection c,
        SqlTransaction t,
        double nroAsiento,
        string tipoNumeracion,
        string fecha,
        int idEjercicio,
        int idPeriodo,
        int timeout,
        CancellationToken ct)
    {
        var (fromJoin, whereExtra) = NumeracionScope(tipoNumeracion);
        var sql = $"""
            SELECT TOP 1 1
            FROM dbo.ASIENTO_ANALITICO_CN a
            {fromJoin}
            WHERE a.NRO_ASIENTO_ANALITICO = @nro
            {whereExtra}
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue("@nro", nroAsiento);
        AddNumeracionParams(cmd, tipoNumeracion, fecha, idEjercicio, idPeriodo);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null and not DBNull;
    }

    private static async Task<double> SiguienteNroAsientoAsync(
        SqlConnection c,
        SqlTransaction t,
        string tipoNumeracion,
        string fecha,
        int idEjercicio,
        int idPeriodo,
        double? nroAsientoDesde,
        int timeout,
        CancellationToken ct)
    {
        var (fromJoin, whereExtra) = NumeracionScope(tipoNumeracion);
        var sql = $"""
            SELECT MAX(a.NRO_ASIENTO_ANALITICO)
            FROM dbo.ASIENTO_ANALITICO_CN a
            {fromJoin}
            WHERE 1=1
            {whereExtra}
            """;
        await using var cmd = CreateCommand(c, t, timeout, sql);
        AddNumeracionParams(cmd, tipoNumeracion, fecha, idEjercicio, idPeriodo);
        var max = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (max is null or DBNull)
        {
            if (tipoNumeracion == Correlativo && nroAsientoDesde is > 0)
            {
                return nroAsientoDesde.Value;
            }

            return 1d;
        }

        return Convert.ToDouble(max) + 1d;
    }

    private static (string FromJoin, string WhereExtra) NumeracionScope(string tipoNumeracion) =>
        tipoNumeracion switch
        {
            PorEjercicio => (
                "INNER JOIN dbo.PERIODO p ON p.ID_PERIODO = a.ID_PERIODO",
                "AND p.ID_EJERCICIO = @idEjercicio"),
            PorDia => (string.Empty, "AND CAST(a.FECHA_ASIENTO AS DATE) = @fecha"),
            PorPeriodo => (string.Empty, "AND a.ID_PERIODO = @idPeriodo"),
            _ => (string.Empty, string.Empty)
        };

    private static void AddNumeracionParams(
        SqlCommand cmd, string tipoNumeracion, string fecha, int idEjercicio, int idPeriodo)
    {
        switch (tipoNumeracion)
        {
            case PorEjercicio:
                cmd.Parameters.AddWithValue("@idEjercicio", idEjercicio);
                break;
            case PorDia:
                cmd.Parameters.AddWithValue("@fecha", fecha);
                break;
            case PorPeriodo:
                cmd.Parameters.AddWithValue("@idPeriodo", idPeriodo);
                break;
        }
    }

    private static async Task<int> InsertCabeceraAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idPeriodo,
        int idTipoAsiento,
        int idMoneda,
        int nroInterno,
        double nroAsiento,
        string? leyenda,
        string fecha,
        string estado,
        string estadoResumen,
        string? observaciones,
        string usuario,
        DateTime now,
        CancellationToken ct)
    {
        var isIdentity = await ColumnIsIdentityAsync(
                c, t, "ASIENTO_ANALITICO_CN", "ID_ASIENTO_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);

        if (isIdentity)
        {
            const string sql = """
                INSERT INTO dbo.ASIENTO_ANALITICO_CN (
                    ID_PERIODO, ID_TIPO_ASIENTO, ID_MONEDA_ASIENTO, NRO_INTERNO_ANALITICO,
                    NRO_ASIENTO_ANALITICO, DESC_ASIENTO_ANALITICO, FECHA_ASIENTO, ORIGEN_EXTERNO,
                    ORIGEN_ASIENTO, CLASE_ASIENTO, ESTADO_ASIENTO_ANALITICO, ESTADO_RESUMEN,
                    ASIENTO_REVERSION, ASIENTO_EXTRACONTABLE, OBSERVACIONES,
                    USUARIO_INGRESO, FECHA_INGRESO, TERMINAL_INGRESO)
                OUTPUT INSERTED.ID_ASIENTO_ANALITICO_CN
                VALUES (
                    @idPeriodo, @idTipo, @idMoneda, @nroInterno, @nroAsiento, @leyenda, @fecha, N'N',
                    N'Manual', N'Básico', @estado, @estadoResumen, N'N', N'N', @obs,
                    @usuario, @fechaIngreso, @terminal)
                """;
            await using var cmd = CreateCommand(c, t, timeout, sql);
            BindCabeceraParams(cmd, idPeriodo, idTipoAsiento, idMoneda, nroInterno, nroAsiento,
                leyenda, fecha, estado, estadoResumen, observaciones, usuario, now);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var id = await NextSequenceIdAsync(
                c, t, "SEQUENCE_ASIENTO_ANALITICO_CN", "ASIENTO_ANALITICO_CN",
                "ID_ASIENTO_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);
        const string sqlSeq = """
            INSERT INTO dbo.ASIENTO_ANALITICO_CN (
                ID_ASIENTO_ANALITICO_CN, ID_PERIODO, ID_TIPO_ASIENTO, ID_MONEDA_ASIENTO,
                NRO_INTERNO_ANALITICO, NRO_ASIENTO_ANALITICO, DESC_ASIENTO_ANALITICO, FECHA_ASIENTO,
                ORIGEN_EXTERNO, ORIGEN_ASIENTO, CLASE_ASIENTO, ESTADO_ASIENTO_ANALITICO, ESTADO_RESUMEN,
                ASIENTO_REVERSION, ASIENTO_EXTRACONTABLE, OBSERVACIONES,
                USUARIO_INGRESO, FECHA_INGRESO, TERMINAL_INGRESO)
            VALUES (
                @id, @idPeriodo, @idTipo, @idMoneda, @nroInterno, @nroAsiento, @leyenda, @fecha, N'N',
                N'Manual', N'Básico', @estado, @estadoResumen, N'N', N'N', @obs,
                @usuario, @fechaIngreso, @terminal)
            """;
        await using var cmdSeq = CreateCommand(c, t, timeout, sqlSeq);
        cmdSeq.Parameters.AddWithValue("@id", id);
        BindCabeceraParams(cmdSeq, idPeriodo, idTipoAsiento, idMoneda, nroInterno, nroAsiento,
            leyenda, fecha, estado, estadoResumen, observaciones, usuario, now);
        await cmdSeq.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return id;
    }

    private static void BindCabeceraParams(
        SqlCommand cmd,
        int idPeriodo,
        int idTipoAsiento,
        int idMoneda,
        int nroInterno,
        double nroAsiento,
        string? leyenda,
        string fecha,
        string estado,
        string estadoResumen,
        string? observaciones,
        string usuario,
        DateTime now)
    {
        cmd.Parameters.AddWithValue("@idPeriodo", idPeriodo);
        cmd.Parameters.AddWithValue("@idTipo", idTipoAsiento);
        cmd.Parameters.AddWithValue("@idMoneda", idMoneda);
        cmd.Parameters.AddWithValue("@nroInterno", nroInterno);
        cmd.Parameters.AddWithValue("@nroAsiento", nroAsiento);
        cmd.Parameters.AddWithValue("@leyenda", (object?)leyenda ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fecha", fecha);
        cmd.Parameters.AddWithValue("@estado", estado);
        cmd.Parameters.AddWithValue("@estadoResumen", estadoResumen);
        cmd.Parameters.AddWithValue("@obs", (object?)observaciones ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@fechaIngreso", now);
        cmd.Parameters.AddWithValue("@terminal", TerminalIngreso);
    }

    private static async Task InsertRenglonAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idAsiento,
        string fecha,
        int idMoneda,
        RenglonInput renglon,
        int idx,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(renglon.CodCuenta))
        {
            throw new ValidationException($"renglones.{idx}.codCuenta es obligatorio.");
        }

        if (renglon.DH is not ("D" or "H"))
        {
            throw new ValidationException($"renglones.{idx}.dH debe ser D o H.");
        }

        var cuenta = await ResolveCuentaAsync(c, t, renglon.CodCuenta, timeout, ct).ConfigureAwait(false);
        var idRenglon = await InsertRenglonRowAsync(
                c, t, timeout, idAsiento, cuenta.IdCuenta, renglon.Renglon, renglon.DH,
                renglon.Leyenda, fecha, ct)
            .ConfigureAwait(false);
        var idImporte = await InsertImporteRowAsync(
                c, t, timeout, idRenglon, idMoneda, (double)renglon.Importe, ct)
            .ConfigureAwait(false);

        var requeridos = await ResolveTiposAuxiliarRequeridosAsync(c, t, cuenta.IdCuenta, timeout, ct)
            .ConfigureAwait(false);
        if (requeridos.Count == 0)
        {
            if (renglon.TiposAuxiliar.Count > 0)
            {
                throw new ValidationException($"renglones.{idx}: la cuenta no admite tipos de auxiliar.");
            }

            return;
        }

        var byCod = new Dictionary<string, TipoAuxiliarInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var tipoNode in renglon.TiposAuxiliar)
        {
            if (!string.IsNullOrWhiteSpace(tipoNode.CodTipoAuxiliar))
            {
                byCod[tipoNode.CodTipoAuxiliar] = tipoNode;
            }
        }

        foreach (var req in requeridos)
        {
            if (!byCod.TryGetValue(req.CodTipoAuxiliar, out var tipoNode))
            {
                throw new ValidationException(
                    $"renglones.{idx}: falta tipo de auxiliar {req.CodTipoAuxiliar}.");
            }

            await InsertTipoAuxiliarAsync(
                    c, t, timeout, idImporte, (double)renglon.Importe, req, tipoNode, idx, ct)
                .ConfigureAwait(false);
            byCod.Remove(req.CodTipoAuxiliar);
        }

        if (byCod.Count > 0)
        {
            throw new ValidationException($"renglones.{idx}: hay tipos de auxiliar sobrantes.");
        }
    }

    private static async Task<int> InsertRenglonRowAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idAsiento,
        int idCuenta,
        int nroRenglon,
        string dH,
        string? leyenda,
        string fecha,
        CancellationToken ct)
    {
        var isIdentity = await ColumnIsIdentityAsync(
                c, t, "RENGLON_ANALITICO_CN", "ID_RENGLON_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);

        if (isIdentity)
        {
            const string sql = """
                INSERT INTO dbo.RENGLON_ANALITICO_CN (
                    ID_ASIENTO_ANALITICO_CN, ID_CUENTA, NRO_RENGLON_ANALITICO, D_H, DESC_LEYENDA, FECHA_ORIGEN)
                OUTPUT INSERTED.ID_RENGLON_ANALITICO_CN
                VALUES (@idAsiento, @idCuenta, @nro, @dH, @leyenda, @fecha)
                """;
            await using var cmd = CreateCommand(c, t, timeout, sql);
            cmd.Parameters.AddWithValue("@idAsiento", idAsiento);
            cmd.Parameters.AddWithValue("@idCuenta", idCuenta);
            cmd.Parameters.AddWithValue("@nro", nroRenglon);
            cmd.Parameters.AddWithValue("@dH", dH);
            cmd.Parameters.AddWithValue("@leyenda", (object?)leyenda ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fecha", fecha);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var id = await NextSequenceIdAsync(
                c, t, "SEQUENCE_RENGLON_ANALITICO_CN", "RENGLON_ANALITICO_CN",
                "ID_RENGLON_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);
        const string sqlSeq = """
            INSERT INTO dbo.RENGLON_ANALITICO_CN (
                ID_RENGLON_ANALITICO_CN, ID_ASIENTO_ANALITICO_CN, ID_CUENTA, NRO_RENGLON_ANALITICO,
                D_H, DESC_LEYENDA, FECHA_ORIGEN)
            VALUES (@id, @idAsiento, @idCuenta, @nro, @dH, @leyenda, @fecha)
            """;
        await using var cmdSeq = CreateCommand(c, t, timeout, sqlSeq);
        cmdSeq.Parameters.AddWithValue("@id", id);
        cmdSeq.Parameters.AddWithValue("@idAsiento", idAsiento);
        cmdSeq.Parameters.AddWithValue("@idCuenta", idCuenta);
        cmdSeq.Parameters.AddWithValue("@nro", nroRenglon);
        cmdSeq.Parameters.AddWithValue("@dH", dH);
        cmdSeq.Parameters.AddWithValue("@leyenda", (object?)leyenda ?? DBNull.Value);
        cmdSeq.Parameters.AddWithValue("@fecha", fecha);
        await cmdSeq.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return id;
    }

    private static async Task<int> InsertImporteRowAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idRenglon,
        int idMoneda,
        double importe,
        CancellationToken ct)
    {
        var isIdentity = await ColumnIsIdentityAsync(
                c, t, "RENGLON_IMPORTE_ANALITICO_CN", "ID_RENGLON_IMPORTE_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);

        if (isIdentity)
        {
            const string sql = """
                INSERT INTO dbo.RENGLON_IMPORTE_ANALITICO_CN (
                    ID_RENGLON_ANALITICO_CN, ID_MONEDA, IMPORTE_RENGLON)
                OUTPUT INSERTED.ID_RENGLON_IMPORTE_ANALITICO_CN
                VALUES (@idRenglon, @idMoneda, @importe)
                """;
            await using var cmd = CreateCommand(c, t, timeout, sql);
            cmd.Parameters.AddWithValue("@idRenglon", idRenglon);
            cmd.Parameters.AddWithValue("@idMoneda", idMoneda);
            cmd.Parameters.AddWithValue("@importe", importe);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var id = await NextSequenceIdAsync(
                c, t, "SEQUENCE_RENGLON_IMPORTE_ANALITICO_CN", "RENGLON_IMPORTE_ANALITICO_CN",
                "ID_RENGLON_IMPORTE_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);
        const string sqlSeq = """
            INSERT INTO dbo.RENGLON_IMPORTE_ANALITICO_CN (
                ID_RENGLON_IMPORTE_ANALITICO_CN, ID_RENGLON_ANALITICO_CN, ID_MONEDA, IMPORTE_RENGLON)
            VALUES (@id, @idRenglon, @idMoneda, @importe)
            """;
        await using var cmdSeq = CreateCommand(c, t, timeout, sqlSeq);
        cmdSeq.Parameters.AddWithValue("@id", id);
        cmdSeq.Parameters.AddWithValue("@idRenglon", idRenglon);
        cmdSeq.Parameters.AddWithValue("@idMoneda", idMoneda);
        cmdSeq.Parameters.AddWithValue("@importe", importe);
        await cmdSeq.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return id;
    }

    private static async Task InsertTipoAuxiliarAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idImporte,
        double importeRenglon,
        TipoAuxiliarRequerido req,
        TipoAuxiliarInput tipoNode,
        int idx,
        CancellationToken ct)
    {
        if (tipoNode.Auxiliares.Count == 0)
        {
            throw new ValidationException($"renglones.{idx}.tiposAuxiliar: auxiliares es obligatorio.");
        }

        var normalizados = new List<(int IdAuxiliar, double Importe, double Porcentaje, List<SubauxiliarInput> Subs)>();
        double suma = 0d;
        foreach (var aux in tipoNode.Auxiliares)
        {
            var resolved = await ResolveAuxiliarAsync(c, t, aux.CodAuxiliar, req.IdTipoAuxiliar, timeout, ct)
                .ConfigureAwait(false);
            var importe = aux.Importe is not null
                ? (double)aux.Importe.Value
                : (aux.Porcentaje is not null
                    ? Math.Round(importeRenglon * (double)aux.Porcentaje.Value / 100d, 2)
                    : 0d);
            var porcentaje = aux.Porcentaje is not null
                ? (double)aux.Porcentaje.Value
                : (importeRenglon > 0 ? Math.Round((importe / importeRenglon) * 100d, 4) : 0d);
            suma += importe;
            normalizados.Add((resolved.IdAuxiliar, importe, porcentaje, aux.Subauxiliares));
        }

        var diff = Math.Round(importeRenglon - suma, 2);
        if (Math.Abs(diff) > 0.01)
        {
            if (req.ValidaApropiacionTotal)
            {
                throw new ValidationException(
                    $"renglones.{idx}.tiposAuxiliar: Σ auxiliares debe igualar el importe del renglón.");
            }

            if (diff > 0)
            {
                var sinAsignar = await ResolveAuxiliarAsync(
                        c, t, CodSinAsignar, req.IdTipoAuxiliar, timeout, ct)
                    .ConfigureAwait(false);
                normalizados.Add((
                    sinAsignar.IdAuxiliar,
                    diff,
                    importeRenglon > 0 ? Math.Round((diff / importeRenglon) * 100d, 4) : 0d,
                    new List<SubauxiliarInput>()));
            }
        }

        foreach (var item in normalizados)
        {
            var idAuxAnalitico = await InsertAuxiliarAnaliticoAsync(
                    c, t, timeout, idImporte, item.IdAuxiliar, item.Porcentaje, item.Importe, ct)
                .ConfigureAwait(false);
            await InsertSubauxiliaresAsync(
                    c, t, timeout, idAuxAnalitico, item.IdAuxiliar, item.Importe,
                    item.Subs, req.ValidaApropiacionTotal, idx, ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task<int> InsertAuxiliarAnaliticoAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idImporte,
        int idAuxiliar,
        double porcentaje,
        double importe,
        CancellationToken ct)
    {
        var isIdentity = await ColumnIsIdentityAsync(
                c, t, "AUXILIAR_ANALITICO_CN", "ID_AUXILIAR_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);

        if (isIdentity)
        {
            const string sql = """
                INSERT INTO dbo.AUXILIAR_ANALITICO_CN (
                    ID_RENGLON_IMPORTE_ANALITICO_CN, ID_AUXILIAR, PORC_APROPIACION, IMPORTE_RENGLON, EDITA_APROPIACION)
                OUTPUT INSERTED.ID_AUXILIAR_ANALITICO_CN
                VALUES (@idImporte, @idAux, @porc, @importe, N'S')
                """;
            await using var cmd = CreateCommand(c, t, timeout, sql);
            cmd.Parameters.AddWithValue("@idImporte", idImporte);
            cmd.Parameters.AddWithValue("@idAux", idAuxiliar);
            cmd.Parameters.AddWithValue("@porc", porcentaje);
            cmd.Parameters.AddWithValue("@importe", importe);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var id = await NextSequenceIdAsync(
                c, t, "SEQUENCE_AUXILIAR_ANALITICO_CN", "AUXILIAR_ANALITICO_CN",
                "ID_AUXILIAR_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);
        const string sqlSeq = """
            INSERT INTO dbo.AUXILIAR_ANALITICO_CN (
                ID_AUXILIAR_ANALITICO_CN, ID_RENGLON_IMPORTE_ANALITICO_CN, ID_AUXILIAR,
                PORC_APROPIACION, IMPORTE_RENGLON, EDITA_APROPIACION)
            VALUES (@id, @idImporte, @idAux, @porc, @importe, N'S')
            """;
        await using var cmdSeq = CreateCommand(c, t, timeout, sqlSeq);
        cmdSeq.Parameters.AddWithValue("@id", id);
        cmdSeq.Parameters.AddWithValue("@idImporte", idImporte);
        cmdSeq.Parameters.AddWithValue("@idAux", idAuxiliar);
        cmdSeq.Parameters.AddWithValue("@porc", porcentaje);
        cmdSeq.Parameters.AddWithValue("@importe", importe);
        await cmdSeq.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return id;
    }

    private static async Task InsertSubauxiliaresAsync(
        SqlConnection c,
        SqlTransaction t,
        int timeout,
        int idAuxAnalitico,
        int idAuxiliar,
        double importeAuxiliar,
        IReadOnlyList<SubauxiliarInput> subauxiliares,
        bool validaTotal,
        int idx,
        CancellationToken ct)
    {
        if (subauxiliares.Count == 0)
        {
            return;
        }

        var normalizados = new List<(int IdSub, double Importe, double Porcentaje)>();
        double suma = 0d;
        foreach (var sub in subauxiliares)
        {
            var resolved = await ResolveSubauxiliarAsync(c, t, sub.CodSubauxiliar, idAuxiliar, timeout, ct)
                .ConfigureAwait(false);
            var importe = sub.Importe is not null
                ? (double)sub.Importe.Value
                : (sub.Porcentaje is not null
                    ? Math.Round(importeAuxiliar * (double)sub.Porcentaje.Value / 100d, 2)
                    : 0d);
            var porcentaje = sub.Porcentaje is not null
                ? (double)sub.Porcentaje.Value
                : (importeAuxiliar > 0 ? Math.Round((importe / importeAuxiliar) * 100d, 4) : 0d);
            suma += importe;
            normalizados.Add((resolved.IdSubauxiliar, importe, porcentaje));
        }

        var diff = Math.Round(importeAuxiliar - suma, 2);
        if (Math.Abs(diff) > 0.01)
        {
            if (validaTotal)
            {
                throw new ValidationException(
                    $"renglones.{idx}.subauxiliares: Σ subauxiliares debe igualar el importe del auxiliar.");
            }

            if (diff > 0)
            {
                var sinAsignar = await ResolveSubauxiliarAsync(c, t, CodSinAsignar, idAuxiliar, timeout, ct)
                    .ConfigureAwait(false);
                normalizados.Add((
                    sinAsignar.IdSubauxiliar,
                    diff,
                    importeAuxiliar > 0 ? Math.Round((diff / importeAuxiliar) * 100d, 4) : 0d));
            }
        }

        var isIdentity = await ColumnIsIdentityAsync(
                c, t, "SUBAUXILIAR_ANALITICO_CN", "ID_SUBAUXILIAR_ANALITICO_CN", timeout, ct)
            .ConfigureAwait(false);

        foreach (var item in normalizados)
        {
            if (isIdentity)
            {
                const string sql = """
                    INSERT INTO dbo.SUBAUXILIAR_ANALITICO_CN (
                        ID_AUXILIAR_ANALITICO_CN, ID_AUXILIAR, ID_SUBAUXILIAR,
                        PORC_APROPIACION, IMPORTE_RENGLON, EDITA_APROPIACION)
                    VALUES (@idAuxAn, @idAux, @idSub, @porc, @importe, N'S')
                    """;
                await using var cmd = CreateCommand(c, t, timeout, sql);
                cmd.Parameters.AddWithValue("@idAuxAn", idAuxAnalitico);
                cmd.Parameters.AddWithValue("@idAux", idAuxiliar);
                cmd.Parameters.AddWithValue("@idSub", item.IdSub);
                cmd.Parameters.AddWithValue("@porc", item.Porcentaje);
                cmd.Parameters.AddWithValue("@importe", item.Importe);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            else
            {
                var id = await NextSequenceIdAsync(
                        c, t, "SEQUENCE_SUBAUXILIAR_ANALITICO_CN", "SUBAUXILIAR_ANALITICO_CN",
                        "ID_SUBAUXILIAR_ANALITICO_CN", timeout, ct)
                    .ConfigureAwait(false);
                const string sqlSeq = """
                    INSERT INTO dbo.SUBAUXILIAR_ANALITICO_CN (
                        ID_SUBAUXILIAR_ANALITICO_CN, ID_AUXILIAR_ANALITICO_CN, ID_AUXILIAR, ID_SUBAUXILIAR,
                        PORC_APROPIACION, IMPORTE_RENGLON, EDITA_APROPIACION)
                    VALUES (@id, @idAuxAn, @idAux, @idSub, @porc, @importe, N'S')
                    """;
                await using var cmdSeq = CreateCommand(c, t, timeout, sqlSeq);
                cmdSeq.Parameters.AddWithValue("@id", id);
                cmdSeq.Parameters.AddWithValue("@idAuxAn", idAuxAnalitico);
                cmdSeq.Parameters.AddWithValue("@idAux", idAuxiliar);
                cmdSeq.Parameters.AddWithValue("@idSub", item.IdSub);
                cmdSeq.Parameters.AddWithValue("@porc", item.Porcentaje);
                cmdSeq.Parameters.AddWithValue("@importe", item.Importe);
                await cmdSeq.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> NextSequenceIdAsync(
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

    private static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            c, t, timeout,
            "SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@obj) AND name = @col");
        cmd.Parameters.AddWithValue("@obj", "dbo." + table);
        cmd.Parameters.AddWithValue("@col", column);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static async Task<bool> ColumnIsIdentityAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            c, t, timeout,
            "SELECT COLUMNPROPERTY(OBJECT_ID(@obj), @col, 'IsIdentity')");
        cmd.Parameters.AddWithValue("@obj", "dbo." + table);
        cmd.Parameters.AddWithValue("@col", column);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null and not DBNull && Convert.ToInt32(o) == 1;
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
            string s when int.TryParse(s, out var parsed) => parsed,
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

    private static bool TryGetProperty(JsonElement el, string name, out JsonElement value)
    {
        if (el.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int? ReadIntProp(JsonElement el, string name)
    {
        if (!TryGetProperty(el, name, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i))
        {
            return i;
        }

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? ReadDecimalProp(JsonElement el, string name)
    {
        if (!TryGetProperty(el, name, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d))
        {
            return d;
        }

        if (prop.ValueKind == JsonValueKind.String
            && decimal.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadStringProp(JsonElement el, string name)
    {
        if (!TryGetProperty(el, name, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static string? ReadString(SqlDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        return reader.IsDBNull(ord) ? null : reader.GetValue(ord)?.ToString();
    }

    private static bool HasColumn(SqlDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static AsientosContablesOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static AsientosContablesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };

    public sealed class ValidationException : Exception
    {
        public ValidationException(string message) : base(message)
        {
        }
    }

    public sealed class ConflictException : Exception
    {
        public ConflictException(string message) : base(message)
        {
        }
    }

    internal sealed record RenglonInput(
        int Renglon,
        string CodCuenta,
        string DH,
        decimal Importe,
        string? Leyenda,
        List<TipoAuxiliarInput> TiposAuxiliar);

    internal sealed record TipoAuxiliarInput(string CodTipoAuxiliar, List<AuxiliarInput> Auxiliares);

    internal sealed record AuxiliarInput(
        string CodAuxiliar,
        decimal? Importe,
        decimal? Porcentaje,
        List<SubauxiliarInput> Subauxiliares);

    internal sealed record SubauxiliarInput(string CodSubauxiliar, decimal? Importe, decimal? Porcentaje);

    private sealed record TipoAsientoInfo(
        int IdTipoAsiento,
        string EstadoInicial,
        string GeneraResumen,
        string EditaEstado,
        string CodTipoAsiento);

    internal sealed record EjercicioPeriodoInfo(
        int IdEjercicio,
        int IdPeriodo,
        string TipoNumeracion,
        double? NroAsientoDesde);

    private sealed record CuentaInfo(int IdCuenta, string CodCuenta);

    private sealed record TipoAuxiliarRequerido(int IdTipoAuxiliar, string CodTipoAuxiliar, bool ValidaApropiacionTotal);
}
