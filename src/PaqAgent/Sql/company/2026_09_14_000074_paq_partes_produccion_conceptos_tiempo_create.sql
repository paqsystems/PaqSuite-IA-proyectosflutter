CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_ConceptosTiempoCreate
    @Codigo NVARCHAR(20),
    @Nombre NVARCHAR(120),
    @EsProductivo BIT = 1,
    @Activo BIT = 1,
    @Habitual BIT = 0,
    @IdTipoTarea INT
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

    DECLARE @codigoNorm NVARCHAR(20) = LTRIM(RTRIM(@Codigo));
    DECLARE @nombreNorm NVARCHAR(120) = LTRIM(RTRIM(@Nombre));
    DECLARE @esProductivoBit BIT = ISNULL(@EsProductivo, 1);
    DECLARE @activoBit BIT = ISNULL(@Activo, 1);
    DECLARE @habitualBit BIT = ISNULL(@Habitual, 0);
    DECLARE @idTipo INT = @IdTipoTarea;

    IF @codigoNorm IS NULL OR @codigoNorm = N'' OR LEN(@codigoNorm) > 20
       OR @nombreNorm IS NULL OR @nombreNorm = N'' OR LEN(@nombreNorm) > 120
       OR @idTipo IS NULL OR @idTipo <= 0
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

    IF @habitualBit = 1 AND (@idTipo IS NULL OR @idTipo <= 0)
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

    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NULL
       OR NOT EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_TIPOS_TAREA
            WHERE ID_TIPO_TAREA = @idTipo
              AND ISNULL(ACTIVO, 0) = 1
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

    IF EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
        WHERE CODIGO_CONCEPTO = @codigoNorm
    )
    BEGIN
        SELECT
            CAST(N'DUPLICATE_CODE' AS NVARCHAR(50)) AS resultCode,
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
    DECLARE @newId INT;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @habitualBit = 1
        BEGIN
            SELECT TOP 1
                @avisoId = CAST(ID_CONCEPTO_TIEMPO AS INT),
                @avisoCodigo = LTRIM(RTRIM(CAST(CODIGO_CONCEPTO AS NVARCHAR(20)))),
                @avisoNombre = LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(120))))
            FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
            WHERE ID_TIPO_TAREA = @idTipo
              AND ISNULL(HABITUAL, 0) = 1;

            IF @avisoId IS NOT NULL
            BEGIN
                UPDATE dbo.PQ_PRD_CONCEPTOS_TIEMPO
                SET HABITUAL = 0
                WHERE ID_CONCEPTO_TIEMPO = @avisoId;

                SET @avisoReemplazado = 1;
            END
        END

        INSERT INTO dbo.PQ_PRD_CONCEPTOS_TIEMPO (
            CODIGO_CONCEPTO,
            NOMBRE,
            ES_PRODUCTIVO,
            ACTIVO,
            HABITUAL,
            ID_TIPO_TAREA
        )
        VALUES (
            @codigoNorm,
            @nombreNorm,
            @esProductivoBit,
            @activoBit,
            @habitualBit,
            @idTipo
        );

        SET @newId = CAST(SCOPE_IDENTITY() AS INT);

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
    WHERE c.ID_CONCEPTO_TIEMPO = @newId;
END
GO
