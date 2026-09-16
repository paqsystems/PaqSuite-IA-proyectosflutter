CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_PartesOperarioMisPartesList
    @UsuarioId INT,
    @Page INT = 1,
    @PageSize INT = 50,
    @PlanificadoPage INT = 1,
    @PlanificadoPageSize INT = 50,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL,
    @FilterFechaDesde DATE = NULL,
    @FilterFechaHasta DATE = NULL,
    @FilterIdTurno INT = NULL,
    @FilterEstado INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PageNorm INT = CASE WHEN ISNULL(@Page, 1) < 1 THEN 1 ELSE @Page END;
    DECLARE @PageSizeNorm INT = CASE
        WHEN ISNULL(@PageSize, 50) < 1 THEN 50
        WHEN @PageSize > 200 THEN 200
        ELSE @PageSize
    END;
    DECLARE @PlanPageNorm INT = CASE WHEN ISNULL(@PlanificadoPage, 1) < 1 THEN 1 ELSE @PlanificadoPage END;
    DECLARE @PlanPageSizeNorm INT = CASE
        WHEN ISNULL(@PlanificadoPageSize, 50) < 1 THEN 50
        WHEN @PlanificadoPageSize > 200 THEN 200
        ELSE @PlanificadoPageSize
    END;
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'DESC')))) = N'ASC' THEN N'ASC' ELSE N'DESC' END;
    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'FECHA_PARTE'))));
    DECLARE @IdOperario INT = NULL;
    DECLARE @TablaLegajos BIT = 0;
    DECLARE @TablaPartes BIT = 0;
    DECLARE @TablasPlanificado BIT = 0;
    DECLARE @TieneLegajo BIT = 0;
    DECLARE @LegajoNro NVARCHAR(50) = NULL;
    DECLARE @LegajoApellido NVARCHAR(80) = NULL;
    DECLARE @LegajoNombre NVARCHAR(80) = NULL;
    DECLARE @Total INT = 0;
    DECLARE @TotalPages INT = 0;
    DECLARE @Offset INT = 0;
    DECLARE @PlanTotal INT = 0;
    DECLARE @PlanTotalPages INT = 0;
    DECLARE @PlanOffset INT = 0;
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX);

    IF @SortCol NOT IN (
        N'FECHA_PARTE', N'ID_PARTE_OPERARIO', N'ESTADO', N'ID_TURNO',
        N'FECHA_ALTA', N'FECHA_APERTURA', N'FECHA_CIERRE'
    )
        SET @SortCol = N'FECHA_PARTE';

    IF OBJECT_ID(N'dbo.PQ_SUELD_LEGAJOS', N'U') IS NOT NULL
        AND COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID_USUARIO') IS NOT NULL
        AND COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID') IS NOT NULL
        SET @TablaLegajos = 1;

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_OPERARIO', N'U') IS NOT NULL
        SET @TablaPartes = 1;

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES', N'U') IS NOT NULL
        AND OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'U') IS NOT NULL
        AND OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS', N'U') IS NOT NULL
        SET @TablasPlanificado = 1;

    IF @TablaLegajos = 1 AND @UsuarioId IS NOT NULL AND @UsuarioId > 0
    BEGIN
        SELECT TOP 1
            @IdOperario = CAST(ID AS INT),
            @LegajoNro = CAST(NRO_LEGAJO AS NVARCHAR(50)),
            @LegajoApellido = CAST(APELLIDO AS NVARCHAR(80)),
            @LegajoNombre = CAST(NOMBRE AS NVARCHAR(80))
        FROM dbo.PQ_SUELD_LEGAJOS
        WHERE ID_USUARIO = @UsuarioId;

        IF @IdOperario IS NOT NULL
            SET @TieneLegajo = 1;
    END

    CREATE TABLE #Planificado
    (
        id_asignacion_item INT NULL,
        id_asignacion INT NULL,
        id_parte_operario INT NULL,
        id_parte_entrada INT NULL,
        id_orden_trabajo INT NULL,
        id_operacion INT NULL,
        id_tipo_tarea INT NULL,
        id_maquina INT NULL,
        fecha_asignacion DATE NULL,
        id_turno INT NULL,
        turno_codigo NVARCHAR(50) NULL,
        turno_nombre NVARCHAR(100) NULL,
        codigo_ot NVARCHAR(50) NULL,
        nro_orden INT NULL,
        operacion_etiqueta NVARCHAR(220) NULL,
        tipo_tarea_etiqueta NVARCHAR(220) NULL,
        supervisor_id INT NULL,
        supervisor_codigo NVARCHAR(50) NULL,
        supervisor_nombre NVARCHAR(100) NULL,
        supervisor_label NVARCHAR(160) NULL,
        maquina_etiqueta NVARCHAR(220) NULL,
        minutos_plan INT NULL,
        unidades_plan DECIMAL(18, 4) NULL,
        unidades_hora_std DECIMAL(18, 4) NULL,
        notas_plan NVARCHAR(2000) NULL,
        registrado_en_parte BIT NOT NULL DEFAULT (0),
        es_generada_usuario BIT NOT NULL DEFAULT (0),
        row_key NVARCHAR(40) NOT NULL
    );

    IF @TablaPartes = 0 OR @TieneLegajo = 0
    BEGIN
        SELECT
            CAST(1 AS INT) AS [page],
            CAST(0 AS INT) AS page_size,
            CAST(0 AS INT) AS total,
            CAST(0 AS INT) AS total_pages,
            CAST(1 AS INT) AS planificado_page,
            CAST(0 AS INT) AS planificado_page_size,
            CAST(0 AS INT) AS planificado_total,
            CAST(0 AS INT) AS planificado_total_pages,
            CAST(CASE WHEN @UsuarioId IS NOT NULL AND @UsuarioId > 0 THEN 1 ELSE 0 END AS BIT) AS autenticado,
            CAST(CASE WHEN @UsuarioId IS NOT NULL AND @UsuarioId > 0 THEN @UsuarioId ELSE NULL END AS INT) AS id_usuario,
            CAST(@TablaLegajos AS BIT) AS tabla_legajos_existe,
            CAST(@TieneLegajo AS BIT) AS tiene_legajo_vinculado,
            CAST(@IdOperario AS INT) AS id_legajo_pk_usado_como_id_operario,
            @LegajoNro AS legajo_nro,
            @LegajoApellido AS legajo_apellido,
            @LegajoNombre AS legajo_nombre,
            CAST(@TablaPartes AS BIT) AS tabla_partes_operario_existe,
            CAST(@TablasPlanificado AS BIT) AS tablas_planificado_existen,
            CAST(@FilterFechaDesde AS DATE) AS filter_fecha_desde,
            CAST(@FilterFechaHasta AS DATE) AS filter_fecha_hasta,
            CAST(@FilterIdTurno AS INT) AS filter_id_turno,
            CAST(@FilterEstado AS INT) AS filter_estado;

        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS DATE) AS fecha_parte,
            CAST(NULL AS INT) AS id_turno,
            CAST(NULL AS NVARCHAR(50)) AS turno_codigo,
            CAST(NULL AS NVARCHAR(100)) AS turno_nombre,
            CAST(NULL AS INT) AS estado,
            CAST(NULL AS DATETIME) AS fecha_apertura,
            CAST(NULL AS DATETIME) AS fecha_cierre,
            CAST(NULL AS NVARCHAR(2000)) AS observaciones
        WHERE 1 = 0;

        SELECT * FROM #Planificado WHERE 1 = 0;
        RETURN;
    END

    SELECT @Total = COUNT(1)
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    WHERE p.ID_OPERARIO = @IdOperario
      AND (@FilterFechaDesde IS NULL OR CAST(p.FECHA_PARTE AS DATE) >= @FilterFechaDesde)
      AND (@FilterFechaHasta IS NULL OR CAST(p.FECHA_PARTE AS DATE) <= @FilterFechaHasta)
      AND (@FilterIdTurno IS NULL OR p.ID_TURNO = @FilterIdTurno)
      AND (@FilterEstado IS NULL OR p.ESTADO = @FilterEstado);

    SET @TotalPages = CASE WHEN @Total > 0 THEN CEILING(1.0 * @Total / @PageSizeNorm) ELSE 0 END;
    IF @TotalPages > 0 AND @PageNorm > @TotalPages
        SET @PageNorm = @TotalPages;
    SET @Offset = (@PageNorm - 1) * @PageSizeNorm;

    IF @TablasPlanificado = 1
    BEGIN
        INSERT INTO #Planificado
        (
            id_asignacion_item, id_asignacion, id_orden_trabajo, id_operacion, id_tipo_tarea, id_maquina,
            fecha_asignacion, id_turno, turno_codigo, turno_nombre, codigo_ot, nro_orden,
            operacion_etiqueta, tipo_tarea_etiqueta, supervisor_id, maquina_etiqueta,
            minutos_plan, unidades_plan, unidades_hora_std, notas_plan,
            registrado_en_parte, es_generada_usuario, row_key
        )
        SELECT
            CAST(i.ID_ASIGNACION_ITEM AS INT),
            CAST(i.ID_ASIGNACION AS INT),
            CAST(i.ID_ORDEN_TRABAJO AS INT),
            CAST(i.ID_OPERACION AS INT),
            CAST(i.ID_TIPO_TAREA AS INT),
            CAST(i.ID_MAQUINA AS INT),
            CAST(a.FECHA_ASIGNACION AS DATE),
            CAST(a.ID_TURNO AS INT),
            CASE WHEN OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
                THEN LTRIM(RTRIM(CAST(t.CODIGO_TURNO AS NVARCHAR(50)))) ELSE NULL END,
            CASE WHEN OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
                THEN LTRIM(RTRIM(CAST(t.NOMBRE AS NVARCHAR(100)))) ELSE NULL END,
            CASE WHEN OBJECT_ID(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'U') IS NOT NULL
                THEN LTRIM(RTRIM(CAST(ot.CODIGO_OT AS NVARCHAR(50)))) ELSE NULL END,
            CAST(ot.NRO_ORDEN AS INT),
            CASE
                WHEN op.CODIGO_OPERACION IS NULL AND op.NOMBRE IS NULL THEN NULL
                WHEN NULLIF(LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) + N' — ' + LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
            END,
            CASE
                WHEN tt.CODIGO_TIPO_TAREA IS NULL AND tt.NOMBRE IS NULL THEN NULL
                WHEN NULLIF(LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))) + N' — ' + LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
            END,
            CAST(a.ID_USUARIO_SUPERVISOR AS INT),
            CASE
                WHEN mq.CODIGO_MAQUINA IS NULL AND mq.NOMBRE IS NULL THEN NULL
                WHEN NULLIF(LTRIM(RTRIM(CAST(mq.CODIGO_MAQUINA AS NVARCHAR(50)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(mq.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(mq.NOMBRE AS NVARCHAR(100)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(mq.CODIGO_MAQUINA AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(mq.CODIGO_MAQUINA AS NVARCHAR(50)))) + N' — ' + LTRIM(RTRIM(CAST(mq.NOMBRE AS NVARCHAR(100))))
            END,
            CAST(i.MINUTOS_PLAN AS INT),
            CAST(i.UNIDADES_PLAN AS DECIMAL(18, 4)),
            CAST(i.UNIDADES_HORA_STD AS DECIMAL(18, 4)),
            CAST(i.NOTAS_PLAN AS NVARCHAR(2000)),
            CAST(CASE
                WHEN OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NOT NULL
                     AND EXISTS (
                        SELECT 1
                        FROM dbo.PQ_PRD_PARTES_ENTRADAS e
                        INNER JOIN dbo.PQ_PRD_PARTES_OPERARIO po ON po.ID_PARTE_OPERARIO = e.ID_PARTE_OPERARIO
                        WHERE e.ID_ASIGNACION_ITEM = i.ID_ASIGNACION_ITEM
                          AND po.ID_OPERARIO = @IdOperario
                     ) THEN 1 ELSE 0
            END AS BIT),
            CAST(0 AS BIT),
            CAST(N'plan-' + CAST(i.ID_ASIGNACION_ITEM AS NVARCHAR(20)) AS NVARCHAR(40))
        FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS i
        INNER JOIN dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS io
            ON io.ID_ASIGNACION_ITEM = i.ID_ASIGNACION_ITEM
           AND io.ID_OPERARIO = @IdOperario
        INNER JOIN dbo.PQ_PRD_ASIGNACIONES a
            ON a.ID_ASIGNACION = i.ID_ASIGNACION
           AND a.ESTADO = 1
           AND (@FilterFechaDesde IS NULL OR CAST(a.FECHA_ASIGNACION AS DATE) >= @FilterFechaDesde)
           AND (@FilterFechaHasta IS NULL OR CAST(a.FECHA_ASIGNACION AS DATE) <= @FilterFechaHasta)
           AND (@FilterIdTurno IS NULL OR a.ID_TURNO = @FilterIdTurno)
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = a.ID_TURNO
        LEFT JOIN dbo.PQ_PRD_ORDENES_TRABAJO ot ON ot.ID_ORDEN_TRABAJO = i.ID_ORDEN_TRABAJO
        LEFT JOIN dbo.PQ_PRD_OPERACIONES op ON op.ID_OPERACION = i.ID_OPERACION
        LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA tt ON tt.ID_TIPO_TAREA = i.ID_TIPO_TAREA
        LEFT JOIN dbo.PQ_PRD_MAQUINAS mq ON mq.ID_MAQUINA = i.ID_MAQUINA
        WHERE ISNULL(CAST(i.ACTIVO AS INT), 1) = 1;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_ASIGNACION_ITEM') IS NOT NULL
    BEGIN
        INSERT INTO #Planificado
        (
            id_parte_operario, id_parte_entrada, id_orden_trabajo, id_operacion, id_tipo_tarea, id_maquina,
            fecha_asignacion, id_turno, turno_codigo, turno_nombre, codigo_ot, nro_orden,
            operacion_etiqueta, tipo_tarea_etiqueta, maquina_etiqueta,
            minutos_plan, unidades_plan, notas_plan,
            registrado_en_parte, es_generada_usuario, row_key
        )
        SELECT
            CAST(e.ID_PARTE_OPERARIO AS INT),
            CAST(e.ID_PARTE_ENTRADA AS INT),
            CAST(e.ID_ORDEN_TRABAJO AS INT),
            CAST(ISNULL(e.ID_OPERACION, ot.ID_OPERACION) AS INT),
            CAST(e.ID_TIPO_TAREA AS INT),
            CAST(e.ID_MAQUINA AS INT),
            CAST(p.FECHA_PARTE AS DATE),
            CAST(p.ID_TURNO AS INT),
            LTRIM(RTRIM(CAST(t.CODIGO_TURNO AS NVARCHAR(50)))),
            LTRIM(RTRIM(CAST(t.NOMBRE AS NVARCHAR(100)))),
            LTRIM(RTRIM(CAST(ot.CODIGO_OT AS NVARCHAR(50)))),
            CAST(ot.NRO_ORDEN AS INT),
            CASE
                WHEN op.CODIGO_OPERACION IS NULL AND op.NOMBRE IS NULL THEN NULL
                WHEN NULLIF(LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(op.CODIGO_OPERACION AS NVARCHAR(50)))) + N' — ' + LTRIM(RTRIM(CAST(op.NOMBRE AS NVARCHAR(100))))
            END,
            CASE
                WHEN tt.CODIGO_TIPO_TAREA IS NULL AND tt.NOMBRE IS NULL THEN NULL
                WHEN NULLIF(LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(50)))) + N' — ' + LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
            END,
            CASE
                WHEN mq.CODIGO_MAQUINA IS NULL AND mq.NOMBRE IS NULL THEN NULL
                WHEN NULLIF(LTRIM(RTRIM(CAST(mq.CODIGO_MAQUINA AS NVARCHAR(50)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(mq.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(mq.NOMBRE AS NVARCHAR(100)))), N'') IS NULL
                    THEN LTRIM(RTRIM(CAST(mq.CODIGO_MAQUINA AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(mq.CODIGO_MAQUINA AS NVARCHAR(50)))) + N' — ' + LTRIM(RTRIM(CAST(mq.NOMBRE AS NVARCHAR(100))))
            END,
            CAST(e.MINUTOS AS INT),
            CAST(e.UNIDADES_HECHAS AS DECIMAL(18, 4)),
            CAST(e.NOTAS AS NVARCHAR(2000)),
            CAST(1 AS BIT),
            CAST(1 AS BIT),
            CAST(N'libre-' + CAST(e.ID_PARTE_ENTRADA AS NVARCHAR(20)) AS NVARCHAR(40))
        FROM dbo.PQ_PRD_PARTES_ENTRADAS e
        INNER JOIN dbo.PQ_PRD_PARTES_OPERARIO p ON p.ID_PARTE_OPERARIO = e.ID_PARTE_OPERARIO
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = p.ID_TURNO
        LEFT JOIN dbo.PQ_PRD_ORDENES_TRABAJO ot ON ot.ID_ORDEN_TRABAJO = e.ID_ORDEN_TRABAJO
        LEFT JOIN dbo.PQ_PRD_OPERACIONES op ON op.ID_OPERACION = ISNULL(e.ID_OPERACION, ot.ID_OPERACION)
        LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA tt ON tt.ID_TIPO_TAREA = e.ID_TIPO_TAREA
        LEFT JOIN dbo.PQ_PRD_MAQUINAS mq ON mq.ID_MAQUINA = e.ID_MAQUINA
        WHERE p.ID_OPERARIO = @IdOperario
          AND e.ID_ASIGNACION_ITEM IS NULL
          AND (
                e.FECHA_HORA_DESDE IS NOT NULL
                OR e.FECHA_HORA_HASTA IS NOT NULL
                OR e.NOTAS IS NOT NULL
                OR e.UNIDADES_HECHAS IS NOT NULL
                OR e.UNIDADES_MERMA IS NOT NULL
                OR e.UNIDADES_RETRABAJO IS NOT NULL
              )
          AND (@FilterFechaDesde IS NULL OR CAST(p.FECHA_PARTE AS DATE) >= @FilterFechaDesde)
          AND (@FilterFechaHasta IS NULL OR CAST(p.FECHA_PARTE AS DATE) <= @FilterFechaHasta)
          AND (@FilterIdTurno IS NULL OR p.ID_TURNO = @FilterIdTurno)
          AND (@FilterEstado IS NULL OR p.ESTADO = @FilterEstado);
    END

    SELECT @PlanTotal = COUNT(1) FROM #Planificado;
    SET @PlanTotalPages = CASE WHEN @PlanTotal > 0 THEN CEILING(1.0 * @PlanTotal / @PlanPageSizeNorm) ELSE 0 END;
    IF @PlanTotalPages > 0 AND @PlanPageNorm > @PlanTotalPages
        SET @PlanPageNorm = @PlanTotalPages;
    SET @PlanOffset = (@PlanPageNorm - 1) * @PlanPageSizeNorm;

    SELECT
        CAST(@PageNorm AS INT) AS [page],
        CAST(@PageSizeNorm AS INT) AS page_size,
        CAST(@Total AS INT) AS total,
        CAST(@TotalPages AS INT) AS total_pages,
        CAST(@PlanPageNorm AS INT) AS planificado_page,
        CAST(@PlanPageSizeNorm AS INT) AS planificado_page_size,
        CAST(@PlanTotal AS INT) AS planificado_total,
        CAST(@PlanTotalPages AS INT) AS planificado_total_pages,
        CAST(1 AS BIT) AS autenticado,
        CAST(@UsuarioId AS INT) AS id_usuario,
        CAST(@TablaLegajos AS BIT) AS tabla_legajos_existe,
        CAST(@TieneLegajo AS BIT) AS tiene_legajo_vinculado,
        CAST(@IdOperario AS INT) AS id_legajo_pk_usado_como_id_operario,
        @LegajoNro AS legajo_nro,
        @LegajoApellido AS legajo_apellido,
        @LegajoNombre AS legajo_nombre,
        CAST(@TablaPartes AS BIT) AS tabla_partes_operario_existe,
        CAST(@TablasPlanificado AS BIT) AS tablas_planificado_existen,
        CAST(@FilterFechaDesde AS DATE) AS filter_fecha_desde,
        CAST(@FilterFechaHasta AS DATE) AS filter_fecha_hasta,
        CAST(@FilterIdTurno AS INT) AS filter_id_turno,
        CAST(@FilterEstado AS INT) AS filter_estado;

    DECLARE @OrderCol NVARCHAR(200) =
        CASE @SortCol
            WHEN N'ID_PARTE_OPERARIO' THEN N'p.ID_PARTE_OPERARIO'
            WHEN N'ESTADO' THEN N'p.ESTADO'
            WHEN N'ID_TURNO' THEN N'p.ID_TURNO'
            WHEN N'FECHA_ALTA' THEN N'p.FECHA_ALTA'
            WHEN N'FECHA_APERTURA' THEN N'p.FECHA_APERTURA'
            WHEN N'FECHA_CIERRE' THEN N'p.FECHA_CIERRE'
            ELSE N'p.FECHA_PARTE'
        END;

    DECLARE @TurnoJoin NVARCHAR(MAX) = N'';
    DECLARE @TurnoSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(50)) AS turno_codigo,
        CAST(NULL AS NVARCHAR(100)) AS turno_nombre';

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
    BEGIN
        SET @TurnoJoin = N'
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = p.ID_TURNO';
        SET @TurnoSelect = N'
        LTRIM(RTRIM(CAST(t.CODIGO_TURNO AS NVARCHAR(50)))) AS turno_codigo,
        LTRIM(RTRIM(CAST(t.NOMBRE AS NVARCHAR(100)))) AS turno_nombre';
    END

    SET @sql = N'
    SELECT
        CAST(p.ID_PARTE_OPERARIO AS INT) AS id,
        CAST(p.FECHA_PARTE AS DATE) AS fecha_parte,
        CAST(p.ID_TURNO AS INT) AS id_turno,
        ' + @TurnoSelect + N',
        CAST(p.ESTADO AS INT) AS estado,
        CAST(p.FECHA_APERTURA AS DATETIME) AS fecha_apertura,
        CAST(p.FECHA_CIERRE AS DATETIME) AS fecha_cierre,
        CAST(p.OBSERVACIONES AS NVARCHAR(2000)) AS observaciones
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    ' + @TurnoJoin + N'
    WHERE p.ID_OPERARIO = @IdOperario
      AND (@FilterFechaDesde IS NULL OR CAST(p.FECHA_PARTE AS DATE) >= @FilterFechaDesde)
      AND (@FilterFechaHasta IS NULL OR CAST(p.FECHA_PARTE AS DATE) <= @FilterFechaHasta)
      AND (@FilterIdTurno IS NULL OR p.ID_TURNO = @FilterIdTurno)
      AND (@FilterEstado IS NULL OR p.ESTADO = @FilterEstado)
    ORDER BY ' + @OrderCol + N' ' + @DirNorm + N'
    OFFSET @Offset ROWS FETCH NEXT @PageSizeNorm ROWS ONLY;';

    SET @params = N'@IdOperario INT, @FilterFechaDesde DATE, @FilterFechaHasta DATE,
        @FilterIdTurno INT, @FilterEstado INT, @Offset INT, @PageSizeNorm INT';

    EXEC sp_executesql
        @sql,
        @params,
        @IdOperario = @IdOperario,
        @FilterFechaDesde = @FilterFechaDesde,
        @FilterFechaHasta = @FilterFechaHasta,
        @FilterIdTurno = @FilterIdTurno,
        @FilterEstado = @FilterEstado,
        @Offset = @Offset,
        @PageSizeNorm = @PageSizeNorm;

    SELECT
        id_asignacion_item,
        id_asignacion,
        id_parte_operario,
        id_parte_entrada,
        id_orden_trabajo,
        id_operacion,
        id_tipo_tarea,
        id_maquina,
        fecha_asignacion,
        id_turno,
        turno_codigo,
        turno_nombre,
        codigo_ot,
        nro_orden,
        operacion_etiqueta,
        tipo_tarea_etiqueta,
        supervisor_id,
        supervisor_codigo,
        supervisor_nombre,
        supervisor_label,
        maquina_etiqueta,
        minutos_plan,
        unidades_plan,
        unidades_hora_std,
        notas_plan,
        CAST(registrado_en_parte AS INT) AS registrado_en_parte,
        CAST(es_generada_usuario AS INT) AS es_generada_usuario,
        row_key
    FROM #Planificado
    ORDER BY fecha_asignacion DESC, row_key ASC
    OFFSET @PlanOffset ROWS FETCH NEXT @PlanPageSizeNorm ROWS ONLY;
END
GO
