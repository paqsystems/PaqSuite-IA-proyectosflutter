CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_OrdenesTrabajoGet
    @IdOrdenTrabajo INT = NULL,
    @CodigoOt NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdNorm INT = ISNULL(@IdOrdenTrabajo, 0);
    DECLARE @CodigoNorm NVARCHAR(50) = LTRIM(RTRIM(ISNULL(@CodigoOt, N'')));
    DECLARE @HasNroOrden BIT = CASE WHEN COL_LENGTH(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'NRO_ORDEN') IS NULL THEN 0 ELSE 1 END;
    DECLARE @ArtTable SYSNAME = NULL;
    DECLARE @ArtJoin NVARCHAR(MAX) = N'';
    DECLARE @ArtSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
        CAST(NULL AS NVARCHAR(250)) AS articulo_label';
    DECLARE @OpJoin NVARCHAR(MAX) = N'';
    DECLARE @OpSelect NVARCHAR(MAX) = N'
        CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
        CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
        CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
        CAST(NULL AS NVARCHAR(160)) AS operacion_label';
    DECLARE @NroSelect NVARCHAR(200) = CASE WHEN @HasNroOrden = 1
        THEN N'CAST(ISNULL(r.NRO_ORDEN, 0) AS INT) AS nro_orden'
        ELSE N'CAST(NULL AS INT) AS nro_orden'
    END;
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX);

    IF OBJECT_ID(N'dbo.pq_vwarticulos', N'V') IS NOT NULL
       AND COL_LENGTH(N'dbo.pq_vwarticulos', N'ID_STA11') IS NOT NULL
       AND COL_LENGTH(N'dbo.pq_vwarticulos', N'COD_ARTICU') IS NOT NULL
       AND COL_LENGTH(N'dbo.pq_vwarticulos', N'DESCRIPCIO') IS NOT NULL
        SET @ArtTable = N'dbo.pq_vwarticulos';
    ELSE IF OBJECT_ID(N'dbo.STA11', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.STA11', N'ID_STA11') IS NOT NULL
       AND COL_LENGTH(N'dbo.STA11', N'COD_ARTICU') IS NOT NULL
       AND COL_LENGTH(N'dbo.STA11', N'DESCRIPCIO') IS NOT NULL
        SET @ArtTable = N'dbo.STA11';

    IF @ArtTable IS NOT NULL
    BEGIN
        SET @ArtJoin = N'
        LEFT JOIN ' + @ArtTable + N' art ON art.ID_STA11 = r.ID_ARTICULO';
        SET @ArtSelect = N'
        LTRIM(RTRIM(CAST(art.COD_ARTICU AS NVARCHAR(50)))) AS articulo_codigo,
        CASE
            WHEN art.COD_ARTICU IS NULL THEN CAST(NULL AS NVARCHAR(250))
            ELSE LTRIM(RTRIM(CAST(art.COD_ARTICU AS NVARCHAR(50)))) + N'' – '' + LTRIM(RTRIM(CAST(ISNULL(art.DESCRIPCIO, N'''') AS NVARCHAR(200))))
        END AS articulo_label';
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_OPERACIONES', N'U') IS NOT NULL
    BEGIN
        SET @OpJoin = N'
        LEFT JOIN dbo.PQ_PRD_OPERACIONES op ON op.ID_OPERACION = r.ID_OPERACION';
        SET @OpSelect = N'
        CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
        LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) AS operacion_codigo,
        LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100)))) AS operacion_nombre,
        CASE
            WHEN op.CODIGO_OPERACION IS NOT NULL AND op.NOMBRE IS NOT NULL
                THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) + N'' – '' + LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
            WHEN op.NOMBRE IS NOT NULL THEN LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
            WHEN op.CODIGO_OPERACION IS NOT NULL THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50))))
            ELSE CAST(NULL AS NVARCHAR(160))
        END AS operacion_label';
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(50)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS tipo_ref_externa,
            CAST(NULL AS NVARCHAR(50)) AS id_ref_externa,
            CAST(NULL AS NVARCHAR(200)) AS descripcion,
            CAST(NULL AS INT) AS id_articulo,
            CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
            CAST(NULL AS NVARCHAR(250)) AS articulo_label,
            CAST(NULL AS INT) AS id_operacion,
            CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
            CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
            CAST(NULL AS NVARCHAR(160)) AS operacion_label,
            CAST(NULL AS INT) AS cantidad_a_producir,
            CAST(NULL AS DATE) AS fecha_inicio_plan,
            CAST(NULL AS DATE) AS fecha_fin_plan,
            CAST(NULL AS INT) AS estado,
            CAST(NULL AS NVARCHAR(20)) AS estado_label,
            CAST(NULL AS NVARCHAR(MAX)) AS observaciones,
            CAST(NULL AS DATETIME) AS fecha_alta,
            CAST(NULL AS INT) AS nro_orden
        WHERE 1 = 0;
        RETURN;
    END

    SET @sql = N'
    SELECT
        CAST(r.ID_ORDEN_TRABAJO AS INT) AS id,
        LTRIM(RTRIM(CAST(r.CODIGO_OT AS NVARCHAR(50)))) AS codigo,
        CAST(r.TIPO_REF_EXTERNA AS NVARCHAR(50)) AS tipo_ref_externa,
        CAST(r.ID_REF_EXTERNA AS NVARCHAR(50)) AS id_ref_externa,
        CAST(r.DESCRIPCION AS NVARCHAR(200)) AS descripcion,
        CAST(r.ID_ARTICULO AS INT) AS id_articulo,
        ' + @ArtSelect + N',
        ' + @OpSelect + N',
        CAST(ISNULL(r.CANTIDAD_A_PRODUCIR, 0) AS INT) AS cantidad_a_producir,
        CAST(r.FECHA_INICIO_PLAN AS DATE) AS fecha_inicio_plan,
        CAST(r.FECHA_FIN_PLAN AS DATE) AS fecha_fin_plan,
        CAST(r.ESTADO AS INT) AS estado,
        CASE CAST(r.ESTADO AS INT)
            WHEN 0 THEN N''Borrador''
            WHEN 1 THEN N''Abierta''
            WHEN 2 THEN N''Cerrada''
            ELSE N''Desconocido''
        END AS estado_label,
        CAST(r.OBSERVACIONES AS NVARCHAR(MAX)) AS observaciones,
        CAST(r.FECHA_ALTA AS DATETIME) AS fecha_alta,
        ' + @NroSelect + N'
    FROM dbo.PQ_PRD_ORDENES_TRABAJO r
    ' + @ArtJoin + N'
    ' + @OpJoin + N'
    WHERE (
            (@IdNorm > 0 AND r.ID_ORDEN_TRABAJO = @IdNorm)
            OR (@IdNorm <= 0 AND @CodigoNorm <> N'''' AND r.CODIGO_OT = @CodigoNorm)
          )
    ORDER BY r.ID_ORDEN_TRABAJO ASC;';

    SET @params = N'@IdNorm INT, @CodigoNorm NVARCHAR(50)';

    EXEC sp_executesql
        @sql,
        @params,
        @IdNorm = @IdNorm,
        @CodigoNorm = @CodigoNorm;
END
GO
