CREATE OR ALTER PROCEDURE dbo.PAQ_MovimientosTesoreria_Get
    @cod_comp NVARCHAR(3),
    @n_comp   NVARCHAR(14),
    @barra    INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @cod NVARCHAR(3) = UPPER(LTRIM(RTRIM(ISNULL(@cod_comp, N''))));
    DECLARE @nro NVARCHAR(14) = LTRIM(RTRIM(ISNULL(@n_comp, N'')));
    DECLARE @bar INT = ISNULL(@barra, 0);

    IF OBJECT_ID(N'dbo.SBA04', N'U') IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS idSba04 WHERE 1 = 0;
        SELECT CAST(NULL AS INT) AS idSba05 WHERE 1 = 0;
        SELECT CAST(NULL AS NVARCHAR(20)) AS rol WHERE 1 = 0;
        RETURN;
    END

    DECLARE @hasIdSba04 BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'ID_SBA04') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasFechaUlt BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'FECHA_ULTIMA_MODIFICACION') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasHoraUlt BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'HORA_ULTIMA_MODIFICACION') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasExterno BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'EXTERNO') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasObs BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'OBSERVACIONES') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCodGva14 BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'COD_GVA14') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @hasCodCpa01 BIT = CASE WHEN COL_LENGTH(N'dbo.SBA04', N'COD_CPA01') IS NOT NULL THEN 1 ELSE 0 END;

    -- RS0 cabecera (vacío = NOT_FOUND en runner)
    DECLARE @sqlCab NVARCHAR(MAX) = N'
        SELECT TOP 1
            LTRIM(RTRIM(CAST(c.COD_COMP AS NVARCHAR(3)))) AS codComp,
            LTRIM(RTRIM(CAST(c.N_COMP AS NVARCHAR(14)))) AS nComp,
            CAST(c.BARRA AS INT) AS barra,
            CAST(c.N_INTERNO AS INT) AS nInterno,
            ' + CASE WHEN @hasIdSba04 = 1 THEN N'CAST(c.ID_SBA04 AS INT)' ELSE N'CAST(NULL AS INT)' END + N' AS idSba04,
            CAST(c.CLASE AS INT) AS clase,
            LTRIM(RTRIM(CAST(c.SITUACION AS NVARCHAR(20)))) AS situacion,
            ' + CASE WHEN @hasExterno = 1 THEN N'CAST(c.EXTERNO AS BIT)' ELSE N'CAST(0 AS BIT)' END + N' AS externo,
            ' + CASE WHEN COL_LENGTH(N'dbo.SBA04', N'FECHA') IS NOT NULL
                THEN N'CONVERT(VARCHAR(10), c.FECHA, 23)' ELSE N'CAST(NULL AS VARCHAR(10))' END + N' AS fecha,
            ' + CASE WHEN COL_LENGTH(N'dbo.SBA04', N'CONCEPTO') IS NOT NULL
                THEN N'NULLIF(LTRIM(RTRIM(CAST(c.CONCEPTO AS NVARCHAR(200)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(200))' END + N' AS concepto,
            ' + CASE WHEN COL_LENGTH(N'dbo.SBA04', N'COTIZACION') IS NOT NULL
                THEN N'CAST(c.COTIZACION AS FLOAT)' ELSE N'CAST(NULL AS FLOAT)' END + N' AS cotizacion,
            ' + CASE WHEN @hasCodGva14 = 1
                THEN N'NULLIF(LTRIM(RTRIM(CAST(c.COD_GVA14 AS NVARCHAR(20)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS codClient,
            ' + CASE WHEN @hasCodCpa01 = 1
                THEN N'NULLIF(LTRIM(RTRIM(CAST(c.COD_CPA01 AS NVARCHAR(20)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(20))' END + N' AS codProvee,
            ' + CASE WHEN @hasObs = 1
                THEN N'NULLIF(LTRIM(RTRIM(CAST(c.OBSERVACIONES AS NVARCHAR(MAX)))), N'''')'
                ELSE N'CAST(NULL AS NVARCHAR(MAX))' END + N' AS observaciones,
            ' + CASE WHEN @hasFechaUlt = 1
                THEN N'CONVERT(VARCHAR(19), c.FECHA_ULTIMA_MODIFICACION, 120)'
                ELSE N'CAST(N'''' AS VARCHAR(19))' END + N' AS fechaUltimaModificacion,
            ' + CASE WHEN @hasHoraUlt = 1
                THEN N'LTRIM(RTRIM(CAST(c.HORA_ULTIMA_MODIFICACION AS NVARCHAR(20))))'
                ELSE N'CAST(N'''' AS NVARCHAR(20))' END + N' AS horaUltimaModificacion
        FROM dbo.SBA04 c
        WHERE UPPER(LTRIM(RTRIM(CAST(c.COD_COMP AS NVARCHAR(3))))) = @p_cod
          AND LTRIM(RTRIM(CAST(c.N_COMP AS NVARCHAR(14)))) = @p_nro
          AND CAST(c.BARRA AS INT) = @p_bar';

    EXEC sp_executesql @sqlCab,
        N'@p_cod NVARCHAR(3), @p_nro NVARCHAR(14), @p_bar INT',
        @p_cod = @cod, @p_nro = @nro, @p_bar = @bar;

    DECLARE @idSba04 INT = NULL;
    IF @hasIdSba04 = 1
    BEGIN
        SELECT TOP 1 @idSba04 = CAST(ID_SBA04 AS INT)
        FROM dbo.SBA04
        WHERE UPPER(LTRIM(RTRIM(CAST(COD_COMP AS NVARCHAR(3))))) = @cod
          AND LTRIM(RTRIM(CAST(N_COMP AS NVARCHAR(14)))) = @nro
          AND CAST(BARRA AS INT) = @bar;
    END

    -- RS1 renglones
    IF OBJECT_ID(N'dbo.SBA05', N'U') IS NULL OR @idSba04 IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS idSba05 WHERE 1 = 0;
    END
    ELSE
    BEGIN
        DECLARE @sqlRen NVARCHAR(MAX) = N'
            SELECT
                CAST(r.RENGLON AS INT) AS renglon,
                CAST(r.COD_CTA AS INT) AS codCta,
                LTRIM(RTRIM(CAST(r.D_H AS NVARCHAR(1)))) AS dH,
                CAST(r.MONTO AS FLOAT) AS monto,
                ' + CASE WHEN COL_LENGTH(N'dbo.SBA05', N'LEYENDA') IS NOT NULL
                    THEN N'NULLIF(LTRIM(RTRIM(CAST(r.LEYENDA AS NVARCHAR(200)))), N'''')'
                    ELSE N'CAST(NULL AS NVARCHAR(200))' END + N' AS leyenda,
                ' + CASE WHEN COL_LENGTH(N'dbo.SBA05', N'ID_SBA05') IS NOT NULL
                    THEN N'CAST(r.ID_SBA05 AS INT)' ELSE N'CAST(NULL AS INT)' END + N' AS idSba05
            FROM dbo.SBA05 r
            WHERE r.ID_SBA04 = @p_id
            ORDER BY r.RENGLON';

        EXEC sp_executesql @sqlRen, N'@p_id INT', @p_id = @idSba04;
    END

    -- RS2 reversion (SBA27 opcional; vacío si no hay vínculo)
    IF OBJECT_ID(N'dbo.SBA27', N'U') IS NULL
    BEGIN
        SELECT CAST(NULL AS NVARCHAR(20)) AS rol WHERE 1 = 0;
        RETURN;
    END

    DECLARE @tCompOri SYSNAME =
        CASE
            WHEN COL_LENGTH(N'dbo.SBA27', N'T_COMP_ORI') IS NOT NULL THEN N'T_COMP_ORI'
            WHEN COL_LENGTH(N'dbo.SBA27', N'COD_COMP_ORI') IS NOT NULL THEN N'COD_COMP_ORI'
            ELSE NULL
        END;
    DECLARE @tCompRev SYSNAME =
        CASE
            WHEN COL_LENGTH(N'dbo.SBA27', N'T_COMP_REV') IS NOT NULL THEN N'T_COMP_REV'
            WHEN COL_LENGTH(N'dbo.SBA27', N'COD_COMP_REV') IS NOT NULL THEN N'COD_COMP_REV'
            ELSE NULL
        END;

    IF @tCompOri IS NULL OR @tCompRev IS NULL
       OR COL_LENGTH(N'dbo.SBA27', N'N_COMP_ORI') IS NULL
       OR COL_LENGTH(N'dbo.SBA27', N'N_COMP_REV') IS NULL
    BEGIN
        SELECT CAST(NULL AS NVARCHAR(20)) AS rol WHERE 1 = 0;
        RETURN;
    END

    DECLARE @barraOriExpr NVARCHAR(200) =
        CASE WHEN COL_LENGTH(N'dbo.SBA27', N'BARRA_ORI') IS NOT NULL
            THEN N'CAST(ISNULL(v.BARRA_ORI, 0) AS INT)' ELSE N'CAST(0 AS INT)' END;
    DECLARE @barraRevExpr NVARCHAR(200) =
        CASE WHEN COL_LENGTH(N'dbo.SBA27', N'BARRA_REV') IS NOT NULL
            THEN N'CAST(ISNULL(v.BARRA_REV, 0) AS INT)' ELSE N'CAST(0 AS INT)' END;

    DECLARE @sqlRev NVARCHAR(MAX) = N'
        SELECT TOP 1
            x.rol,
            x.codCompRev,
            x.nCompRev,
            x.barraRev,
            x.codCompOri,
            x.nCompOri,
            x.barraOri
        FROM (
            SELECT
                N''origen'' AS rol,
                LTRIM(RTRIM(CAST(v.' + QUOTENAME(@tCompRev) + N' AS NVARCHAR(3)))) AS codCompRev,
                LTRIM(RTRIM(CAST(v.N_COMP_REV AS NVARCHAR(14)))) AS nCompRev,
                ' + @barraRevExpr + N' AS barraRev,
                CAST(NULL AS NVARCHAR(3)) AS codCompOri,
                CAST(NULL AS NVARCHAR(14)) AS nCompOri,
                CAST(NULL AS INT) AS barraOri,
                0 AS prioridad
            FROM dbo.SBA27 v
            WHERE UPPER(LTRIM(RTRIM(CAST(v.' + QUOTENAME(@tCompOri) + N' AS NVARCHAR(3))))) = @p_cod
              AND LTRIM(RTRIM(CAST(v.N_COMP_ORI AS NVARCHAR(14)))) = @p_nro
              AND ' + @barraOriExpr + N' = @p_bar
            UNION ALL
            SELECT
                N''rev'' AS rol,
                CAST(NULL AS NVARCHAR(3)) AS codCompRev,
                CAST(NULL AS NVARCHAR(14)) AS nCompRev,
                CAST(NULL AS INT) AS barraRev,
                LTRIM(RTRIM(CAST(v.' + QUOTENAME(@tCompOri) + N' AS NVARCHAR(3)))) AS codCompOri,
                LTRIM(RTRIM(CAST(v.N_COMP_ORI AS NVARCHAR(14)))) AS nCompOri,
                ' + @barraOriExpr + N' AS barraOri,
                1 AS prioridad
            FROM dbo.SBA27 v
            WHERE UPPER(LTRIM(RTRIM(CAST(v.' + QUOTENAME(@tCompRev) + N' AS NVARCHAR(3))))) = @p_cod
              AND LTRIM(RTRIM(CAST(v.N_COMP_REV AS NVARCHAR(14)))) = @p_nro
              AND ' + @barraRevExpr + N' = @p_bar
        ) x
        ORDER BY x.prioridad';

    EXEC sp_executesql @sqlRev,
        N'@p_cod NVARCHAR(3), @p_nro NVARCHAR(14), @p_bar INT',
        @p_cod = @cod, @p_nro = @nro, @p_bar = @bar;
END
GO
