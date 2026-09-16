CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_AsignacionesList
    @Page INT = 1,
    @PageSize INT = 50,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL,
    @FilterFechaDesde DATE = NULL,
    @FilterFechaHasta DATE = NULL,
    @FilterIdTurno INT = NULL,
    @FilterIdTipoTarea INT = NULL,
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
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'ASC')))) = N'DESC' THEN N'DESC' ELSE N'ASC' END;
    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'FECHA_ASIGNACION'))));
    DECLARE @Total INT = 0;
    DECLARE @TotalPages INT = 0;
    DECLARE @Offset INT = 0;
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX);
    DECLARE @TurnoJoin NVARCHAR(MAX) = N'';
    DECLARE @TurnoSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(50)) AS turno_codigo';
    DECLARE @TipoJoin NVARCHAR(MAX) = N'';
    DECLARE @TipoSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo,
        CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre';

    IF @SortCol NOT IN (
        N'FECHA_ASIGNACION', N'ID_ASIGNACION', N'ESTADO', N'ID_TURNO', N'ID_TIPO_TAREA',
        N'FECHA_ALTA', N'FECHA_PUBLICACION', N'FECHA_CIERRE', N'ID_USUARIO_SUPERVISOR'
    )
        SET @SortCol = N'FECHA_ASIGNACION';

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES', N'U') IS NULL
    BEGIN
        SELECT
            CAST(1 AS INT) AS [page],
            CAST(@PageSizeNorm AS INT) AS page_size,
            CAST(0 AS INT) AS total,
            CAST(0 AS INT) AS total_pages;

        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS DATE) AS fecha_asignacion,
            CAST(NULL AS INT) AS id_turno,
            CAST(NULL AS NVARCHAR(50)) AS turno_codigo,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS INT) AS supervisor_id,
            CAST(NULL AS NVARCHAR(50)) AS supervisor_codigo,
            CAST(NULL AS NVARCHAR(100)) AS supervisor_nombre,
            CAST(NULL AS NVARCHAR(160)) AS supervisor_label,
            CAST(NULL AS INT) AS estado,
            CAST(NULL AS NVARCHAR(20)) AS estado_label,
            CAST(NULL AS DATETIME) AS fecha_publicacion,
            CAST(NULL AS DATETIME) AS fecha_cierre
        WHERE 1 = 0;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'ID_TURNO') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'CODIGO_TURNO') IS NOT NULL
    BEGIN
        SET @TurnoJoin = N'
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = a.ID_TURNO';
        SET @TurnoSelect = N'LTRIM(RTRIM(CAST(t.CODIGO_TURNO AS NVARCHAR(50)))) AS turno_codigo';
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TIPOS_TAREA', N'ID_TIPO_TAREA') IS NOT NULL
    BEGIN
        SET @TipoJoin = N'
        LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA tt ON tt.ID_TIPO_TAREA = a.ID_TIPO_TAREA';
        SET @TipoSelect = N'
        LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))) AS tipo_tarea_codigo,
        LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100)))) AS tipo_tarea_nombre';
    END

    SELECT @Total = COUNT(1)
    FROM dbo.PQ_PRD_ASIGNACIONES a
    WHERE (@FilterFechaDesde IS NULL OR a.FECHA_ASIGNACION >= @FilterFechaDesde)
      AND (@FilterFechaHasta IS NULL OR a.FECHA_ASIGNACION <= @FilterFechaHasta)
      AND (@FilterIdTurno IS NULL OR a.ID_TURNO = @FilterIdTurno)
      AND (@FilterIdTipoTarea IS NULL OR a.ID_TIPO_TAREA = @FilterIdTipoTarea)
      AND (@FilterEstado IS NULL OR a.ESTADO = @FilterEstado);

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
            WHEN N'ID_ASIGNACION' THEN N'a.ID_ASIGNACION'
            WHEN N'ESTADO' THEN N'a.ESTADO'
            WHEN N'ID_TURNO' THEN N'a.ID_TURNO'
            WHEN N'ID_TIPO_TAREA' THEN N'a.ID_TIPO_TAREA'
            WHEN N'FECHA_ALTA' THEN N'a.FECHA_ALTA'
            WHEN N'FECHA_PUBLICACION' THEN N'a.FECHA_PUBLICACION'
            WHEN N'FECHA_CIERRE' THEN N'a.FECHA_CIERRE'
            WHEN N'ID_USUARIO_SUPERVISOR' THEN N'a.ID_USUARIO_SUPERVISOR'
            ELSE N'a.FECHA_ASIGNACION'
        END;

    SET @sql = N'
    SELECT
        CAST(a.ID_ASIGNACION AS INT) AS id,
        CAST(a.FECHA_ASIGNACION AS DATE) AS fecha_asignacion,
        CAST(a.ID_TURNO AS INT) AS id_turno,
        ' + @TurnoSelect + N',
        CAST(a.ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
        ' + @TipoSelect + N',
        CAST(a.ID_USUARIO_SUPERVISOR AS INT) AS supervisor_id,
        CAST(NULL AS NVARCHAR(50)) AS supervisor_codigo,
        CAST(NULL AS NVARCHAR(100)) AS supervisor_nombre,
        CAST(NULL AS NVARCHAR(160)) AS supervisor_label,
        CAST(a.ESTADO AS INT) AS estado,
        CASE CAST(a.ESTADO AS INT)
            WHEN 0 THEN N''Borrador''
            WHEN 1 THEN N''Publicada''
            WHEN 2 THEN N''Cerrada''
            WHEN 3 THEN N''Anulada''
            ELSE N''Desconocido''
        END AS estado_label,
        CAST(a.FECHA_PUBLICACION AS DATETIME) AS fecha_publicacion,
        CAST(a.FECHA_CIERRE AS DATETIME) AS fecha_cierre
    FROM dbo.PQ_PRD_ASIGNACIONES a
    ' + @TurnoJoin + N'
    ' + @TipoJoin + N'
    WHERE (@FilterFechaDesde IS NULL OR a.FECHA_ASIGNACION >= @FilterFechaDesde)
      AND (@FilterFechaHasta IS NULL OR a.FECHA_ASIGNACION <= @FilterFechaHasta)
      AND (@FilterIdTurno IS NULL OR a.ID_TURNO = @FilterIdTurno)
      AND (@FilterIdTipoTarea IS NULL OR a.ID_TIPO_TAREA = @FilterIdTipoTarea)
      AND (@FilterEstado IS NULL OR a.ESTADO = @FilterEstado)
    ORDER BY ' + @OrderCol + N' ' + @DirNorm + N'
    OFFSET @Offset ROWS FETCH NEXT @PageSizeNorm ROWS ONLY;';

    SET @params = N'@FilterFechaDesde DATE, @FilterFechaHasta DATE, @FilterIdTurno INT,
        @FilterIdTipoTarea INT, @FilterEstado INT, @Offset INT, @PageSizeNorm INT';

    EXEC sp_executesql
        @sql,
        @params,
        @FilterFechaDesde = @FilterFechaDesde,
        @FilterFechaHasta = @FilterFechaHasta,
        @FilterIdTurno = @FilterIdTurno,
        @FilterIdTipoTarea = @FilterIdTipoTarea,
        @FilterEstado = @FilterEstado,
        @Offset = @Offset,
        @PageSizeNorm = @PageSizeNorm;
END
GO
