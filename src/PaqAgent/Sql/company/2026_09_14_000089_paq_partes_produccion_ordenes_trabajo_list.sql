CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_OrdenesTrabajoList
    @Agrupado BIT = 1,
    @Page INT = 1,
    @PageSize INT = 50,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL,
    @FilterEstado INT = NULL,
    @FilterCodigo NVARCHAR(100) = NULL,
    @FilterDescripcion NVARCHAR(200) = NULL,
    @FilterFechaDesde DATE = NULL,
    @FilterFechaHasta DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PageNorm INT = CASE WHEN ISNULL(@Page, 1) < 1 THEN 1 ELSE @Page END;
    DECLARE @PageSizeNorm INT = CASE
        WHEN ISNULL(@PageSize, 50) < 1 THEN 50
        WHEN @PageSize > 100 THEN 100
        ELSE @PageSize
    END;
    DECLARE @AgrupadoNorm BIT = ISNULL(@Agrupado, 1);
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'ASC')))) = N'DESC' THEN N'DESC' ELSE N'ASC' END;
    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'CODIGO_OT'))));
    DECLARE @Total INT = 0;
    DECLARE @TotalPages INT = 0;
    DECLARE @Offset INT = 0;
    DECLARE @HasNroOrden BIT = CASE WHEN COL_LENGTH(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'NRO_ORDEN') IS NULL THEN 0 ELSE 1 END;
    DECLARE @ArtTable SYSNAME = NULL;
    DECLARE @ArtJoin NVARCHAR(MAX) = N'';
    DECLARE @ArtSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
        CAST(NULL AS NVARCHAR(250)) AS articulo_label';
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

    IF OBJECT_ID(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'U') IS NULL
    BEGIN
        SELECT
            CAST(1 AS INT) AS [page],
            CAST(@PageSizeNorm AS INT) AS page_size,
            CAST(0 AS INT) AS total,
            CAST(0 AS INT) AS total_pages,
            CAST(@AgrupadoNorm AS BIT) AS agrupado;

        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_orden_trabajo_representativo,
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

    IF @SortCol NOT IN (
        N'CODIGO_OT', N'DESCRIPCION', N'ESTADO', N'FECHA_INICIO_PLAN',
        N'FECHA_FIN_PLAN', N'ID_ORDEN_TRABAJO', N'CANTIDAD_A_PRODUCIR'
    )
        SET @SortCol = N'CODIGO_OT';

    IF @AgrupadoNorm = 1
    BEGIN
        ;WITH filtered AS (
            SELECT ot.*
            FROM dbo.PQ_PRD_ORDENES_TRABAJO ot
            WHERE (@FilterCodigo IS NULL OR ot.CODIGO_OT LIKE N'%' + @FilterCodigo + N'%')
              AND (@FilterDescripcion IS NULL OR ot.DESCRIPCION LIKE N'%' + @FilterDescripcion + N'%')
              AND (
                    @FilterFechaDesde IS NULL
                    OR ot.FECHA_INICIO_PLAN >= @FilterFechaDesde
                    OR ot.FECHA_FIN_PLAN >= @FilterFechaDesde
                  )
              AND (
                    @FilterFechaHasta IS NULL
                    OR ot.FECHA_INICIO_PLAN <= @FilterFechaHasta
                    OR ot.FECHA_FIN_PLAN <= @FilterFechaHasta
                  )
        ),
        grouped AS (
            SELECT
                f.CODIGO_OT,
                MIN(f.ID_ORDEN_TRABAJO) AS ID_ORDEN_TRABAJO_REP,
                MAX(f.ESTADO) AS ESTADO_AGG
            FROM filtered f
            GROUP BY f.CODIGO_OT
            HAVING (@FilterEstado IS NULL OR MAX(f.ESTADO) = @FilterEstado)
        )
        SELECT @Total = COUNT(1) FROM grouped;

        SET @TotalPages = CASE WHEN @Total > 0 THEN CEILING(1.0 * @Total / @PageSizeNorm) ELSE 0 END;
        IF @TotalPages > 0 AND @PageNorm > @TotalPages
            SET @PageNorm = @TotalPages;
        SET @Offset = (@PageNorm - 1) * @PageSizeNorm;

        SELECT
            CAST(@PageNorm AS INT) AS [page],
            CAST(@PageSizeNorm AS INT) AS page_size,
            CAST(@Total AS INT) AS total,
            CAST(@TotalPages AS INT) AS total_pages,
            CAST(1 AS BIT) AS agrupado;

        DECLARE @OrderAgrupado NVARCHAR(200) =
            CASE @SortCol
                WHEN N'DESCRIPCION' THEN N'r.DESCRIPCION'
                WHEN N'ESTADO' THEN N'g.ESTADO_AGG'
                WHEN N'FECHA_INICIO_PLAN' THEN N'r.FECHA_INICIO_PLAN'
                WHEN N'FECHA_FIN_PLAN' THEN N'r.FECHA_FIN_PLAN'
                WHEN N'ID_ORDEN_TRABAJO' THEN N'g.ID_ORDEN_TRABAJO_REP'
                WHEN N'CANTIDAD_A_PRODUCIR' THEN N'r.CANTIDAD_A_PRODUCIR'
                ELSE N'g.CODIGO_OT'
            END;

        SET @sql = N'
        ;WITH filtered AS (
            SELECT ot.*
            FROM dbo.PQ_PRD_ORDENES_TRABAJO ot
            WHERE (@FilterCodigo IS NULL OR ot.CODIGO_OT LIKE N''%'' + @FilterCodigo + N''%'')
              AND (@FilterDescripcion IS NULL OR ot.DESCRIPCION LIKE N''%'' + @FilterDescripcion + N''%'')
              AND (
                    @FilterFechaDesde IS NULL
                    OR ot.FECHA_INICIO_PLAN >= @FilterFechaDesde
                    OR ot.FECHA_FIN_PLAN >= @FilterFechaDesde
                  )
              AND (
                    @FilterFechaHasta IS NULL
                    OR ot.FECHA_INICIO_PLAN <= @FilterFechaHasta
                    OR ot.FECHA_FIN_PLAN <= @FilterFechaHasta
                  )
        ),
        grouped AS (
            SELECT
                f.CODIGO_OT,
                MIN(f.ID_ORDEN_TRABAJO) AS ID_ORDEN_TRABAJO_REP,
                MAX(f.ESTADO) AS ESTADO_AGG
            FROM filtered f
            GROUP BY f.CODIGO_OT
            HAVING (@FilterEstado IS NULL OR MAX(f.ESTADO) = @FilterEstado)
        )
        SELECT
            CAST(g.ID_ORDEN_TRABAJO_REP AS INT) AS id,
            CAST(g.ID_ORDEN_TRABAJO_REP AS INT) AS id_orden_trabajo_representativo,
            LTRIM(RTRIM(CAST(g.CODIGO_OT AS NVARCHAR(50)))) AS codigo,
            CAST(r.TIPO_REF_EXTERNA AS NVARCHAR(50)) AS tipo_ref_externa,
            CAST(r.ID_REF_EXTERNA AS NVARCHAR(50)) AS id_ref_externa,
            CAST(r.DESCRIPCION AS NVARCHAR(200)) AS descripcion,
            CAST(r.ID_ARTICULO AS INT) AS id_articulo,
            ' + @ArtSelect + N',
            CAST(NULL AS INT) AS id_operacion,
            CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
            CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
            CAST(NULL AS NVARCHAR(160)) AS operacion_label,
            CAST(ISNULL(r.CANTIDAD_A_PRODUCIR, 0) AS INT) AS cantidad_a_producir,
            CAST(r.FECHA_INICIO_PLAN AS DATE) AS fecha_inicio_plan,
            CAST(r.FECHA_FIN_PLAN AS DATE) AS fecha_fin_plan,
            CAST(g.ESTADO_AGG AS INT) AS estado,
            CASE CAST(g.ESTADO_AGG AS INT)
                WHEN 0 THEN N''Borrador''
                WHEN 1 THEN N''Abierta''
                WHEN 2 THEN N''Cerrada''
                ELSE N''Desconocido''
            END AS estado_label,
            CAST(r.OBSERVACIONES AS NVARCHAR(MAX)) AS observaciones,
            CAST(r.FECHA_ALTA AS DATETIME) AS fecha_alta,
            CAST(NULL AS INT) AS nro_orden
        FROM grouped g
        INNER JOIN dbo.PQ_PRD_ORDENES_TRABAJO r ON r.ID_ORDEN_TRABAJO = g.ID_ORDEN_TRABAJO_REP
        ' + @ArtJoin + N'
        ORDER BY ' + @OrderAgrupado + N' ' + @DirNorm + N'
        OFFSET @Offset ROWS FETCH NEXT @PageSizeNorm ROWS ONLY;';

        SET @params = N'@FilterEstado INT, @FilterCodigo NVARCHAR(100), @FilterDescripcion NVARCHAR(200),
            @FilterFechaDesde DATE, @FilterFechaHasta DATE, @Offset INT, @PageSizeNorm INT';

        EXEC sp_executesql
            @sql,
            @params,
            @FilterEstado = @FilterEstado,
            @FilterCodigo = @FilterCodigo,
            @FilterDescripcion = @FilterDescripcion,
            @FilterFechaDesde = @FilterFechaDesde,
            @FilterFechaHasta = @FilterFechaHasta,
            @Offset = @Offset,
            @PageSizeNorm = @PageSizeNorm;
        RETURN;
    END

    -- Modo por fila
    SELECT @Total = COUNT(1)
    FROM dbo.PQ_PRD_ORDENES_TRABAJO ot
    WHERE (@FilterEstado IS NULL OR ot.ESTADO = @FilterEstado)
      AND (@FilterCodigo IS NULL OR ot.CODIGO_OT LIKE N'%' + @FilterCodigo + N'%')
      AND (@FilterDescripcion IS NULL OR ot.DESCRIPCION LIKE N'%' + @FilterDescripcion + N'%')
      AND (
            @FilterFechaDesde IS NULL
            OR ot.FECHA_INICIO_PLAN >= @FilterFechaDesde
            OR ot.FECHA_FIN_PLAN >= @FilterFechaDesde
          )
      AND (
            @FilterFechaHasta IS NULL
            OR ot.FECHA_INICIO_PLAN <= @FilterFechaHasta
            OR ot.FECHA_FIN_PLAN <= @FilterFechaHasta
          );

    SET @TotalPages = CASE WHEN @Total > 0 THEN CEILING(1.0 * @Total / @PageSizeNorm) ELSE 0 END;
    IF @TotalPages > 0 AND @PageNorm > @TotalPages
        SET @PageNorm = @TotalPages;
    SET @Offset = (@PageNorm - 1) * @PageSizeNorm;

    SELECT
        CAST(@PageNorm AS INT) AS [page],
        CAST(@PageSizeNorm AS INT) AS page_size,
        CAST(@Total AS INT) AS total,
        CAST(@TotalPages AS INT) AS total_pages,
        CAST(0 AS BIT) AS agrupado;

    DECLARE @OrderFila NVARCHAR(200) =
        CASE @SortCol
            WHEN N'DESCRIPCION' THEN N'r.DESCRIPCION'
            WHEN N'ESTADO' THEN N'r.ESTADO'
            WHEN N'FECHA_INICIO_PLAN' THEN N'r.FECHA_INICIO_PLAN'
            WHEN N'FECHA_FIN_PLAN' THEN N'r.FECHA_FIN_PLAN'
            WHEN N'ID_ORDEN_TRABAJO' THEN N'r.ID_ORDEN_TRABAJO'
            WHEN N'CANTIDAD_A_PRODUCIR' THEN N'r.CANTIDAD_A_PRODUCIR'
            ELSE N'r.CODIGO_OT'
        END;

    DECLARE @OpJoin NVARCHAR(MAX) = N'';
    DECLARE @OpSelect NVARCHAR(MAX) = N'
        CAST(CASE WHEN r.ID_OPERACION IS NOT NULL AND r.ID_OPERACION > 0 THEN r.ID_OPERACION ELSE NULL END AS INT) AS id_operacion,
        CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
        CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
        CAST(NULL AS NVARCHAR(160)) AS operacion_label';

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

    DECLARE @NroSelect NVARCHAR(200) = CASE WHEN @HasNroOrden = 1
        THEN N'CAST(ISNULL(r.NRO_ORDEN, 0) AS INT) AS nro_orden'
        ELSE N'CAST(NULL AS INT) AS nro_orden'
    END;

    SET @sql = N'
    SELECT
        CAST(r.ID_ORDEN_TRABAJO AS INT) AS id,
        CAST(NULL AS INT) AS id_orden_trabajo_representativo,
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
    WHERE (@FilterEstado IS NULL OR r.ESTADO = @FilterEstado)
      AND (@FilterCodigo IS NULL OR r.CODIGO_OT LIKE N''%'' + @FilterCodigo + N''%'')
      AND (@FilterDescripcion IS NULL OR r.DESCRIPCION LIKE N''%'' + @FilterDescripcion + N''%'')
      AND (
            @FilterFechaDesde IS NULL
            OR r.FECHA_INICIO_PLAN >= @FilterFechaDesde
            OR r.FECHA_FIN_PLAN >= @FilterFechaDesde
          )
      AND (
            @FilterFechaHasta IS NULL
            OR r.FECHA_INICIO_PLAN <= @FilterFechaHasta
            OR r.FECHA_FIN_PLAN <= @FilterFechaHasta
          )
    ORDER BY ' + @OrderFila + N' ' + @DirNorm + N'
    OFFSET @Offset ROWS FETCH NEXT @PageSizeNorm ROWS ONLY;';

    SET @params = N'@FilterEstado INT, @FilterCodigo NVARCHAR(100), @FilterDescripcion NVARCHAR(200),
        @FilterFechaDesde DATE, @FilterFechaHasta DATE, @Offset INT, @PageSizeNorm INT';

    EXEC sp_executesql
        @sql,
        @params,
        @FilterEstado = @FilterEstado,
        @FilterCodigo = @FilterCodigo,
        @FilterDescripcion = @FilterDescripcion,
        @FilterFechaDesde = @FilterFechaDesde,
        @FilterFechaHasta = @FilterFechaHasta,
        @Offset = @Offset,
        @PageSizeNorm = @PageSizeNorm;
END
GO
