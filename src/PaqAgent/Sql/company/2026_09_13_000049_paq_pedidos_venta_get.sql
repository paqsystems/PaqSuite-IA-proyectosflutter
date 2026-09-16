CREATE OR ALTER PROCEDURE dbo.PAQ_PedidosVenta_Get
    @talon_ped  INT,
    @nro_pedido NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @nro NVARCHAR(50) = LTRIM(RTRIM(ISNULL(@nro_pedido, N'')));

    IF OBJECT_ID(N'dbo.GVA21', N'U') IS NULL
    BEGIN
        -- RS0 vacío = NOT_FOUND en el runner
        SELECT CAST(NULL AS INT) AS idGva21 WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS idGva03 WHERE 1 = 0;
        SELECT CAST(NULL AS NVARCHAR(200)) AS razonSoci WHERE 1 = 0;
        SELECT CAST(NULL AS NVARCHAR(50)) AS codigoImpuesto WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS nroCuota WHERE 1 = 0;
        RETURN;
    END

    DECLARE @hasIdGva21 BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'ID_GVA21') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasIdExterno BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'ID_EXTERNO') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasTotalImp BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'TOTAL_PEDI_CON_IMPUESTOS') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasRowVersion BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'ROW_VERSION') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasFechaEntr BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'FECHA_ENTR') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCompStk BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COMP_STK') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasMonCte BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'MON_CTE') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCotiz BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COTIZ') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasNLista BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'N_LISTA') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCondVta BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COND_VTA') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCodVended BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COD_VENDED') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCodTransp BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COD_TRANSP') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasObs BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'OBSERVACIONES') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasTotalPerc BIT = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'TOTAL_PERCEPCIONES') IS NOT NULL THEN 1 ELSE 0 END;

    -- RS0 cabecera
    DECLARE @sqlCab NVARCHAR(MAX) = N'
        SELECT TOP 1
            ' + CASE WHEN @hasIdGva21 = 1 THEN N'CAST(c.ID_GVA21 AS INT)' ELSE N'CAST(NULL AS INT)' END + N' AS idGva21,
            CAST(c.TALON_PED AS INT) AS talonPed,
            LTRIM(RTRIM(CAST(c.NRO_PEDIDO AS NVARCHAR(50)))) AS nroPedido,
            ' + CASE WHEN COL_LENGTH(N'dbo.GVA21', N'ESTADO') IS NOT NULL
                THEN N'CAST(c.ESTADO AS INT)' ELSE N'CAST(NULL AS INT)' END + N' AS estado,
            ' + CASE WHEN COL_LENGTH(N'dbo.GVA21', N'TOTAL_PEDI') IS NOT NULL
                THEN N'CAST(c.TOTAL_PEDI AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS totalPedi,
            ' + CASE WHEN @hasTotalImp = 1
                THEN N'CAST(c.TOTAL_PEDI_CON_IMPUESTOS AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS totalPediConImpuestos,
            ' + CASE WHEN @hasRowVersion = 1
                THEN N'CAST(c.ROW_VERSION AS VARBINARY(8))'
                ELSE N'CAST(NULL AS VARBINARY(8))' END + N' AS rowVersion,
            ' + CASE WHEN @hasIdExterno = 1
                THEN N'NULLIF(LTRIM(RTRIM(CAST(c.ID_EXTERNO AS NVARCHAR(100)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(100))' END + N' AS idExterno,
            ' + CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COD_CLIENT') IS NOT NULL
                THEN N'LTRIM(RTRIM(CAST(c.COD_CLIENT AS NVARCHAR(20))))'
                ELSE N'CAST(N'''' AS NVARCHAR(20))' END + N' AS codClient,
            ' + CASE WHEN COL_LENGTH(N'dbo.GVA21', N'FECHA_PEDI') IS NOT NULL
                THEN N'CONVERT(VARCHAR(10), c.FECHA_PEDI, 23)' ELSE N'CAST(NULL AS VARCHAR(10))' END + N' AS fechaPedi,
            ' + CASE WHEN @hasFechaEntr = 1
                THEN N'CONVERT(VARCHAR(10), c.FECHA_ENTR, 23)' ELSE N'CAST(NULL AS VARCHAR(10))' END + N' AS fechaEntr,
            ' + CASE WHEN @hasCompStk = 1 THEN N'CAST(c.COMP_STK AS BIT)' ELSE N'CAST(0 AS BIT)' END + N' AS compStk,
            ' + CASE WHEN @hasMonCte = 1 THEN N'CAST(c.MON_CTE AS BIT)' ELSE N'CAST(1 AS BIT)' END + N' AS monCte,
            ' + CASE WHEN @hasCotiz = 1 THEN N'CAST(c.COTIZ AS FLOAT)' ELSE N'CAST(1 AS FLOAT)' END + N' AS cotiz,
            ' + CASE WHEN @hasNLista = 1 THEN N'CAST(c.N_LISTA AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS nLista,
            ' + CASE WHEN @hasCondVta = 1 THEN N'CAST(c.COND_VTA AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS condVta,
            ' + CASE WHEN @hasCodVended = 1
                THEN N'LTRIM(RTRIM(CAST(c.COD_VENDED AS NVARCHAR(20))))' ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS codVended,
            ' + CASE WHEN @hasCodTransp = 1
                THEN N'LTRIM(RTRIM(CAST(c.COD_TRANSP AS NVARCHAR(20))))' ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS codTransp,
            ' + CASE WHEN @hasObs = 1
                THEN N'LTRIM(RTRIM(CAST(c.OBSERVACIONES AS NVARCHAR(MAX))))' ELSE N'CAST(NULL AS NVARCHAR(MAX))' END + N' AS observaciones,
            ' + CASE WHEN @hasTotalPerc = 1
                THEN N'CAST(c.TOTAL_PERCEPCIONES AS FLOAT)' ELSE N'CAST(NULL AS FLOAT)' END + N' AS totalPercepciones
        FROM dbo.GVA21 c
        WHERE c.TALON_PED = @p_talon
          AND LTRIM(RTRIM(CAST(c.NRO_PEDIDO AS NVARCHAR(50)))) = @p_nro';

    EXEC sp_executesql @sqlCab,
        N'@p_talon INT, @p_nro NVARCHAR(50)',
        @p_talon = @talon_ped, @p_nro = @nro;

    DECLARE @idGva21 INT = NULL;
    DECLARE @codClient NVARCHAR(20) = NULL;

    IF @hasIdGva21 = 1
    BEGIN
        SELECT TOP 1
            @idGva21 = CAST(ID_GVA21 AS INT),
            @codClient = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COD_CLIENT') IS NOT NULL
                THEN LTRIM(RTRIM(CAST(COD_CLIENT AS NVARCHAR(20)))) ELSE N'' END
        FROM dbo.GVA21
        WHERE TALON_PED = @talon_ped
          AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @nro;
    END
    ELSE
    BEGIN
        SELECT TOP 1
            @codClient = CASE WHEN COL_LENGTH(N'dbo.GVA21', N'COD_CLIENT') IS NOT NULL
                THEN LTRIM(RTRIM(CAST(COD_CLIENT AS NVARCHAR(20)))) ELSE N'' END
        FROM dbo.GVA21
        WHERE TALON_PED = @talon_ped
          AND LTRIM(RTRIM(CAST(NRO_PEDIDO AS NVARCHAR(50)))) = @nro;
    END

    -- RS1 renglones
    IF OBJECT_ID(N'dbo.GVA03', N'U') IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS idGva03 WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @hasIdGva03 BIT = CASE WHEN COL_LENGTH(N'dbo.GVA03', N'ID_GVA03') IS NOT NULL THEN 1 ELSE 0 END;
        DECLARE @hasIdPed03 BIT = CASE WHEN COL_LENGTH(N'dbo.GVA03', N'ID_GVA21') IS NOT NULL THEN 1 ELSE 0 END;
        DECLARE @nRengCol SYSNAME =
            CASE WHEN COL_LENGTH(N'dbo.GVA03', N'N_RENGLON') IS NOT NULL THEN N'N_RENGLON'
                 WHEN COL_LENGTH(N'dbo.GVA03', N'NRO_ORDEN') IS NOT NULL THEN N'NRO_ORDEN'
                 ELSE NULL END;
        DECLARE @depCol SYSNAME =
            CASE WHEN COL_LENGTH(N'dbo.GVA03', N'COD_DEPOSI') IS NOT NULL THEN N'COD_DEPOSI'
                 WHEN COL_LENGTH(N'dbo.GVA03', N'COD_SUCURS') IS NOT NULL THEN N'COD_SUCURS'
                 ELSE NULL END;
        DECLARE @cantPedCol SYSNAME =
            CASE WHEN COL_LENGTH(N'dbo.GVA03', N'CANT_PEDID') IS NOT NULL THEN N'CANT_PEDID'
                 WHEN COL_LENGTH(N'dbo.GVA03', N'CANTIDAD') IS NOT NULL THEN N'CANTIDAD'
                 ELSE NULL END;

        DECLARE @whereReng NVARCHAR(500);
        IF @hasIdPed03 = 1 AND @idGva21 IS NOT NULL AND @idGva21 > 0
            SET @whereReng = N'r.ID_GVA21 = @p_id';
        ELSE
            SET @whereReng = N'r.TALON_PED = @p_talon AND LTRIM(RTRIM(CAST(r.NRO_PEDIDO AS NVARCHAR(50)))) = @p_nro';

        DECLARE @sqlReng NVARCHAR(MAX) = N'
            SELECT
                ' + CASE WHEN @hasIdGva03 = 1 THEN N'CAST(r.ID_GVA03 AS INT)' ELSE N'CAST(NULL AS INT)' END + N' AS idGva03,
                ' + CASE WHEN @nRengCol IS NOT NULL THEN N'CAST(r.' + QUOTENAME(@nRengCol) + N' AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS nRenglon,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA03', N'COD_ARTICU') IS NOT NULL
                    THEN N'NULLIF(LTRIM(RTRIM(CAST(r.COD_ARTICU AS NVARCHAR(30)))), N'''')'
                    ELSE N'CAST(NULL AS NVARCHAR(30))' END + N' AS codArticu,
                ' + CASE WHEN @depCol IS NOT NULL
                    THEN N'NULLIF(LTRIM(RTRIM(CAST(r.' + QUOTENAME(@depCol) + N' AS NVARCHAR(20)))), N'''')'
                    ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS codDeposi,
                ' + CASE WHEN @cantPedCol IS NOT NULL THEN N'CAST(r.' + QUOTENAME(@cantPedCol) + N' AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS cantPedid,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA03', N'CANT_PEN_D') IS NOT NULL THEN N'CAST(r.CANT_PEN_D AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS cantPenD,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA03', N'CANT_PEN_F') IS NOT NULL THEN N'CAST(r.CANT_PEN_F AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS cantPenF,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA03', N'CANT_A_DES') IS NOT NULL THEN N'CAST(r.CANT_A_DES AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS cantADes,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA03', N'CANT_A_FAC') IS NOT NULL THEN N'CAST(r.CANT_A_FAC AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS cantAFac,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA03', N'PRECIO') IS NOT NULL THEN N'CAST(r.PRECIO AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS precio
            FROM dbo.GVA03 r
            WHERE ' + @whereReng;

        EXEC sp_executesql @sqlReng,
            N'@p_id INT, @p_talon INT, @p_nro NVARCHAR(50)',
            @p_id = @idGva21, @p_talon = @talon_ped, @p_nro = @nro;
    END

    -- RS2 datosCliente (ocasional 000000)
    IF OBJECT_ID(N'dbo.GVA38', N'U') IS NULL
       OR @idGva21 IS NULL OR @idGva21 <= 0
       OR LTRIM(RTRIM(ISNULL(@codClient, N''))) <> N'000000'
       OR COL_LENGTH(N'dbo.GVA38', N'ID_GVA21') IS NULL
    BEGIN
        SELECT CAST(NULL AS NVARCHAR(200)) AS razonSoci WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @sqlCli NVARCHAR(MAX) = N'
            SELECT TOP 1
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA38', N'RAZON_SOCI') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(d.RAZON_SOCI AS NVARCHAR(200))))' ELSE N'CAST(NULL AS NVARCHAR(200))' END + N' AS razonSoci,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA38', N'TIPO_DOC') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(d.TIPO_DOC AS NVARCHAR(20))))' ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS tipoDoc,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA38', N'N_CUIT') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(d.N_CUIT AS NVARCHAR(20))))' ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS nCuit,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA38', N'COD_PROVIN') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(d.COD_PROVIN AS NVARCHAR(20))))' ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS codProvin,
                ' + CASE WHEN COL_LENGTH(N'dbo.GVA38', N'ID_CATEGORIA_IVA') IS NOT NULL
                    THEN N'CAST(d.ID_CATEGORIA_IVA AS INT)' ELSE N'CAST(NULL AS INT)' END + N' AS categoriaIva
            FROM dbo.GVA38 d
            WHERE d.ID_GVA21 = @p_id';

        EXEC sp_executesql @sqlCli, N'@p_id INT', @p_id = @idGva21;
    END

    -- RS3 impuestos (columnas Delta6 típicas; vacío si tabla ausente)
    IF OBJECT_ID(N'dbo.PEDIDO_IMPUESTO', N'U') IS NULL
       OR @idGva21 IS NULL OR @idGva21 <= 0
       OR COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'ID_GVA21') IS NULL
    BEGIN
        SELECT CAST(NULL AS NVARCHAR(50)) AS codigoImpuesto WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @sqlImp NVARCHAR(MAX) = N'
            SELECT
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'CODIGO_IMPUESTO') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(i.CODIGO_IMPUESTO AS NVARCHAR(50))))' ELSE N'CAST(NULL AS NVARCHAR(50))' END + N' AS codigoImpuesto,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'DESCRIPCION_IMPUESTO') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(i.DESCRIPCION_IMPUESTO AS NVARCHAR(200))))' ELSE N'CAST(NULL AS NVARCHAR(200))' END + N' AS descripcion,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'TIPO_IMPUESTO') IS NOT NULL
                    THEN N'CAST(i.TIPO_IMPUESTO AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS tipo,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'TIPO_IMPUESTO_DESCRIPCION') IS NOT NULL
                    THEN N'LTRIM(RTRIM(CAST(i.TIPO_IMPUESTO_DESCRIPCION AS NVARCHAR(100))))' ELSE N'CAST(NULL AS NVARCHAR(100))' END + N' AS descripcionTipo,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'CODIGO_ALICUOTA') IS NOT NULL
                    THEN N'CAST(i.CODIGO_ALICUOTA AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS codigoAlicuota,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'ALICUOTA_PORCENTAJE') IS NOT NULL
                    THEN N'CAST(i.ALICUOTA_PORCENTAJE AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS porcentaje,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'BASE_IMPONIBLE') IS NOT NULL
                    THEN N'CAST(i.BASE_IMPONIBLE AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS baseImponible,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_IMPUESTO', N'IMPORTE') IS NOT NULL
                    THEN N'CAST(i.IMPORTE AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS importe
            FROM dbo.PEDIDO_IMPUESTO i
            WHERE i.ID_GVA21 = @p_id';
        EXEC sp_executesql @sqlImp, N'@p_id INT', @p_id = @idGva21;
    END

    -- RS4 cuotas
    IF OBJECT_ID(N'dbo.PEDIDO_CUOTA', N'U') IS NULL
       OR @idGva21 IS NULL OR @idGva21 <= 0
       OR COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'ID_GVA21') IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS nroCuota WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @orderCuota NVARCHAR(100) =
            CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'NRO_CUOTA') IS NOT NULL
                THEN N'q.NRO_CUOTA' ELSE N'(SELECT NULL)' END;
        DECLARE @sqlCuo NVARCHAR(MAX) = N'
            SELECT
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'NRO_CUOTA') IS NOT NULL
                    THEN N'CAST(q.NRO_CUOTA AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS nroCuota,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'CANTIDAD_CUOTAS') IS NOT NULL
                    THEN N'CAST(q.CANTIDAD_CUOTAS AS INT)' ELSE N'CAST(0 AS INT)' END + N' AS cantidadCuotas,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'FECHA_VTO') IS NOT NULL
                    THEN N'CONVERT(VARCHAR(10), q.FECHA_VTO, 23)' ELSE N'CAST(NULL AS VARCHAR(10))' END + N' AS fechaVto,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'IMPORTE_VTO') IS NOT NULL
                    THEN N'CAST(q.IMPORTE_VTO AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS importeVto,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'IMPORTE_VTO_EXTRANJERA') IS NOT NULL
                    THEN N'CAST(q.IMPORTE_VTO_EXTRANJERA AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS importeVtoExtranjera,
                ' + CASE WHEN COL_LENGTH(N'dbo.PEDIDO_CUOTA', N'PORCENTAJE') IS NOT NULL
                    THEN N'CAST(q.PORCENTAJE AS FLOAT)' ELSE N'CAST(0 AS FLOAT)' END + N' AS porcentaje
            FROM dbo.PEDIDO_CUOTA q
            WHERE q.ID_GVA21 = @p_id
            ORDER BY ' + @orderCuota;
        EXEC sp_executesql @sqlCuo, N'@p_id INT', @p_id = @idGva21;
    END
END
