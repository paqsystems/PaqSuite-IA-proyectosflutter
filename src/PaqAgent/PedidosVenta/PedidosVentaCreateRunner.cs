using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.PedidosVenta;

/// <summary>
/// D6.2 MVP — alta PedidosVenta en TX company (sin Delta6 / sin cliente ocasional).
/// Orquestación en agente: correlativo PROXIMO (crypto Tango) + INSERT GVA21/GVA03 + STA19.
/// </summary>
public sealed class PedidosVentaCreateRunner
{
    public async Task<PedidosVentaOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var talonPed = ExtractInt(parameters, "talon_ped");
        var codClient = ExtractString(parameters, "cod_client")?.Trim();
        var renglonesJson = ExtractString(parameters, "renglones_json");

        if (string.IsNullOrWhiteSpace(database)
            || talonPed is null or <= 0
            || string.IsNullOrWhiteSpace(codClient)
            || string.IsNullOrWhiteSpace(renglonesJson))
        {
            return Fail(
                "INVALID_PARAMETERS",
                "talon_ped, cod_client, renglones_json y _database son obligatorios.");
        }

        if (string.Equals(codClient, "000000", StringComparison.Ordinal))
        {
            return Fail("OCASIONAL_NO_SOPORTADO", "Cliente ocasional 000000 fuera de alcance D6.2 MVP.");
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
            return Fail("INVALID_PARAMETERS", "Debe informar al menos un renglon con articulo.");
        }

        var nLista = ExtractInt(parameters, "n_lista") ?? 0;
        if (nLista <= 0)
        {
            return Fail("INVALID_PARAMETERS", "n_lista es obligatorio y debe ser > 0.");
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
                        parameters,
                        talonPed.Value,
                        codClient,
                        nLista,
                        renglones,
                        timeoutSeconds,
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

    private static async Task<PedidosVentaOutcome> ExecuteCreateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyDictionary<string, object?> parameters,
        int talonPed,
        string codClient,
        int nLista,
        IReadOnlyList<RenglonInput> renglones,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TalonarioPedOkAsync(connection, transaction, talonPed, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return Fail("TALONARIO_INVALIDO", "Talonario inexistente, inhabilitado o COMPROB distinto de PED.");
        }

        if (!await ClienteExisteAsync(connection, transaction, codClient, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return Fail("CLIENTE_INEXISTENTE", "Cliente inexistente.");
        }

        var idExterno = ExtractString(parameters, "id_externo")?.Trim();
        if (!string.IsNullOrWhiteSpace(idExterno))
        {
            idExterno = idExterno.Length > 20 ? idExterno[..20] : idExterno;
            if (await IdExternoExisteAsync(connection, transaction, idExterno, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Fail("ID_EXTERNO_DUPLICADO", "Ya existe un pedido con ese idExterno.");
            }
        }
        else
        {
            idExterno = null;
        }

        var nroPedidoInformado = ExtractString(parameters, "nro_pedido")?.Trim();
        string nroPedido;
        if (!string.IsNullOrWhiteSpace(nroPedidoInformado))
        {
            nroPedido = nroPedidoInformado;
            if (await PedidoExisteAsync(connection, transaction, talonPed, nroPedido, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Fail("NRO_PEDIDO_DUPLICADO", "Ya existe un pedido con talonPed y nroPedido.");
            }
        }
        else
        {
            var correlativo = await AsignarCorrelativoAsync(
                    connection, transaction, talonPed, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (correlativo.ErrorCode is not null)
            {
                return Fail(correlativo.ErrorCode, correlativo.ErrorMessage ?? correlativo.ErrorCode);
            }

            nroPedido = correlativo.NroPedido!;
        }

        var (apruebaPe, compStk) = await ReadParametrosAsync(
                connection, transaction, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var estado = apruebaPe ? 1 : 2;

        var totalPedi = renglones.Sum(r =>
            Math.Round(r.CantPedid * r.Precio * (1m - (r.Descuento / 100m)), 4));

        var monCte = ExtractBool(parameters, "mon_cte") ?? true;
        var cotiz = ExtractDecimal(parameters, "cotiz") ?? 1m;
        if (cotiz <= 0m && monCte)
        {
            cotiz = 1m;
        }

        var totalExtranjera = cotiz > 0m ? Math.Round(totalPedi / cotiz, 4) : 0m;
        var idGva21 = await NextIdAsync(
                connection, transaction, "SEQUENCE_GVA21", "GVA21", "ID_GVA21", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var idGva14 = await LookupIdAsync(
                connection, transaction,
                "SELECT TOP 1 ID_GVA14 FROM dbo.GVA14 WHERE LTRIM(RTRIM(COD_CLIENT))=@c",
                ("@c", codClient), timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var idGva10 = await LookupIdAsync(
                connection, transaction,
                "SELECT TOP 1 ID_GVA10 FROM dbo.GVA10 WHERE NRO_DE_LIS=@n OR ID_GVA10=@n",
                ("@n", nLista), timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var idGva43 = await LookupIdAsync(
                connection, transaction,
                "SELECT TOP 1 ID_GVA43 FROM dbo.GVA43 WHERE TALONARIO=@t",
                ("@t", talonPed), timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var fechaPedi = ExtractDate(parameters, "fecha_pedi") ?? DateTime.Today;
        var fechaEntr = ExtractDate(parameters, "fecha_entr");
        var now = DateTime.Now;
        var hora = now.ToString("HHmmss");
        var usuario = ExtractString(parameters, "usuario_ingreso") ?? "api";

        await InsertCabeceraAsync(
                connection,
                transaction,
                idGva21,
                talonPed,
                nroPedido,
                codClient,
                idGva14,
                idGva10,
                idGva43,
                estado,
                compStk,
                monCte,
                cotiz,
                nLista,
                totalPedi,
                totalExtranjera,
                fechaPedi,
                fechaEntr,
                ExtractInt(parameters, "cond_vta") ?? 1,
                ExtractString(parameters, "cod_vended") ?? string.Empty,
                ExtractString(parameters, "cod_transp") ?? string.Empty,
                ExtractString(parameters, "cod_sucurs") ?? string.Empty,
                ExtractString(parameters, "observaciones") ?? string.Empty,
                idExterno,
                usuario,
                hora,
                now,
                timeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);

        var codSucursDefault = ExtractString(parameters, "cod_sucurs") ?? string.Empty;
        foreach (var (renglon, index) in renglones.Select((r, i) => (r, i)))
        {
            var nRenglon = renglon.NRenglon ?? (index + 1);
            var idGva03 = await NextIdAsync(
                    connection, transaction, "SEQUENCE_GVA03", "GVA03", "ID_GVA03", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            var codDeposi = string.IsNullOrWhiteSpace(renglon.CodDeposi) ? codSucursDefault : renglon.CodDeposi!;
            var precioNeto = Math.Round(renglon.Precio * (1m - (renglon.Descuento / 100m)), 4);
            var importe = Math.Round(renglon.CantPedid * precioNeto, 4);

            var idSta11 = await LookupIdAsync(
                    connection, transaction,
                    "SELECT TOP 1 ID_STA11 FROM dbo.STA11 WHERE LTRIM(RTRIM(COD_ARTICU))=@a",
                    ("@a", renglon.CodArticu), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
            if (idSta11 is null)
            {
                return Fail("ARTICULO_INEXISTENTE", $"Articulo inexistente: {renglon.CodArticu}");
            }

            int? idSta22 = null;
            if (!string.IsNullOrWhiteSpace(codDeposi))
            {
                idSta22 = await LookupIdAsync(
                        connection, transaction,
                        "SELECT TOP 1 ID_STA22 FROM dbo.STA22 WHERE LTRIM(RTRIM(COD_STA22))=LTRIM(RTRIM(@d)) OR LTRIM(RTRIM(COD_SUCURS))=LTRIM(RTRIM(@d))",
                        ("@d", codDeposi), timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            await InsertRenglonAsync(
                    connection,
                    transaction,
                    idGva03,
                    idGva21,
                    talonPed,
                    nroPedido,
                    nRenglon,
                    renglon,
                    codDeposi,
                    precioNeto,
                    importe,
                    idSta11,
                    idSta22,
                    idGva10,
                    timeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);

            if (compStk && estado < 3 && idSta11 is not null && idSta22 is not null)
            {
                await IncrementarSta19Async(
                        connection, transaction, idSta11.Value, idSta22.Value, renglon.CantPedid,
                        timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        byte[]? rowVersion = null;
        if (await ColumnExistsAsync(connection, transaction, "GVA21", "ROW_VERSION", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            rowVersion = await ScalarBytesAsync(
                    connection, transaction,
                    "SELECT ROW_VERSION FROM dbo.GVA21 WHERE ID_GVA21=@id",
                    ("@id", idGva21), timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        return Ok(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["idGva21"] = idGva21,
            ["talonPed"] = talonPed,
            ["nroPedido"] = nroPedido,
            ["estado"] = estado,
            ["totalPedi"] = (double)totalPedi,
            ["totalPediConImpuestos"] = (double)totalPedi,
            ["rowVersion"] = rowVersion is null ? null : Convert.ToBase64String(rowVersion),
            ["creado"] = true,
            ["idExterno"] = idExterno
        });
    }

    private sealed record RenglonInput(
        int? NRenglon,
        string CodArticu,
        decimal CantPedid,
        decimal Precio,
        decimal Descuento,
        string? CodDeposi);

    private static List<RenglonInput> ParseRenglones(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("se esperaba un array JSON");
        }

        var list = new List<RenglonInput>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var cod = GetJsonString(el, "codArticu") ?? GetJsonString(el, "cod_articu");
            if (string.IsNullOrWhiteSpace(cod))
            {
                continue;
            }

            list.Add(new RenglonInput(
                GetJsonInt(el, "nRenglon") ?? GetJsonInt(el, "n_renglon"),
                cod.Trim(),
                GetJsonDecimal(el, "cantPedid") ?? GetJsonDecimal(el, "cant_pedid") ?? 0m,
                GetJsonDecimal(el, "precio") ?? 0m,
                GetJsonDecimal(el, "descuento") ?? 0m,
                GetJsonString(el, "codDeposi") ?? GetJsonString(el, "cod_deposi")));
        }

        return list;
    }

    private static async Task<(string? NroPedido, string? ErrorCode, string? ErrorMessage)> AsignarCorrelativoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int talonPed,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1
                LTRIM(RTRIM(CAST(PROXIMO AS NVARCHAR(32)))) AS proximo,
                CAST(ISNULL(TIPO, N'') AS NVARCHAR(10)) AS tipo,
                CAST(ISNULL(SUCURSAL, N'00000') AS NVARCHAR(10)) AS sucursal
            FROM dbo.GVA43
            WHERE TALONARIO = @t
            """);
        cmd.Parameters.AddWithValue("@t", talonPed);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (null, "TALONARIO_INVALIDO", "No se pudo leer GVA43.PROXIMO.");
        }

        var proximoEnc = reader.GetString(0);
        var tipo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        var sucursal = reader.IsDBNull(2) ? "00000" : reader.GetString(2);
        await reader.CloseAsync().ConfigureAwait(false);

        string nroPedido;
        string siguiente;
        try
        {
            (nroPedido, siguiente, _) = EncriptacionTalonarios.AsignarNumero(proximoEnc, tipo, sucursal);
        }
        catch (Exception ex)
        {
            return (null, "PROXIMO_INVALIDO", ex.Message);
        }

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            UPDATE dbo.GVA43
            SET PROXIMO = @next
            WHERE TALONARIO = @t AND LTRIM(RTRIM(CAST(PROXIMO AS NVARCHAR(32)))) = @prev
            """);
        upd.Parameters.AddWithValue("@next", siguiente);
        upd.Parameters.AddWithValue("@t", talonPed);
        upd.Parameters.AddWithValue("@prev", proximoEnc);
        var updated = await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (updated != 1)
        {
            return (null, "PROXIMO_CONFLICT", "Conflicto al actualizar correlativo del talonario (PROXIMO).");
        }

        return (nroPedido, null, null);
    }

    private static async Task InsertCabeceraAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idGva21,
        int talonPed,
        string nroPedido,
        string codClient,
        int? idGva14,
        int? idGva10,
        int? idGva43,
        int estado,
        bool compStk,
        bool monCte,
        decimal cotiz,
        int nLista,
        decimal totalPedi,
        decimal totalExtranjera,
        DateTime fechaPedi,
        DateTime? fechaEntr,
        int condVta,
        string codVended,
        string codTransp,
        string codSucurs,
        string observaciones,
        string? idExterno,
        string usuario,
        string hora,
        DateTime now,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var hasIdExterno = await ColumnExistsAsync(connection, transaction, "GVA21", "ID_EXTERNO", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasTotalImp = await ColumnExistsAsync(connection, transaction, "GVA21", "TOTAL_PEDI_CON_IMPUESTOS", timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        var fkCols = new List<string>();
        var fkVals = new List<string>();
        if (await ColumnExistsAsync(connection, transaction, "GVA21", "ID_GVA14", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            fkCols.Add("ID_GVA14");
            fkVals.Add("@idGva14");
        }

        if (await ColumnExistsAsync(connection, transaction, "GVA21", "ID_GVA10", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            fkCols.Add("ID_GVA10");
            fkVals.Add("@idGva10");
        }

        if (await ColumnExistsAsync(connection, transaction, "GVA21", "ID_GVA43_TALON_PED", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            fkCols.Add("ID_GVA43_TALON_PED");
            fkVals.Add("@idGva43");
        }

        var fkColSql = fkCols.Count == 0 ? string.Empty : ", " + string.Join(", ", fkCols);
        var fkValSql = fkVals.Count == 0 ? string.Empty : ", " + string.Join(", ", fkVals);

        var sql = $"""
            INSERT INTO dbo.GVA21 (
                ID_GVA21, TALON_PED, NRO_PEDIDO, COD_CLIENT, FECHA_PEDI, FECHA_ENTR,
                ESTADO, COMP_STK, MON_CTE, COTIZ, N_LISTA, COND_VTA,
                COD_VENDED, COD_TRANSP, COD_SUCURS, OBSERVACIONES, TOTAL_PEDI
                {(hasTotalImp ? ", TOTAL_PEDI_CON_IMPUESTOS" : "")}
                {(hasIdExterno ? ", ID_EXTERNO" : "")}
                {fkColSql},
                HORA, HORA_INGRESO, FECHA_INGRESO, USUARIO_INGRESO, TERMINAL_INGRESO
            ) VALUES (
                @id, @talon, @nro, @client, @fechaPedi, @fechaEntr,
                @estado, @compStk, @monCte, @cotiz, @nLista, @condVta,
                @vended, @transp, @sucurs, @obs, @total
                {(hasTotalImp ? ", @total" : "")}
                {(hasIdExterno ? ", @idExt" : "")}
                {fkValSql},
                @hora, @hora, @now, @usuario, @terminal
            )
            """;

        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id", idGva21);
        cmd.Parameters.AddWithValue("@talon", talonPed);
        cmd.Parameters.AddWithValue("@nro", nroPedido);
        cmd.Parameters.AddWithValue("@client", codClient);
        cmd.Parameters.AddWithValue("@fechaPedi", fechaPedi.Date);
        cmd.Parameters.AddWithValue("@fechaEntr", (object?)fechaEntr?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@estado", estado);
        cmd.Parameters.AddWithValue("@compStk", compStk ? 1 : 0);
        cmd.Parameters.AddWithValue("@monCte", monCte ? 1 : 0);
        cmd.Parameters.AddWithValue("@cotiz", cotiz);
        cmd.Parameters.AddWithValue("@nLista", nLista);
        cmd.Parameters.AddWithValue("@condVta", condVta);
        cmd.Parameters.AddWithValue("@vended", codVended);
        cmd.Parameters.AddWithValue("@transp", codTransp);
        cmd.Parameters.AddWithValue("@sucurs", codSucurs);
        cmd.Parameters.AddWithValue("@obs", observaciones);
        cmd.Parameters.AddWithValue("@total", totalPedi);
        if (hasIdExterno)
        {
            cmd.Parameters.AddWithValue("@idExt", (object?)idExterno ?? DBNull.Value);
        }

        cmd.Parameters.AddWithValue("@idGva14", (object?)idGva14 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idGva10", (object?)idGva10 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idGva43", (object?)idGva43 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hora", hora);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@terminal", "Api-PaqSuiteWeb");
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertRenglonAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idGva03,
        int idGva21,
        int talonPed,
        string nroPedido,
        int nRenglon,
        RenglonInput renglon,
        string codDeposi,
        decimal precioNeto,
        decimal importe,
        int? idSta11,
        int? idSta22,
        int? idGva10,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            INSERT INTO dbo.GVA03 (
                ID_GVA03, ID_GVA21, TALON_PED, NRO_PEDIDO, N_RENGLON,
                COD_ARTICU, COD_DEPOSI, CANT_PEDID, CANT_PEN_D, CANT_PEN_F,
                CANT_A_DES, CANT_A_FAC, PRECIO, DESCUENTO, PRECIO_NETO,
                ID_STA11, ID_STA22, ID_GVA10
            ) VALUES (
                @id03, @id21, @talon, @nro, @nReng,
                @art, @dep, @cant, @cant, @cant,
                @cant, @cant, @precio, @dto, @neto,
                @sta11, @sta22, @lista
            )
            """);
        cmd.Parameters.AddWithValue("@id03", idGva03);
        cmd.Parameters.AddWithValue("@id21", idGva21);
        cmd.Parameters.AddWithValue("@talon", talonPed);
        cmd.Parameters.AddWithValue("@nro", nroPedido);
        cmd.Parameters.AddWithValue("@nReng", nRenglon);
        cmd.Parameters.AddWithValue("@art", renglon.CodArticu);
        cmd.Parameters.AddWithValue("@dep", codDeposi);
        cmd.Parameters.AddWithValue("@cant", renglon.CantPedid);
        cmd.Parameters.AddWithValue("@precio", renglon.Precio);
        cmd.Parameters.AddWithValue("@dto", renglon.Descuento);
        cmd.Parameters.AddWithValue("@neto", precioNeto);
        cmd.Parameters.AddWithValue("@sta11", (object?)idSta11 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sta22", (object?)idSta22 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lista", (object?)idGva10 ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task IncrementarSta19Async(
        SqlConnection connection,
        SqlTransaction transaction,
        int idSta11,
        int idSta22,
        decimal cantidad,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "STA19", timeoutSeconds, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        await using var upd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            UPDATE dbo.STA19
            SET CANT_COMP = ISNULL(CANT_COMP, 0) + @cant
            WHERE ID_STA11 = @a AND ID_STA22 = @d
            """);
        upd.Parameters.AddWithValue("@cant", cantidad);
        upd.Parameters.AddWithValue("@a", idSta11);
        upd.Parameters.AddWithValue("@d", idSta22);
        var n = await upd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (n > 0)
        {
            return;
        }

        await using var ins = CreateCommand(
            connection, transaction, timeoutSeconds,
            "INSERT INTO dbo.STA19 (ID_STA11, ID_STA22, CANT_COMP) VALUES (@a, @d, @cant)");
        ins.Parameters.AddWithValue("@a", idSta11);
        ins.Parameters.AddWithValue("@d", idSta22);
        ins.Parameters.AddWithValue("@cant", cantidad);
        await ins.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TalonarioPedOkAsync(
        SqlConnection connection, SqlTransaction transaction, int talonPed, int timeoutSeconds, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds,
            """
            SELECT TOP 1 1
            FROM dbo.GVA43
            WHERE TALONARIO = @t
              AND UPPER(LTRIM(RTRIM(CAST(COMPROB AS NVARCHAR(10))))) = N'PED'
              AND (HABILITADO IS NULL OR HABILITADO = 1 OR HABILITADO = 'S' OR HABILITADO = 0x01)
            """);
        // HABILITADO varies — fallback simpler:
        cmd.CommandText = """
            SELECT TOP 1 1 FROM dbo.GVA43
            WHERE TALONARIO = @t
              AND UPPER(LTRIM(RTRIM(CAST(COMPROB AS NVARCHAR(10))))) = N'PED'
            """;
        cmd.Parameters.AddWithValue("@t", talonPed);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<bool> ClienteExisteAsync(
        SqlConnection c, SqlTransaction t, string cod, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT TOP 1 1 FROM dbo.GVA14 WHERE LTRIM(RTRIM(COD_CLIENT))=@c AND (HABILITADO IS NULL OR HABILITADO=1)");
        cmd.CommandText = "SELECT TOP 1 1 FROM dbo.GVA14 WHERE LTRIM(RTRIM(COD_CLIENT))=@c";
        cmd.Parameters.AddWithValue("@c", cod);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<bool> IdExternoExisteAsync(
        SqlConnection c, SqlTransaction t, string idExt, int timeout, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(c, t, "GVA21", "ID_EXTERNO", timeout, ct).ConfigureAwait(false))
        {
            return false;
        }

        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT TOP 1 1 FROM dbo.GVA21 WHERE LTRIM(RTRIM(CAST(ID_EXTERNO AS NVARCHAR(50))))=@x");
        cmd.Parameters.AddWithValue("@x", idExt);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<bool> PedidoExisteAsync(
        SqlConnection c, SqlTransaction t, int talon, string nro, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT TOP 1 1 FROM dbo.GVA21 WHERE TALON_PED=@t AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50))))=@n");
        cmd.Parameters.AddWithValue("@t", talon);
        cmd.Parameters.AddWithValue("@n", nro);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null && o is not DBNull;
    }

    private static async Task<(bool ApruebaPe, bool CompStk)> ReadParametrosAsync(
        SqlConnection c, SqlTransaction t, int timeout, CancellationToken ct)
    {
        var aprueba = true;
        var compStk = false;
        if (!await TableExistsAsync(c, t, "GVA16", timeout, ct).ConfigureAwait(false))
        {
            return (aprueba, compStk);
        }

        await using var cmd = CreateCommand(c, t, timeout, "SELECT TOP 1 * FROM dbo.GVA16");
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            aprueba = ReadBoolLoose(reader, "APRUEBA_PE") ?? true;
            compStk = ReadBoolLoose(reader, "COMP_STK") ?? false;
        }

        await reader.CloseAsync().ConfigureAwait(false);
        return (aprueba, compStk);
    }

    private static async Task<int> NextIdAsync(
        SqlConnection c, SqlTransaction t, string sequence, string table, string idCol, int timeout, CancellationToken ct)
    {
        try
        {
            await using var cmd = CreateCommand(c, t, timeout, $"SELECT NEXT VALUE FOR dbo.{sequence}");
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(o);
        }
        catch
        {
            await using var cmd = CreateCommand(c, t, timeout, $"SELECT ISNULL(MAX({idCol}),0)+1 FROM dbo.{table}");
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(o);
        }
    }

    private static async Task<int?> LookupIdAsync(
        SqlConnection c, SqlTransaction t, string sql, (string name, object value) param, int timeout, CancellationToken ct)
    {
        try
        {
            await using var cmd = CreateCommand(c, t, timeout, sql);
            cmd.Parameters.AddWithValue(param.name, param.value);
            var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return o is null or DBNull ? null : Convert.ToInt32(o);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection c, SqlTransaction t, string table, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT 1 FROM sys.tables WHERE name=@n");
        cmd.Parameters.AddWithValue("@n", table);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection c, SqlTransaction t, string table, string column, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout,
            "SELECT 1 FROM sys.columns c INNER JOIN sys.tables tb ON tb.object_id=c.object_id WHERE tb.name=@t AND c.name=@c");
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o is not null;
    }

    private static async Task<byte[]?> ScalarBytesAsync(
        SqlConnection c, SqlTransaction t, string sql, (string name, object value) param, int timeout, CancellationToken ct)
    {
        await using var cmd = CreateCommand(c, t, timeout, sql);
        cmd.Parameters.AddWithValue(param.name, param.value);
        var o = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return o as byte[];
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection, SqlTransaction transaction, int timeoutSeconds, string sql) =>
        new(sql, connection, transaction)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };

    private static bool? ReadBoolLoose(SqlDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
            {
                return null;
            }

            var v = reader.GetValue(ordinal);
            return v switch
            {
                bool b => b,
                byte by => by != 0,
                short s => s != 0,
                int i => i != 0,
                _ => Convert.ToInt32(v) != 0
            };
        }
        catch
        {
            return null;
        }
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

    private static decimal? ExtractDecimal(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDecimal(),
            JsonElement je when je.ValueKind == JsonValueKind.String
                && decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
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
            string s when bool.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind is JsonValueKind.True or JsonValueKind.False => je.GetBoolean(),
            _ => null
        };
    }

    private static DateTime? ExtractDate(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        var s = ExtractString(parameters, key);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt
            : null;
    }

    private static string? GetJsonString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

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
