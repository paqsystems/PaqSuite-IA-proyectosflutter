CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_PartesOperarioPartesRevisarList
    @Page INT = 1,
    @PageSize INT = 50,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL,
    @FilterFechaDesde DATE = NULL,
    @FilterFechaHasta DATE = NULL,
    @FilterIdTurno INT = NULL,
    @FilterIdOperario INT = NULL,
    @FilterEstado INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PageNorm INT = CASE WHEN ISNULL(@Page, 1) < 1 THEN 1 ELSE @Page END;
    DECLARE @PageSizeNorm INT = CASE
        WHEN ISNULL(@PageSize, 50) < 1 THEN 50
        WHEN @PageSize > 100 THEN 100
        ELSE @PageSize
    END;
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'DESC')))) = N'ASC' THEN N'ASC' ELSE N'DESC' END;
    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'FECHA_PARTE'))));
    DECLARE @Total INT = 0;
    DECLARE @TotalPages INT = 0;
    DECLARE @Offset INT = 0;
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX);
    DECLARE @TurnoJoin NVARCHAR(MAX) = N'';
    DECLARE @TurnoSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(50)) AS turno_codigo,
        CAST(NULL AS NVARCHAR(100)) AS turno_nombre';
    DECLARE @LegajoJoin NVARCHAR(MAX) = N'';
    DECLARE @OperarioSelect NVARCHAR(MAX) = N'
        CAST(N''Op.'' + CAST(p.ID_OPERARIO AS NVARCHAR(20)) AS NVARCHAR(200)) AS operario_nombre';
    DECLARE @EntradasSelect NVARCHAR(MAX) = N'CAST(0 AS INT) AS cantidad_entradas';
    DECLARE @EntradasJoin NVARCHAR(MAX) = N'';

    IF @SortCol NOT IN (
        N'FECHA_PARTE', N'ID_PARTE_OPERARIO', N'ESTADO', N'ID_OPERARIO', N'ID_TURNO',
        N'FECHA_ALTA', N'FECHA_MODIF'
    )
        SET @SortCol = N'FECHA_PARTE';

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_OPERARIO', N'U') IS NULL
    BEGIN
        SELECT
            CAST(1 AS INT) AS [page],
            CAST(50 AS INT) AS page_size,
            CAST(0 AS INT) AS total,
            CAST(0 AS INT) AS total_pages;

        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_operario,
            CAST(NULL AS NVARCHAR(200)) AS operario_nombre,
            CAST(NULL AS DATE) AS fecha_parte,
            CAST(NULL AS INT) AS id_turno,
            CAST(NULL AS NVARCHAR(50)) AS turno_codigo,
            CAST(NULL AS NVARCHAR(100)) AS turno_nombre,
            CAST(NULL AS INT) AS estado,
            CAST(NULL AS DATETIME) AS fecha_envio,
            CAST(NULL AS INT) AS cantidad_entradas
        WHERE 1 = 0;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'ID_TURNO') IS NOT NULL
    BEGIN
        SET @TurnoJoin = N'
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = p.ID_TURNO';
        SET @TurnoSelect = N'
        LTRIM(RTRIM(CAST(t.CODIGO_TURNO AS NVARCHAR(50)))) AS turno_codigo,
        LTRIM(RTRIM(CAST(t.NOMBRE AS NVARCHAR(100)))) AS turno_nombre';
    END

    IF OBJECT_ID(N'dbo.PQ_SUELD_LEGAJOS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID') IS NOT NULL
    BEGIN
        SET @LegajoJoin = N'
        LEFT JOIN dbo.PQ_SUELD_LEGAJOS l ON l.ID = p.ID_OPERARIO';
        SET @OperarioSelect = N'
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(CAST(l.APELLIDO AS NVARCHAR(80)), N'''') + N'' '' + ISNULL(CAST(l.NOMBRE AS NVARCHAR(80)), N''''))), N'''') IS NULL
                THEN CAST(N''Op.'' + CAST(p.ID_OPERARIO AS NVARCHAR(20)) AS NVARCHAR(200))
            ELSE CAST(LTRIM(RTRIM(ISNULL(CAST(l.APELLIDO AS NVARCHAR(80)), N'''') + N'' '' + ISNULL(CAST(l.NOMBRE AS NVARCHAR(80)), N''''))) AS NVARCHAR(200))
        END AS operario_nombre';
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_PARTE_OPERARIO') IS NOT NULL
    BEGIN
        SET @EntradasJoin = N'
        LEFT JOIN (
            SELECT ID_PARTE_OPERARIO, COUNT(1) AS cantidad_entradas
            FROM dbo.PQ_PRD_PARTES_ENTRADAS
            GROUP BY ID_PARTE_OPERARIO
        ) e ON e.ID_PARTE_OPERARIO = p.ID_PARTE_OPERARIO';
        SET @EntradasSelect = N'CAST(ISNULL(e.cantidad_entradas, 0) AS INT) AS cantidad_entradas';
    END

    SELECT @Total = COUNT(1)
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    WHERE (@FilterFechaDesde IS NULL OR CAST(p.FECHA_PARTE AS DATE) >= @FilterFechaDesde)
      AND (@FilterFechaHasta IS NULL OR CAST(p.FECHA_PARTE AS DATE) <= @FilterFechaHasta)
      AND (@FilterIdTurno IS NULL OR p.ID_TURNO = @FilterIdTurno)
      AND (@FilterIdOperario IS NULL OR p.ID_OPERARIO = @FilterIdOperario)
      AND (
            (@FilterEstado IS NOT NULL AND p.ESTADO = @FilterEstado)
            OR (@FilterEstado IS NULL AND p.ESTADO IN (0, 1, 2))
          );

    SET @TotalPages = CASE WHEN @Total > 0 THEN CEILING(1.0 * @Total / @PageSizeNorm) ELSE 0 END;
    IF @TotalPages > 0 AND @PageNorm > @TotalPages
        SET @PageNorm = @TotalPages;
    SET @Offset = (@PageNorm - 1) * @PageSizeNorm;

    SELECT
        CAST(@PageNorm AS INT) AS [page],
        CAST(@PageSizeNorm AS INT) AS page_size,
        CAST(@Total AS INT) AS total,
        CAST(@TotalPages AS INT) AS total_pages;

    DECLARE @OrderCol NVARCHAR(200) =
        CASE @SortCol
            WHEN N'ID_PARTE_OPERARIO' THEN N'p.ID_PARTE_OPERARIO'
            WHEN N'ESTADO' THEN N'p.ESTADO'
            WHEN N'ID_OPERARIO' THEN N'p.ID_OPERARIO'
            WHEN N'ID_TURNO' THEN N'p.ID_TURNO'
            WHEN N'FECHA_ALTA' THEN N'p.FECHA_ALTA'
            WHEN N'FECHA_MODIF' THEN N'p.FECHA_MODIF'
            ELSE N'p.FECHA_PARTE'
        END;

    SET @sql = N'
    SELECT
        CAST(p.ID_PARTE_OPERARIO AS INT) AS id,
        CAST(p.ID_OPERARIO AS INT) AS id_operario,
        ' + @OperarioSelect + N',
        CAST(p.FECHA_PARTE AS DATE) AS fecha_parte,
        CAST(p.ID_TURNO AS INT) AS id_turno,
        ' + @TurnoSelect + N',
        CAST(p.ESTADO AS INT) AS estado,
        CASE WHEN CAST(p.ESTADO AS INT) = 1 THEN CAST(p.FECHA_MODIF AS DATETIME) ELSE CAST(NULL AS DATETIME) END AS fecha_envio,
        ' + @EntradasSelect + N'
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    ' + @TurnoJoin + N'
    ' + @LegajoJoin + N'
    ' + @EntradasJoin + N'
    WHERE (@FilterFechaDesde IS NULL OR CAST(p.FECHA_PARTE AS DATE) >= @FilterFechaDesde)
      AND (@FilterFechaHasta IS NULL OR CAST(p.FECHA_PARTE AS DATE) <= @FilterFechaHasta)
      AND (@FilterIdTurno IS NULL OR p.ID_TURNO = @FilterIdTurno)
      AND (@FilterIdOperario IS NULL OR p.ID_OPERARIO = @FilterIdOperario)
      AND (
            (@FilterEstado IS NOT NULL AND p.ESTADO = @FilterEstado)
            OR (@FilterEstado IS NULL AND p.ESTADO IN (0, 1, 2))
          )
    ORDER BY ' + @OrderCol + N' ' + @DirNorm + N'
    OFFSET @Offset ROWS FETCH NEXT @PageSizeNorm ROWS ONLY;';

    SET @params = N'@FilterFechaDesde DATE, @FilterFechaHasta DATE, @FilterIdTurno INT,
        @FilterIdOperario INT, @FilterEstado INT, @Offset INT, @PageSizeNorm INT';

    EXEC sp_executesql
        @sql,
        @params,
        @FilterFechaDesde = @FilterFechaDesde,
        @FilterFechaHasta = @FilterFechaHasta,
        @FilterIdTurno = @FilterIdTurno,
        @FilterIdOperario = @FilterIdOperario,
        @FilterEstado = @FilterEstado,
        @Offset = @Offset,
        @PageSizeNorm = @PageSizeNorm;
END
GO
