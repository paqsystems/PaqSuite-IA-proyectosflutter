CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_AsignacionesGet
    @IdAsignacion INT
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES', N'U') IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS id WHERE 1 = 0;
        RETURN;
    END

    DECLARE @TurnoJoin NVARCHAR(MAX) = N'';
    DECLARE @TurnoSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(50)) AS turno_codigo';
    DECLARE @TipoJoin NVARCHAR(MAX) = N'';
    DECLARE @TipoSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo,
        CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre';
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX);

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

    SET @sql = N'
    SELECT TOP 1
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
        CAST(a.FECHA_CIERRE AS DATETIME) AS fecha_cierre,
        CAST(a.OBSERVACIONES AS NVARCHAR(2000)) AS observaciones
    FROM dbo.PQ_PRD_ASIGNACIONES a
    ' + @TurnoJoin + N'
    ' + @TipoJoin + N'
    WHERE a.ID_ASIGNACION = @IdAsignacion;';

    SET @params = N'@IdAsignacion INT';

    EXEC sp_executesql
        @sql,
        @params,
        @IdAsignacion = @IdAsignacion;
END
GO
