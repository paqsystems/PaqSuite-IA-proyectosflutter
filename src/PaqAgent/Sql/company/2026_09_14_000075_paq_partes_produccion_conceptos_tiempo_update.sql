CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_ConceptosTiempoUpdate
    @IdConceptoTiempo INT,
    @Nombre NVARCHAR(120) = NULL,
    @EsProductivo BIT = NULL,
    @Activo BIT = NULL,
    @Habitual BIT = NULL,
    @IdTipoTarea INT = NULL,
    @SetIdTipoTarea BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END

    IF COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'HABITUAL') IS NULL
       OR COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'ID_TIPO_TAREA') IS NULL
    BEGIN
        SELECT
            CAST(N'schemaIncompleto' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END

    IF @IdConceptoTiempo IS NULL OR @IdConceptoTiempo <= 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END

    IF @Nombre IS NULL
       AND @EsProductivo IS NULL
       AND @Activo IS NULL
       AND @Habitual IS NULL
       AND ISNULL(@SetIdTipoTarea, 0) = 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END

    DECLARE @nombreNorm NVARCHAR(120) = NULL;
    IF @Nombre IS NOT NULL
    BEGIN
        SET @nombreNorm = LTRIM(RTRIM(@Nombre));
        IF @nombreNorm = N'' OR LEN(@nombreNorm) > 120
        BEGIN
            SELECT
                CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
                CAST(NULL AS INT) AS id,
                CAST(NULL AS NVARCHAR(20)) AS codigo,
                CAST(NULL AS NVARCHAR(120)) AS nombre,
                CAST(NULL AS BIT) AS es_productivo,
                CAST(NULL AS BIT) AS activo,
                CAST(NULL AS BIT) AS habitual,
                CAST(NULL AS INT) AS id_tipo_tarea,
                CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
                CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
                CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
                CAST(NULL AS BIT) AS aviso_reemplazado,
                CAST(NULL AS INT) AS aviso_concepto_id,
                CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
                CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
            RETURN;
        END
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
        WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo
    )
    BEGIN
        SELECT
            CAST(N'NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END

    DECLARE @currentTipo INT;
    DECLARE @currentHabitual BIT;

    SELECT
        @currentTipo = CAST(ID_TIPO_TAREA AS INT),
        @currentHabitual = CAST(ISNULL(HABITUAL, 0) AS BIT)
    FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
    WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo;

    DECLARE @effectiveTipo INT = CASE
        WHEN ISNULL(@SetIdTipoTarea, 0) = 1 THEN @IdTipoTarea
        ELSE @currentTipo
    END;
    DECLARE @effectiveHabitual BIT = CASE
        WHEN @Habitual IS NOT NULL THEN @Habitual
        ELSE @currentHabitual
    END;

    IF ISNULL(@SetIdTipoTarea, 0) = 1
    BEGIN
        IF @IdTipoTarea IS NULL OR @IdTipoTarea <= 0
        BEGIN
            SELECT
                CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
                CAST(NULL AS INT) AS id,
                CAST(NULL AS NVARCHAR(20)) AS codigo,
                CAST(NULL AS NVARCHAR(120)) AS nombre,
                CAST(NULL AS BIT) AS es_productivo,
                CAST(NULL AS BIT) AS activo,
                CAST(NULL AS BIT) AS habitual,
                CAST(NULL AS INT) AS id_tipo_tarea,
                CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
                CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
                CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
                CAST(NULL AS BIT) AS aviso_reemplazado,
                CAST(NULL AS INT) AS aviso_concepto_id,
                CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
                CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
            RETURN;
        END

        IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NULL
           OR NOT EXISTS (
                SELECT 1
                FROM dbo.PQ_PRD_TIPOS_TAREA
                WHERE ID_TIPO_TAREA = @IdTipoTarea
                  AND (
                        ISNULL(ACTIVO, 0) = 1
                        OR @IdTipoTarea = ISNULL(@currentTipo, -1)
                  )
           )
        BEGIN
            SELECT
                CAST(N'TIPO_TAREA_NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
                CAST(NULL AS INT) AS id,
                CAST(NULL AS NVARCHAR(20)) AS codigo,
                CAST(NULL AS NVARCHAR(120)) AS nombre,
                CAST(NULL AS BIT) AS es_productivo,
                CAST(NULL AS BIT) AS activo,
                CAST(NULL AS BIT) AS habitual,
                CAST(NULL AS INT) AS id_tipo_tarea,
                CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
                CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
                CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
                CAST(NULL AS BIT) AS aviso_reemplazado,
                CAST(NULL AS INT) AS aviso_concepto_id,
                CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
                CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
            RETURN;
        END
    END

    IF @effectiveHabitual = 1 AND (@effectiveTipo IS NULL OR @effectiveTipo <= 0)
    BEGIN
        SELECT
            CAST(N'HABITUAL_SIN_TIPO' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END

    DECLARE @avisoReemplazado BIT = NULL;
    DECLARE @avisoId INT = NULL;
    DECLARE @avisoCodigo NVARCHAR(20) = NULL;
    DECLARE @avisoNombre NVARCHAR(120) = NULL;
    DECLARE @shouldClearHabitual BIT = 0;

    IF @effectiveHabitual = 1 AND @effectiveTipo IS NOT NULL AND @effectiveTipo > 0
       AND (
            @Habitual IS NOT NULL
            OR ISNULL(@SetIdTipoTarea, 0) = 1
       )
    BEGIN
        SET @shouldClearHabitual = 1;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @shouldClearHabitual = 1
        BEGIN
            SELECT TOP 1
                @avisoId = CAST(ID_CONCEPTO_TIEMPO AS INT),
                @avisoCodigo = LTRIM(RTRIM(CAST(CODIGO_CONCEPTO AS NVARCHAR(20)))),
                @avisoNombre = LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(120))))
            FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
            WHERE ID_TIPO_TAREA = @effectiveTipo
              AND ISNULL(HABITUAL, 0) = 1
              AND ID_CONCEPTO_TIEMPO <> @IdConceptoTiempo;

            IF @avisoId IS NOT NULL
            BEGIN
                UPDATE dbo.PQ_PRD_CONCEPTOS_TIEMPO
                SET HABITUAL = 0
                WHERE ID_CONCEPTO_TIEMPO = @avisoId;

                SET @avisoReemplazado = 1;
            END
        END

        UPDATE dbo.PQ_PRD_CONCEPTOS_TIEMPO
        SET
            NOMBRE = CASE WHEN @nombreNorm IS NOT NULL THEN @nombreNorm ELSE NOMBRE END,
            ES_PRODUCTIVO = CASE WHEN @EsProductivo IS NOT NULL THEN @EsProductivo ELSE ES_PRODUCTIVO END,
            ACTIVO = CASE WHEN @Activo IS NOT NULL THEN @Activo ELSE ACTIVO END,
            HABITUAL = CASE
                WHEN @Habitual IS NOT NULL OR ISNULL(@SetIdTipoTarea, 0) = 1 THEN @effectiveHabitual
                ELSE HABITUAL
            END,
            ID_TIPO_TAREA = CASE
                WHEN ISNULL(@SetIdTipoTarea, 0) = 1 THEN @IdTipoTarea
                ELSE ID_TIPO_TAREA
            END
        WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        SELECT
            CAST(N'SQL_ERROR' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS habitual,
            CAST(NULL AS INT) AS id_tipo_tarea,
            CAST(NULL AS NVARCHAR(20)) AS tipo_tarea_codigo,
            CAST(NULL AS NVARCHAR(100)) AS tipo_tarea_nombre,
            CAST(NULL AS NVARCHAR(150)) AS tipo_tarea_label,
            CAST(NULL AS BIT) AS aviso_reemplazado,
            CAST(NULL AS INT) AS aviso_concepto_id,
            CAST(NULL AS NVARCHAR(20)) AS aviso_concepto_codigo,
            CAST(NULL AS NVARCHAR(120)) AS aviso_concepto_nombre;
        RETURN;
    END CATCH

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        CAST(c.ID_CONCEPTO_TIEMPO AS INT) AS id,
        LTRIM(RTRIM(CAST(c.CODIGO_CONCEPTO AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(c.NOMBRE AS NVARCHAR(120)))) AS nombre,
        CAST(ISNULL(c.ES_PRODUCTIVO, 0) AS BIT) AS es_productivo,
        CAST(ISNULL(c.ACTIVO, 0) AS BIT) AS activo,
        CAST(ISNULL(c.HABITUAL, 0) AS BIT) AS habitual,
        CAST(c.ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
        CASE
            WHEN tt.ID_TIPO_TAREA IS NULL THEN CAST(NULL AS NVARCHAR(20))
            ELSE LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(20))))
        END AS tipo_tarea_codigo,
        CASE
            WHEN tt.ID_TIPO_TAREA IS NULL THEN CAST(NULL AS NVARCHAR(100))
            ELSE LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
        END AS tipo_tarea_nombre,
        CASE
            WHEN tt.ID_TIPO_TAREA IS NULL THEN CAST(NULL AS NVARCHAR(150))
            WHEN LTRIM(RTRIM(CAST(ISNULL(tt.CODIGO_TIPO_TAREA, N'') AS NVARCHAR(20)))) = N''
                 AND LTRIM(RTRIM(CAST(ISNULL(tt.NOMBRE, N'') AS NVARCHAR(100)))) = N''
                THEN CAST(NULL AS NVARCHAR(150))
            WHEN LTRIM(RTRIM(CAST(ISNULL(tt.CODIGO_TIPO_TAREA, N'') AS NVARCHAR(20)))) = N''
                THEN LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
            WHEN LTRIM(RTRIM(CAST(ISNULL(tt.NOMBRE, N'') AS NVARCHAR(100)))) = N''
                THEN LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(20))))
            ELSE LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(20))))
                 + N' — '
                 + LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
        END AS tipo_tarea_label,
        @avisoReemplazado AS aviso_reemplazado,
        @avisoId AS aviso_concepto_id,
        @avisoCodigo AS aviso_concepto_codigo,
        @avisoNombre AS aviso_concepto_nombre
    FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO AS c
    LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA AS tt
        ON tt.ID_TIPO_TAREA = c.ID_TIPO_TAREA
    WHERE c.ID_CONCEPTO_TIEMPO = @IdConceptoTiempo;
END
GO
