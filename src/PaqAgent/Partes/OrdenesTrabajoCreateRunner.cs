using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Partes;

/// <summary>
/// D6.8.3 — alta Órdenes de trabajo orquestada en agente (espejo PHP OrdenTrabajoController::store / OrdenesTrabajoService::storeLocal).
/// Multi-fila + numeración + validaciones de catálogo. Sin SP monolítico.
/// </summary>
public sealed class OrdenesTrabajoCreateRunner
{
    private const string ProgramaPartes = "PartesProduccion";

    public async Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var database = ExtractString(parameters, "_database");
        var idArticulo = ExtractInt(parameters, "id_articulo");
        var cantidad = ExtractInt(parameters, "cantidad_a_producir");

        if (string.IsNullOrWhiteSpace(database) || idArticulo is null or <= 0 || cantidad is null or <= 0)
        {
            return Fail(
                "INVALID_PARAMETERS",
                "id_articulo, cantidad_a_producir y _database son obligatorios.");
        }

        if (!agentOptions.HasSqlConfig)
        {
            return new PartesOutcome
            {
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        var modoIndividual = ExtractBool(parameters, "modo_individual");
        List<int> idOperaciones;
        try
        {
            idOperaciones = ResolveOperaciones(parameters, modoIndividual);
        }
        catch (ValidationException vex)
        {
            return Fail("VALIDATION", vex.Message);
        }
        catch (Exception ex)
        {
            return Fail("INVALID_PARAMETERS", ex.Message);
        }

        if (idOperaciones.Count == 0)
        {
            return Fail("VALIDATION", "Indique al menos una operación.");
        }

        if (modoIndividual is null)
        {
            modoIndividual = parameters.ContainsKey("id_operacion")
                && !parameters.ContainsKey("operaciones_incluidas_json")
                && !parameters.ContainsKey("operaciones_incluidas");
        }

        var codigoInformado = Truncate(ExtractString(parameters, "codigo")?.Trim(), 30);
        var descripcion = Truncate(ExtractString(parameters, "descripcion")?.Trim(), 200);
        var observaciones = ExtractString(parameters, "observaciones")?.Trim();
        var fechaInicio = NormalizeDate(ExtractString(parameters, "fecha_inicio_plan"));
        var fechaFin = NormalizeDate(ExtractString(parameters, "fecha_fin_plan"));
        var tipoRef = Truncate(ExtractString(parameters, "tipo_ref_externa")?.Trim(), 20);
        var idRef = Truncate(ExtractString(parameters, "id_ref_externa")?.Trim(), 50);
        var usuarioId = ExtractInt(parameters, "usuario_id") ?? 0;

        if (fechaInicio is not null && fechaFin is not null
            && string.CompareOrdinal(fechaFin, fechaInicio) < 0)
        {
            return Fail("VALIDATION", "fecha_fin_plan debe ser >= fecha_inicio_plan.");
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
                if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new ValidationException("Tabla PQ_PRD_ORDENES_TRABAJO no disponible.");
                }

                if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PRD_OPERACIONES", cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new ValidationException("Catálogo de operaciones no disponible.");
                }

                var otParams = await LoadOtParamsAsync(connection, transaction, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var idOp in idOperaciones)
                {
                    if (!await OperacionActivaExistsAsync(connection, transaction, timeoutSeconds, idOp, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        throw new ValidationException($"La operación {idOp} no existe o no está activa.");
                    }
                }

                if (!await ArticuloExistsAsync(connection, transaction, timeoutSeconds, idArticulo.Value, cancellationToken)
                        .ConfigureAwait(false))
                {
                    throw new ValidationException("El artículo seleccionado no existe en el catálogo.");
                }

                var fechaRef = fechaInicio ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                foreach (var idOp in idOperaciones)
                {
                    if (!await ParStdVigenteAsync(
                            connection, transaction, timeoutSeconds, idArticulo.Value, idOp, fechaRef, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        throw new ValidationException(
                            $"La operación {idOp} no está habilitada para el artículo seleccionado (estándar vigente).");
                    }
                }

                string codigoFinal;
                if (otParams.NumeracionAutomatica)
                {
                    codigoFinal = await GenerarCodigoAutomaticoAsync(
                            connection, transaction, timeoutSeconds, otParams, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var msgManual = ValidateManualCodigo(codigoInformado ?? string.Empty, otParams);
                    if (msgManual is not null)
                    {
                        throw new ValidationException(msgManual);
                    }

                    codigoFinal = codigoInformado!;
                }

                var operaciones = await ResolverOperacionesConNroOrdenAsync(
                        connection,
                        transaction,
                        timeoutSeconds,
                        idArticulo.Value,
                        idOperaciones,
                        fechaRef,
                        otParams.IncrementoOrden,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (operaciones.Count == 0)
                {
                    throw new ValidationException("No se pudo resolver el orden de las operaciones seleccionadas.");
                }

                var hasNroOrden = await ColumnExistsAsync(
                        connection, transaction, timeoutSeconds, "PQ_PRD_ORDENES_TRABAJO", "NRO_ORDEN", cancellationToken)
                    .ConfigureAwait(false);

                foreach (var op in operaciones)
                {
                    if (await CodigoNroExistsAsync(
                            connection, transaction, timeoutSeconds, codigoFinal, op.NroOrden, hasNroOrden, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var msg = otParams.NumeracionAutomatica
                            ? "El código de OT generado ya existe; reintente el alta."
                            : "El código de OT ya existe para una de las operaciones indicadas.";
                        throw new ConflictException(msg);
                    }
                }

                var estado = otParams.IniciaEnBorrador ? 0 : 1;
                var createdIds = new List<int>();
                foreach (var op in operaciones)
                {
                    var id = await InsertOrdenAsync(
                            connection,
                            transaction,
                            timeoutSeconds,
                            codigoFinal,
                            tipoRef,
                            idRef,
                            string.IsNullOrWhiteSpace(descripcion) ? null : descripcion,
                            idArticulo.Value,
                            op.IdOperacion,
                            op.NroOrden,
                            cantidad.Value,
                            fechaInicio,
                            fechaFin,
                            estado,
                            string.IsNullOrWhiteSpace(observaciones) ? null : observaciones,
                            usuarioId,
                            hasNroOrden,
                            cancellationToken)
                        .ConfigureAwait(false);
                    createdIds.Add(id);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                var items = await LoadCreatedItemsAsync(
                        connection, timeoutSeconds, createdIds, hasNroOrden, cancellationToken)
                    .ConfigureAwait(false);

                if (items.Count == 0)
                {
                    return Fail("SQL_ERROR", "No se pudo recuperar la orden de trabajo creada.");
                }

                var payload = new Dictionary<string, object?>(items[0], StringComparer.OrdinalIgnoreCase)
                {
                    ["items"] = items
                };
                return Ok(payload);
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
            catch (SqlException sqlEx) when (sqlEx.Number is 2627 or 2601)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Fail("CONFLICT", "El código de OT ya existe para una de las operaciones indicadas.");
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

    private static List<int> ResolveOperaciones(
        IReadOnlyDictionary<string, object?> parameters,
        bool? modoIndividual)
    {
        var individual = modoIndividual
            ?? (parameters.ContainsKey("id_operacion")
                && !parameters.ContainsKey("operaciones_incluidas_json")
                && !parameters.ContainsKey("operaciones_incluidas"));

        if (individual)
        {
            var idOp = ExtractInt(parameters, "id_operacion");
            if (idOp is null or <= 0)
            {
                throw new ValidationException("id_operacion es obligatorio en modo individual.");
            }

            return [idOp.Value];
        }

        if (parameters.TryGetValue("operaciones_incluidas_json", out var jsonRaw) && jsonRaw is not null)
        {
            var json = ExtractString(parameters, "operaciones_incluidas_json");
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ValidationException("Indique al menos una operación a incluir.");
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("operaciones_incluidas_json debe ser un array JSON.");
            }

            var list = new List<int>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && n > 0)
                {
                    list.Add(n);
                }
                else if (el.ValueKind == JsonValueKind.String
                         && int.TryParse(el.GetString(), out var p)
                         && p > 0)
                {
                    list.Add(p);
                }
            }

            return list.Distinct().ToList();
        }

        if (parameters.TryGetValue("operaciones_incluidas", out var arrRaw) && arrRaw is not null)
        {
            if (arrRaw is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                var list = new List<int>();
                foreach (var el in je.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) && n > 0)
                    {
                        list.Add(n);
                    }
                }

                return list.Distinct().ToList();
            }

            if (arrRaw is IEnumerable<object?> enumerable)
            {
                return enumerable
                    .Select(x => x switch
                    {
                        int i => i,
                        long l => (int)l,
                        string s when int.TryParse(s, out var p) => p,
                        _ => 0
                    })
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();
            }
        }

        throw new ValidationException("Indique al menos una operación a incluir.");
    }

    private sealed record OtParams(
        bool NumeracionAutomatica,
        bool IniciaEnBorrador,
        string Prefijo,
        int DigitosNumeracion,
        int? DigitosParam,
        int IncrementoOrden);

    private static async Task<OtParams> LoadOtParamsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!await TableExistsAsync(connection, transaction, timeoutSeconds, "PQ_PARAMETROS_GRAL", cancellationToken)
                .ConfigureAwait(false))
        {
            return new OtParams(false, true, string.Empty, 6, null, 1);
        }

        const string sql = """
            SELECT Clave, Tipo_Valor, Valor_Bool, Valor_Int, Valor_Decimal, Valor_String, Valor_Text
            FROM dbo.PQ_PARAMETROS_GRAL
            WHERE LOWER([Programa]) = LOWER(@programa)
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@programa", ProgramaPartes);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var clave = reader["Clave"]?.ToString() ?? string.Empty;
            if (clave == string.Empty)
            {
                continue;
            }

            var tipo = (reader["Tipo_Valor"]?.ToString() ?? "S").Trim().ToUpperInvariant();
            object? valor = tipo switch
            {
                "B" => reader["Valor_Bool"],
                "I" => reader["Valor_Int"],
                "N" => reader["Valor_Decimal"],
                "S" => reader["Valor_String"],
                _ => reader["Valor_Text"] ?? reader["Valor_String"]
            };
            map[clave] = valor is null or DBNull ? null : Convert.ToString(valor, CultureInfo.InvariantCulture);
        }

        var numeracion = ParseBool(map.GetValueOrDefault("ot_numeracion_automatica")) ?? false;
        var iniciaBorrador = ParseBool(map.GetValueOrDefault("ot_inicia_borrador")) ?? true;
        var prefijo = (map.GetValueOrDefault("ot_prefijo_codigo") ?? string.Empty).Trim();
        int? digitosParam = null;
        if (int.TryParse(map.GetValueOrDefault("ot_digitos_numero"), out var digitosRaw))
        {
            digitosParam = digitosRaw;
        }

        var digitosNorm = digitosParam is null ? (int?)null : Math.Max(6, Math.Min(30, digitosParam.Value));
        var digitosNum = digitosNorm ?? 6;
        var incremento = 1;
        if (int.TryParse(map.GetValueOrDefault("ot_incremento_orden_operacion"), out var inc) && inc >= 1)
        {
            incremento = inc;
        }

        return new OtParams(numeracion, iniciaBorrador, prefijo, digitosNum, digitosNorm, incremento);
    }

    private static async Task<string> GenerarCodigoAutomaticoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        OtParams otParams,
        CancellationToken cancellationToken)
    {
        if (otParams.Prefijo.Length + otParams.DigitosNumeracion > 30)
        {
            throw new ValidationException(
                "La configuración de prefijo y dígitos supera los 30 caracteres permitidos para el código de OT.");
        }

        for (var intento = 0; intento < 12; intento++)
        {
            var max = 0;
            const string sql = """
                SELECT CODIGO_OT
                FROM dbo.PQ_PRD_ORDENES_TRABAJO
                WHERE CODIGO_OT LIKE @prefijo + N'%'
                """;
            await using (var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql))
            {
                cmd.Parameters.AddWithValue("@prefijo", otParams.Prefijo);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var codigo = (reader.GetString(0) ?? string.Empty).Trim();
                    if (!codigo.StartsWith(otParams.Prefijo, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var suf = codigo[otParams.Prefijo.Length..];
                    if (suf.Length > 0 && suf.All(char.IsDigit))
                    {
                        var n = int.Parse(suf, CultureInfo.InvariantCulture);
                        if (n > max)
                        {
                            max = n;
                        }
                    }
                }
            }

            var sig = max + 1;
            var numStr = sig.ToString(CultureInfo.InvariantCulture).PadLeft(otParams.DigitosNumeracion, '0');
            if (numStr.Length > otParams.DigitosNumeracion)
            {
                throw new ValidationException(
                    "Se agotó el rango de numeración automática para la cantidad de dígitos configurada.");
            }

            var codigoFinal = otParams.Prefijo + numStr;
            if (codigoFinal.Length > 30)
            {
                throw new ValidationException("El código de OT generado supera los 30 caracteres.");
            }

            if (!await CodigoExistsAsync(connection, transaction, timeoutSeconds, codigoFinal, cancellationToken)
                    .ConfigureAwait(false))
            {
                return codigoFinal;
            }
        }

        throw new ValidationException("No se pudo generar un código de OT único.");
    }

    private static string? ValidateManualCodigo(string codigo, OtParams otParams)
    {
        codigo = codigo.Trim();
        if (codigo == string.Empty)
        {
            return "El código de OT es obligatorio.";
        }

        if (codigo.Length > 30)
        {
            return "El código de OT no puede superar los 30 caracteres.";
        }

        if (otParams.DigitosParam is null)
        {
            return null;
        }

        var digitos = Math.Max(6, Math.Min(30, otParams.DigitosParam.Value));
        var prefijo = otParams.Prefijo;
        if (prefijo.Length + digitos > 30)
        {
            return null;
        }

        if (prefijo != string.Empty)
        {
            if (!codigo.StartsWith(prefijo, StringComparison.Ordinal))
            {
                return "El código de OT debe comenzar con el prefijo configurado para el módulo.";
            }

            var suf = codigo[prefijo.Length..];
            if (suf == string.Empty || !suf.All(char.IsDigit))
            {
                return "El código de OT debe incluir solo dígitos después del prefijo.";
            }

            if (suf.Length > digitos)
            {
                return "La parte numérica del código de OT supera la cantidad de dígitos permitida.";
            }
        }
        else if (codigo.All(char.IsDigit) && codigo.Length > digitos)
        {
            return "El código numérico supera la cantidad de dígitos permitida.";
        }

        return null;
    }

    private sealed record OpNro(int IdOperacion, int NroOrden);

    private static async Task<List<OpNro>> ResolverOperacionesConNroOrdenAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idArticulo,
        IReadOnlyList<int> idOperaciones,
        string fechaRef,
        int incremento,
        CancellationToken cancellationToken)
    {
        var ids = idOperaciones.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var hasStd = await TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", cancellationToken)
            .ConfigureAwait(false);
        var hasNroStd = hasStd
            && await ColumnExistsAsync(
                    connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", "NRO_ORDEN", cancellationToken)
                .ConfigureAwait(false);

        var nroPorOp = new Dictionary<int, int>();
        var incluidosAuto = new List<int>();

        if (hasStd)
        {
            var inParams = string.Join(",", ids.Select((_, i) => "@op" + i));
            var nroSelect = hasNroStd ? "NRO_ORDEN" : "CAST(0 AS INT) AS NRO_ORDEN";
            var sql = $"""
                SELECT ID_OPERACION, {nroSelect}
                FROM dbo.PQ_PRD_ARTICULO_OPERACION_STD
                WHERE ID_ARTICULO = @idArt
                  AND ID_OPERACION IN ({inParams})
                  AND ACTIVO = 1
                  AND (VIGENTE_DESDE IS NULL OR VIGENTE_DESDE <= @fecha)
                  AND (VIGENTE_HASTA IS NULL OR VIGENTE_HASTA >= @fecha)
                ORDER BY VIGENTE_DESDE DESC
                """;
            await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
            cmd.Parameters.AddWithValue("@idArt", idArticulo);
            cmd.Parameters.AddWithValue("@fecha", fechaRef);
            for (var i = 0; i < ids.Count; i++)
            {
                cmd.Parameters.AddWithValue("@op" + i, ids[i]);
            }

            var seen = new HashSet<int>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idOp = Convert.ToInt32(reader["ID_OPERACION"], CultureInfo.InvariantCulture);
                if (!seen.Add(idOp))
                {
                    continue;
                }

                var nroStd = hasNroStd ? Convert.ToInt32(reader["NRO_ORDEN"], CultureInfo.InvariantCulture) : 0;
                if (nroStd > 0)
                {
                    nroPorOp[idOp] = nroStd;
                }
                else
                {
                    incluidosAuto.Add(idOp);
                }
            }
        }

        if (incluidosAuto.Count > 0)
        {
            var sorted = incluidosAuto
                .OrderBy(id => id)
                .ToList();
            var inc = Math.Max(1, incremento);
            for (var i = 0; i < sorted.Count; i++)
            {
                nroPorOp[sorted[i]] = inc * (i + 1);
            }
        }

        var outList = new List<OpNro>();
        foreach (var idOp in ids)
        {
            if (nroPorOp.TryGetValue(idOp, out var nro))
            {
                outList.Add(new OpNro(idOp, nro));
            }
        }

        return outList.OrderBy(x => x.NroOrden).ToList();
    }

    private static async Task<int> InsertOrdenAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string codigoOt,
        string? tipoRef,
        string? idRef,
        string? descripcion,
        int idArticulo,
        int idOperacion,
        int nroOrden,
        int cantidad,
        string? fechaInicio,
        string? fechaFin,
        int estado,
        string? observaciones,
        int usuarioId,
        bool hasNroOrden,
        CancellationToken cancellationToken)
    {
        var cols = new StringBuilder(
            "CODIGO_OT, TIPO_REF_EXTERNA, ID_REF_EXTERNA, DESCRIPCION, ID_ARTICULO, ID_OPERACION, ");
        var vals = new StringBuilder(
            "@codigo, @tipoRef, @idRef, @descripcion, @idArticulo, @idOperacion, ");
        if (hasNroOrden)
        {
            cols.Append("NRO_ORDEN, ");
            vals.Append("@nroOrden, ");
        }

        cols.Append(
            "CANTIDAD_A_PRODUCIR, FECHA_INICIO_PLAN, FECHA_FIN_PLAN, ESTADO, OBSERVACIONES, FECHA_ALTA, USUARIO_ALTA");
        vals.Append(
            "@cantidad, @fechaInicio, @fechaFin, @estado, @observaciones, GETDATE(), @usuarioAlta");

        var sql = $"""
            INSERT INTO dbo.PQ_PRD_ORDENES_TRABAJO ({cols})
            OUTPUT INSERTED.ID_ORDEN_TRABAJO
            VALUES ({vals});
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@codigo", codigoOt);
        cmd.Parameters.AddWithValue("@tipoRef", (object?)tipoRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idRef", (object?)idRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@descripcion", (object?)descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idArticulo", idArticulo);
        cmd.Parameters.AddWithValue("@idOperacion", idOperacion);
        if (hasNroOrden)
        {
            cmd.Parameters.AddWithValue("@nroOrden", nroOrden);
        }

        cmd.Parameters.AddWithValue("@cantidad", cantidad);
        cmd.Parameters.AddWithValue("@fechaInicio", (object?)fechaInicio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fechaFin", (object?)fechaFin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@estado", estado);
        cmd.Parameters.AddWithValue("@observaciones", (object?)observaciones ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuarioAlta", usuarioId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<List<Dictionary<string, object?>>> LoadCreatedItemsAsync(
        SqlConnection connection,
        int timeoutSeconds,
        IReadOnlyList<int> ids,
        bool hasNroOrden,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var artTable = await ResolveArticuloTableAsync(connection, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        var hasOps = await TableExistsAsync(connection, null, timeoutSeconds, "PQ_PRD_OPERACIONES", cancellationToken)
            .ConfigureAwait(false);

        var artJoin = artTable is null
            ? string.Empty
            : $"LEFT JOIN {artTable} art ON art.ID_STA11 = r.ID_ARTICULO";
        var artSelect = artTable is null
            ? """
              CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
              CAST(NULL AS NVARCHAR(250)) AS articulo_label
              """
            : """
              LTRIM(RTRIM(CAST(art.COD_ARTICU AS NVARCHAR(50)))) AS articulo_codigo,
              CASE
                  WHEN art.COD_ARTICU IS NULL THEN CAST(NULL AS NVARCHAR(250))
                  ELSE LTRIM(RTRIM(CAST(art.COD_ARTICU AS NVARCHAR(50)))) + N' – ' + LTRIM(RTRIM(CAST(ISNULL(art.DESCRIPCIO, N'') AS NVARCHAR(200))))
              END AS articulo_label
              """;

        var opJoin = hasOps ? "LEFT JOIN dbo.PQ_PRD_OPERACIONES op ON op.ID_OPERACION = r.ID_OPERACION" : string.Empty;
        var opSelect = hasOps
            ? """
              CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
              LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) AS operacion_codigo,
              LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100)))) AS operacion_nombre,
              CASE
                  WHEN op.CODIGO_OPERACION IS NOT NULL AND op.NOMBRE IS NOT NULL
                      THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) + N' – ' + LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
                  WHEN op.NOMBRE IS NOT NULL THEN LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
                  WHEN op.CODIGO_OPERACION IS NOT NULL THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50))))
                  ELSE CAST(NULL AS NVARCHAR(160))
              END AS operacion_label
              """
            : """
              CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
              CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
              CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
              CAST(NULL AS NVARCHAR(160)) AS operacion_label
              """;

        var nroSelect = hasNroOrden
            ? "CAST(ISNULL(r.NRO_ORDEN, 0) AS INT) AS nro_orden"
            : "CAST(NULL AS INT) AS nro_orden";

        var inParams = string.Join(",", ids.Select((_, i) => "@id" + i));
        var sql = $"""
            SELECT
                CAST(r.ID_ORDEN_TRABAJO AS INT) AS id,
                LTRIM(RTRIM(CAST(r.CODIGO_OT AS NVARCHAR(50)))) AS codigo,
                LTRIM(RTRIM(CAST(r.TIPO_REF_EXTERNA AS NVARCHAR(50)))) AS tipo_ref_externa,
                r.ID_REF_EXTERNA AS id_ref_externa,
                LTRIM(RTRIM(CAST(r.DESCRIPCION AS NVARCHAR(200)))) AS descripcion,
                CAST(r.ID_ARTICULO AS INT) AS id_articulo,
                {artSelect},
                {opSelect},
                CAST(r.CANTIDAD_A_PRODUCIR AS INT) AS cantidad_a_producir,
                r.FECHA_INICIO_PLAN AS fecha_inicio_plan,
                r.FECHA_FIN_PLAN AS fecha_fin_plan,
                CAST(r.ESTADO AS INT) AS estado,
                CASE CAST(r.ESTADO AS INT)
                    WHEN 0 THEN N'Borrador'
                    WHEN 1 THEN N'Abierta'
                    WHEN 2 THEN N'Cerrada'
                    ELSE N'Desconocido'
                END AS estado_label,
                r.OBSERVACIONES AS observaciones,
                r.FECHA_ALTA AS fecha_alta,
                {nroSelect}
            FROM dbo.PQ_PRD_ORDENES_TRABAJO r
            {artJoin}
            {opJoin}
            WHERE r.ID_ORDEN_TRABAJO IN ({inParams})
            ORDER BY r.ID_ORDEN_TRABAJO
            """;

        await using var cmd = new SqlCommand(sql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };
        for (var i = 0; i < ids.Count; i++)
        {
            cmd.Parameters.AddWithValue("@id" + i, ids[i]);
        }

        var items = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var estado = reader["estado"] is DBNull ? 0 : Convert.ToInt32(reader["estado"], CultureInfo.InvariantCulture);
            var item = new Dictionary<string, object?>
            {
                ["id"] = Convert.ToInt32(reader["id"], CultureInfo.InvariantCulture),
                ["codigo"] = reader["codigo"] is DBNull ? null : reader["codigo"]?.ToString(),
                ["tipo_ref_externa"] = reader["tipo_ref_externa"] is DBNull ? null : reader["tipo_ref_externa"]?.ToString(),
                ["id_ref_externa"] = reader["id_ref_externa"] is DBNull ? null : reader["id_ref_externa"],
                ["descripcion"] = reader["descripcion"] is DBNull ? null : reader["descripcion"]?.ToString(),
                ["id_articulo"] = reader["id_articulo"] is DBNull ? null : Convert.ToInt32(reader["id_articulo"], CultureInfo.InvariantCulture),
                ["articulo_codigo"] = reader["articulo_codigo"] is DBNull ? null : reader["articulo_codigo"]?.ToString(),
                ["articulo_label"] = reader["articulo_label"] is DBNull ? null : reader["articulo_label"]?.ToString(),
                ["id_operacion"] = reader["id_operacion"] is DBNull ? null : Convert.ToInt32(reader["id_operacion"], CultureInfo.InvariantCulture),
                ["operacion_codigo"] = reader["operacion_codigo"] is DBNull ? null : reader["operacion_codigo"]?.ToString(),
                ["operacion_nombre"] = reader["operacion_nombre"] is DBNull ? null : reader["operacion_nombre"]?.ToString(),
                ["operacion_label"] = reader["operacion_label"] is DBNull ? null : reader["operacion_label"]?.ToString(),
                ["cantidad_a_producir"] = reader["cantidad_a_producir"] is DBNull
                    ? 0
                    : Convert.ToInt32(reader["cantidad_a_producir"], CultureInfo.InvariantCulture),
                ["fecha_inicio_plan"] = FormatDateOnly(reader["fecha_inicio_plan"]),
                ["fecha_fin_plan"] = FormatDateOnly(reader["fecha_fin_plan"]),
                ["estado"] = estado,
                ["estado_label"] = reader["estado_label"]?.ToString() ?? "Desconocido",
                ["observaciones"] = reader["observaciones"] is DBNull ? null : reader["observaciones"]?.ToString(),
                ["fecha_alta"] = FormatDateTime(reader["fecha_alta"])
            };
            if (hasNroOrden && reader["nro_orden"] is not DBNull)
            {
                item["nro_orden"] = Convert.ToInt32(reader["nro_orden"], CultureInfo.InvariantCulture);
            }

            items.Add(item);
        }

        return items;
    }

    private static async Task<string?> ResolveArticuloTableAsync(
        SqlConnection connection,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { "pq_vwarticulos", "STA11" })
        {
            if (await ObjectExistsAsync(connection, timeoutSeconds, candidate, cancellationToken).ConfigureAwait(false)
                && await ColumnExistsAsync(connection, null, timeoutSeconds, candidate, "ID_STA11", cancellationToken)
                    .ConfigureAwait(false)
                && await ColumnExistsAsync(connection, null, timeoutSeconds, candidate, "COD_ARTICU", cancellationToken)
                    .ConfigureAwait(false)
                && await ColumnExistsAsync(connection, null, timeoutSeconds, candidate, "DESCRIPCIO", cancellationToken)
                    .ConfigureAwait(false))
            {
                return "dbo." + candidate;
            }
        }

        return null;
    }

    private static async Task<bool> ArticuloExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idArticulo,
        CancellationToken cancellationToken)
    {
        var table = await ResolveArticuloTableAsync(connection, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (table is null)
        {
            return true;
        }

        await using var cmd = CreateCommand(
            connection, transaction, timeoutSeconds, $"SELECT TOP 1 1 FROM {table} WHERE ID_STA11 = @id");
        cmd.Parameters.AddWithValue("@id", idArticulo);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> OperacionActivaExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idOperacion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_OPERACIONES
            WHERE ID_OPERACION = @id AND ACTIVA = 1
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@id", idOperacion);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> ParStdVigenteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        int idArticulo,
        int idOperacion,
        string fechaRef,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(
                connection, transaction, timeoutSeconds, "PQ_PRD_ARTICULO_OPERACION_STD", cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_ARTICULO_OPERACION_STD
            WHERE ID_ARTICULO = @idArt
              AND ID_OPERACION = @idOp
              AND ACTIVO = 1
              AND (VIGENTE_DESDE IS NULL OR VIGENTE_DESDE <= @fecha)
              AND (VIGENTE_HASTA IS NULL OR VIGENTE_HASTA >= @fecha)
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@idArt", idArticulo);
        cmd.Parameters.AddWithValue("@idOp", idOperacion);
        cmd.Parameters.AddWithValue("@fecha", fechaRef);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> CodigoExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string codigo,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT TOP 1 1 FROM dbo.PQ_PRD_ORDENES_TRABAJO WHERE CODIGO_OT = @codigo";
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> CodigoNroExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int timeoutSeconds,
        string codigo,
        int nroOrden,
        bool hasNroOrden,
        CancellationToken cancellationToken)
    {
        if (!hasNroOrden)
        {
            return await CodigoExistsAsync(connection, transaction, timeoutSeconds, codigo, cancellationToken)
                .ConfigureAwait(false);
        }

        const string sql = """
            SELECT TOP 1 1
            FROM dbo.PQ_PRD_ORDENES_TRABAJO
            WHERE CODIGO_OT = @codigo AND NRO_ORDEN = @nro
            """;
        await using var cmd = CreateCommand(connection, transaction, timeoutSeconds, sql);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@nro", nroOrden);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection,
            transaction,
            timeoutSeconds,
            "SELECT 1 FROM sys.tables WHERE name = @n AND schema_id = SCHEMA_ID(N'dbo')");
        cmd.Parameters.AddWithValue("@n", tableName);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> ObjectExistsAsync(
        SqlConnection connection,
        int timeoutSeconds,
        string name,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(
            "SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(@n)",
            connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };
        cmd.Parameters.AddWithValue("@n", "dbo." + name);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            connection,
            transaction,
            timeoutSeconds,
            "SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c");
        cmd.Parameters.AddWithValue("@t", "dbo." + tableName);
        cmd.Parameters.AddWithValue("@c", columnName);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction? transaction,
        int timeoutSeconds,
        string sql)
    {
        var cmd = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = Math.Max(1, timeoutSeconds);
        return cmd;
    }

    private static string? FormatDateOnly(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string? FormatDateTime(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

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

        return null;
    }

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = value.Trim().ToLowerInvariant();
        if (s is "1" or "true" or "yes" or "si" or "sí" or "s")
        {
            return true;
        }

        if (s is "0" or "false" or "no" or "n")
        {
            return false;
        }

        if (bool.TryParse(s, out var b))
        {
            return b;
        }

        return null;
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

    private static PartesOutcome Ok(object data) =>
        new()
        {
            Status = JobStatuses.Success,
            Data = data
        };

    private static PartesOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };

    private sealed class ValidationException(string message) : Exception(message);

    private sealed class ConflictException(string message) : Exception(message);
}
