CREATE OR ALTER PROCEDURE dbo.PAQ_AsientosContables_Get
    @nro_interno_analitico INT
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.ASIENTO_ANALITICO_CN', N'U') IS NULL
    BEGIN
        -- RS0 vacío = NOT_FOUND en el runner
        SELECT CAST(NULL AS INT) AS idAsientoAnaliticoCn WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS idRenglonImporteAnaliticoCn WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS idAuxiliarAnaliticoCn WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS idAuxiliarAnaliticoCn WHERE 1 = 0;
        RETURN;
    END

    DECLARE @hasRowVersion BIT = CASE WHEN COL_LENGTH(N'dbo.ASIENTO_ANALITICO_CN', N'ROW_VERSION') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasTipoAsiento BIT = CASE
        WHEN OBJECT_ID(N'dbo.TIPO_ASIENTO', N'U') IS NOT NULL
         AND COL_LENGTH(N'dbo.TIPO_ASIENTO', N'COD_TIPO_ASIENTO') IS NOT NULL
         AND COL_LENGTH(N'dbo.TIPO_ASIENTO', N'ID_TIPO_ASIENTO') IS NOT NULL
        THEN 1 ELSE 0 END;
    DECLARE @hasMoneda BIT = CASE
        WHEN OBJECT_ID(N'dbo.MONEDA', N'U') IS NOT NULL
         AND COL_LENGTH(N'dbo.MONEDA', N'COD_MONEDA') IS NOT NULL
         AND COL_LENGTH(N'dbo.MONEDA', N'ID_MONEDA') IS NOT NULL
        THEN 1 ELSE 0 END;

    -- RS0 cabecera
    DECLARE @sqlCab NVARCHAR(MAX) = N'
        SELECT TOP 1
            CAST(c.ID_ASIENTO_ANALITICO_CN AS INT) AS idAsientoAnaliticoCn,
            CAST(c.NRO_INTERNO_ANALITICO AS INT) AS nroInternoAnalitico,
            CAST(c.NRO_ASIENTO_ANALITICO AS FLOAT) AS nroAsiento,
            CONVERT(VARCHAR(10), c.FECHA_ASIENTO, 23) AS fecha,
            ' + CASE WHEN @hasTipoAsiento = 1
                THEN N'NULLIF(LTRIM(RTRIM(CAST(ta.COD_TIPO_ASIENTO AS NVARCHAR(50)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(50))' END + N' AS codTipoAsiento,
            ' + CASE WHEN @hasMoneda = 1
                THEN N'NULLIF(LTRIM(RTRIM(CAST(m.COD_MONEDA AS NVARCHAR(50)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(50))' END + N' AS codMoneda,
            ' + CASE WHEN COL_LENGTH(N'dbo.ASIENTO_ANALITICO_CN', N'DESC_ASIENTO_ANALITICO') IS NOT NULL
                THEN N'c.DESC_ASIENTO_ANALITICO' ELSE N'CAST(NULL AS NVARCHAR(100))' END + N' AS leyenda,
            ' + CASE WHEN COL_LENGTH(N'dbo.ASIENTO_ANALITICO_CN', N'OBSERVACIONES') IS NOT NULL
                THEN N'c.OBSERVACIONES' ELSE N'CAST(NULL AS NVARCHAR(1000))' END + N' AS observaciones,
            LTRIM(RTRIM(CAST(c.ESTADO_ASIENTO_ANALITICO AS NVARCHAR(20)))) AS estadoAsientoAnalitico,
            LTRIM(RTRIM(CAST(c.ESTADO_RESUMEN AS NVARCHAR(20)))) AS estadoResumen,
            ' + CASE WHEN @hasRowVersion = 1
                THEN N'CAST(c.ROW_VERSION AS VARBINARY(8))'
                ELSE N'CAST(NULL AS VARBINARY(8))' END + N' AS rowVersion
        FROM dbo.ASIENTO_ANALITICO_CN c
        ' + CASE WHEN @hasTipoAsiento = 1
            THEN N'LEFT JOIN dbo.TIPO_ASIENTO ta ON ta.ID_TIPO_ASIENTO = c.ID_TIPO_ASIENTO'
            ELSE N'' END + N'
        ' + CASE WHEN @hasMoneda = 1
            THEN N'LEFT JOIN dbo.MONEDA m ON m.ID_MONEDA = c.ID_MONEDA_ASIENTO'
            ELSE N'' END + N'
        WHERE c.NRO_INTERNO_ANALITICO = @p_nro';

    EXEC sp_executesql @sqlCab,
        N'@p_nro INT',
        @p_nro = @nro_interno_analitico;

    DECLARE @idAsiento INT = NULL;
    SELECT TOP 1 @idAsiento = CAST(ID_ASIENTO_ANALITICO_CN AS INT)
    FROM dbo.ASIENTO_ANALITICO_CN
    WHERE NRO_INTERNO_ANALITICO = @nro_interno_analitico;

    -- RS1 renglones
    IF OBJECT_ID(N'dbo.RENGLON_ANALITICO_CN', N'U') IS NULL
       OR @idAsiento IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS idRenglonImporteAnaliticoCn WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @hasImporte BIT = CASE WHEN OBJECT_ID(N'dbo.RENGLON_IMPORTE_ANALITICO_CN', N'U') IS NOT NULL THEN 1 ELSE 0 END;
        DECLARE @hasCuenta BIT = CASE
            WHEN OBJECT_ID(N'dbo.CUENTA', N'U') IS NOT NULL
             AND COL_LENGTH(N'dbo.CUENTA', N'COD_CUENTA') IS NOT NULL
             AND COL_LENGTH(N'dbo.CUENTA', N'ID_CUENTA') IS NOT NULL
            THEN 1 ELSE 0 END;

        DECLARE @sqlRen NVARCHAR(MAX) = N'
            SELECT
                ' + CASE WHEN @hasImporte = 1
                    THEN N'CAST(ri.ID_RENGLON_IMPORTE_ANALITICO_CN AS INT)'
                    ELSE N'CAST(NULL AS INT)' END + N' AS idRenglonImporteAnaliticoCn,
                CAST(r.NRO_RENGLON_ANALITICO AS INT) AS renglon,
                ' + CASE WHEN @hasCuenta = 1
                    THEN N'NULLIF(LTRIM(RTRIM(CAST(c.COD_CUENTA AS NVARCHAR(50)))), N'''')'
                    ELSE N'CAST(NULL AS NVARCHAR(50))' END + N' AS codCuenta,
                LTRIM(RTRIM(CAST(r.D_H AS NVARCHAR(1)))) AS dH,
                ' + CASE WHEN @hasImporte = 1
                    THEN N'CAST(ISNULL(ri.IMPORTE_RENGLON, 0) AS FLOAT)'
                    ELSE N'CAST(0 AS FLOAT)' END + N' AS importe,
                r.DESC_LEYENDA AS leyenda
            FROM dbo.RENGLON_ANALITICO_CN r
            ' + CASE WHEN @hasImporte = 1
                THEN N'LEFT JOIN dbo.RENGLON_IMPORTE_ANALITICO_CN ri ON ri.ID_RENGLON_ANALITICO_CN = r.ID_RENGLON_ANALITICO_CN'
                ELSE N'' END + N'
            ' + CASE WHEN @hasCuenta = 1
                THEN N'LEFT JOIN dbo.CUENTA c ON c.ID_CUENTA = r.ID_CUENTA'
                ELSE N'' END + N'
            WHERE r.ID_ASIENTO_ANALITICO_CN = @p_id
            ORDER BY r.NRO_RENGLON_ANALITICO';

        EXEC sp_executesql @sqlRen, N'@p_id INT', @p_id = @idAsiento;
    END

    -- RS2 auxiliares (vacío si tabla ausente)
    IF OBJECT_ID(N'dbo.AUXILIAR_ANALITICO_CN', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AUXILIAR', N'U') IS NULL
       OR @idAsiento IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS idAuxiliarAnaliticoCn WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @hasTipoAux BIT = CASE
            WHEN OBJECT_ID(N'dbo.TIPO_AUXILIAR', N'U') IS NOT NULL
             AND COL_LENGTH(N'dbo.TIPO_AUXILIAR', N'COD_TIPO_AUXILIAR') IS NOT NULL
            THEN 1 ELSE 0 END;

        DECLARE @sqlAux NVARCHAR(MAX) = N'
            SELECT
                CAST(aa.ID_RENGLON_IMPORTE_ANALITICO_CN AS INT) AS idRenglonImporteAnaliticoCn,
                CAST(aa.ID_AUXILIAR_ANALITICO_CN AS INT) AS idAuxiliarAnaliticoCn,
                ' + CASE WHEN @hasTipoAux = 1
                    THEN N'LTRIM(RTRIM(CAST(ISNULL(ta.COD_TIPO_AUXILIAR, N'''') AS NVARCHAR(50))))'
                    ELSE N'CAST(N'''' AS NVARCHAR(50))' END + N' AS codTipoAuxiliar,
                LTRIM(RTRIM(CAST(ax.COD_AUXILIAR AS NVARCHAR(50)))) AS codAuxiliar,
                CAST(aa.IMPORTE_RENGLON AS FLOAT) AS importe,
                CAST(aa.PORC_APROPIACION AS FLOAT) AS porcentaje
            FROM dbo.AUXILIAR_ANALITICO_CN aa
            INNER JOIN dbo.AUXILIAR ax ON ax.ID_AUXILIAR = aa.ID_AUXILIAR
            INNER JOIN dbo.RENGLON_IMPORTE_ANALITICO_CN ri
                ON ri.ID_RENGLON_IMPORTE_ANALITICO_CN = aa.ID_RENGLON_IMPORTE_ANALITICO_CN
            INNER JOIN dbo.RENGLON_ANALITICO_CN r
                ON r.ID_RENGLON_ANALITICO_CN = ri.ID_RENGLON_ANALITICO_CN
            ' + CASE WHEN @hasTipoAux = 1
                THEN N'LEFT JOIN dbo.TIPO_AUXILIAR ta ON ta.ID_TIPO_AUXILIAR = ax.ID_TIPO_AUXILIAR'
                ELSE N'' END + N'
            WHERE r.ID_ASIENTO_ANALITICO_CN = @p_id';

        EXEC sp_executesql @sqlAux, N'@p_id INT', @p_id = @idAsiento;
    END

    -- RS3 subauxiliares (vacío si tabla ausente)
    IF OBJECT_ID(N'dbo.SUBAUXILIAR_ANALITICO_CN', N'U') IS NULL
       OR OBJECT_ID(N'dbo.SUBAUXILIAR', N'U') IS NULL
       OR @idAsiento IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS idAuxiliarAnaliticoCn WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @sqlSub NVARCHAR(MAX) = N'
            SELECT
                CAST(sa.ID_AUXILIAR_ANALITICO_CN AS INT) AS idAuxiliarAnaliticoCn,
                LTRIM(RTRIM(CAST(sx.COD_SUBAUXILIAR AS NVARCHAR(50)))) AS codSubauxiliar,
                CAST(sa.IMPORTE_RENGLON AS FLOAT) AS importe,
                CAST(sa.PORC_APROPIACION AS FLOAT) AS porcentaje
            FROM dbo.SUBAUXILIAR_ANALITICO_CN sa
            INNER JOIN dbo.SUBAUXILIAR sx ON sx.ID_SUBAUXILIAR = sa.ID_SUBAUXILIAR
            INNER JOIN dbo.AUXILIAR_ANALITICO_CN aa
                ON aa.ID_AUXILIAR_ANALITICO_CN = sa.ID_AUXILIAR_ANALITICO_CN
            INNER JOIN dbo.RENGLON_IMPORTE_ANALITICO_CN ri
                ON ri.ID_RENGLON_IMPORTE_ANALITICO_CN = aa.ID_RENGLON_IMPORTE_ANALITICO_CN
            INNER JOIN dbo.RENGLON_ANALITICO_CN r
                ON r.ID_RENGLON_ANALITICO_CN = ri.ID_RENGLON_ANALITICO_CN
            WHERE r.ID_ASIENTO_ANALITICO_CN = @p_id';

        EXEC sp_executesql @sqlSub, N'@p_id INT', @p_id = @idAsiento;
    END
END
