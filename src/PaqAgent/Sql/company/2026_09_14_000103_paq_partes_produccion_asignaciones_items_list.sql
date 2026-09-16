CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_AsignacionesItemsList
    @IdAsignacion INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @IdAsignacion IS NULL OR @IdAsignacion <= 0
    BEGIN
        RAISERROR(N'id_asignacion debe ser > 0', 16, 1);
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES', N'U') IS NULL
       OR OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_asignacion,
            CAST(NULL AS INT) AS id_orden_trabajo,
            CAST(NULL AS NVARCHAR(50)) AS orden_trabajo_codigo,
            CAST(NULL AS INT) AS id_articulo,
            CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
            CAST(NULL AS INT) AS id_operacion,
            CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
            CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS INT) AS id_maquina,
            CAST(NULL AS NVARCHAR(50)) AS maquina_codigo,
            CAST(NULL AS NVARCHAR(100)) AS maquina_nombre,
            CAST(NULL AS DECIMAL(18, 4)) AS unidades_hora_std,
            CAST(NULL AS DECIMAL(18, 4)) AS unidades_plan,
            CAST(NULL AS INT) AS minutos_plan,
            CAST(NULL AS NVARCHAR(2000)) AS notas_plan,
            CAST(NULL AS INT) AS prioridad,
            CAST(NULL AS NVARCHAR(5)) AS hora_inicio_plan,
            CAST(NULL AS NVARCHAR(5)) AS hora_fin_plan,
            CAST(NULL AS INT) AS nro_orden_operacion
        WHERE 1 = 0;
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.PQ_PRD_ASIGNACIONES WHERE ID_ASIGNACION = @IdAsignacion)
    BEGIN
        -- RS vacío: gateway interpreta como lista vacía; host valida NOT_FOUND por SQL local o Agent NOT_FOUND via runner.
        -- Aquí devolvemos marca not_found en meta RS0 + items vacíos RS1.
        SELECT CAST(1 AS INT) AS not_found;
        SELECT
            CAST(NULL AS INT) AS id
        WHERE 1 = 0;
        RETURN;
    END

    SELECT CAST(0 AS INT) AS not_found;

    DECLARE @sql NVARCHAR(MAX) = N'
SELECT
    CAST(i.ID_ASIGNACION_ITEM AS INT) AS id,
    CAST(i.ID_ASIGNACION AS INT) AS id_asignacion,
    CAST(i.ID_ORDEN_TRABAJO AS INT) AS id_orden_trabajo,
    CAST(NULL AS NVARCHAR(50)) AS orden_trabajo_codigo,
    CAST(i.ID_ARTICULO AS INT) AS id_articulo,
    CAST(NULL AS NVARCHAR(50)) AS articulo_codigo,
    CAST(i.ID_OPERACION AS INT) AS id_operacion,
    CAST(NULL AS NVARCHAR(50)) AS operacion_codigo,
    CAST(NULL AS NVARCHAR(100)) AS operacion_nombre,
    CAST(i.ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
    CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo,
    CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
    CAST(i.ID_MAQUINA AS INT) AS id_maquina,
    CAST(NULL AS NVARCHAR(50)) AS maquina_codigo,
    CAST(NULL AS NVARCHAR(100)) AS maquina_nombre,
    CAST(i.UNIDADES_HORA_STD AS DECIMAL(18, 4)) AS unidades_hora_std,
    CAST(i.UNIDADES_PLAN AS DECIMAL(18, 4)) AS unidades_plan,
    CAST(i.MINUTOS_PLAN AS INT) AS minutos_plan,
    CAST(i.NOTAS_PLAN AS NVARCHAR(2000)) AS notas_plan,
    CAST(i.PRIORIDAD AS INT) AS prioridad';

    IF COL_LENGTH(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'HORA_INICIO_PLAN') IS NOT NULL
        SET @sql += N',
    CAST(i.HORA_INICIO_PLAN AS NVARCHAR(5)) AS hora_inicio_plan,
    CAST(i.HORA_FIN_PLAN AS NVARCHAR(5)) AS hora_fin_plan';
    ELSE
        SET @sql += N',
    CAST(NULL AS NVARCHAR(5)) AS hora_inicio_plan,
    CAST(NULL AS NVARCHAR(5)) AS hora_fin_plan';

    IF COL_LENGTH(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'NRO_ORDEN_OPERACION') IS NOT NULL
        SET @sql += N',
    CAST(i.NRO_ORDEN_OPERACION AS INT) AS nro_orden_operacion';
    ELSE
        SET @sql += N',
    CAST(NULL AS INT) AS nro_orden_operacion';

    IF OBJECT_ID(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'U') IS NOT NULL
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(50)) AS orden_trabajo_codigo',
            N'LTRIM(RTRIM(CAST(ot.CODIGO_OT AS NVARCHAR(50)))) AS orden_trabajo_codigo');

    IF OBJECT_ID(N'dbo.PQ_PRD_OPERACIONES', N'U') IS NOT NULL
    BEGIN
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(50)) AS operacion_codigo',
            N'LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) AS operacion_codigo');
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(100)) AS operacion_nombre',
            N'LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100)))) AS operacion_nombre');
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NOT NULL
    BEGIN
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(50)) AS tipo_tarea_codigo',
            N'LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))) AS tipo_tarea_codigo');
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre',
            N'LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100)))) AS tipo_tarea_nombre');
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_MAQUINAS', N'U') IS NOT NULL
    BEGIN
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(50)) AS maquina_codigo',
            N'LTRIM(RTRIM(CAST(m.CODIGO_MAQUINA AS NVARCHAR(50)))) AS maquina_codigo');
        SET @sql = REPLACE(@sql,
            N'CAST(NULL AS NVARCHAR(100)) AS maquina_nombre',
            N'LTRIM(RTRIM(CAST(m.NOMBRE AS NVARCHAR(100)))) AS maquina_nombre');
    END

    SET @sql += N'
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS i';

    IF OBJECT_ID(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'U') IS NOT NULL
        SET @sql += N'
LEFT JOIN dbo.PQ_PRD_ORDENES_TRABAJO ot ON ot.ID_ORDEN_TRABAJO = i.ID_ORDEN_TRABAJO';
    IF OBJECT_ID(N'dbo.PQ_PRD_OPERACIONES', N'U') IS NOT NULL
        SET @sql += N'
LEFT JOIN dbo.PQ_PRD_OPERACIONES op ON op.ID_OPERACION = i.ID_OPERACION';
    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NOT NULL
        SET @sql += N'
LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA tt ON tt.ID_TIPO_TAREA = i.ID_TIPO_TAREA';
    IF OBJECT_ID(N'dbo.PQ_PRD_MAQUINAS', N'U') IS NOT NULL
        SET @sql += N'
LEFT JOIN dbo.PQ_PRD_MAQUINAS m ON m.ID_MAQUINA = i.ID_MAQUINA';

    SET @sql += N'
WHERE i.ID_ASIGNACION = @IdAsignacion
ORDER BY i.PRIORIDAD, i.ID_ASIGNACION_ITEM;';

    EXEC sp_executesql @sql, N'@IdAsignacion INT', @IdAsignacion = @IdAsignacion;
END
