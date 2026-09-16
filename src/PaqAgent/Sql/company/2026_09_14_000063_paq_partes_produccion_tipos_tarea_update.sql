CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TiposTareaUpdate
    @IdTipoTarea INT,
    @Nombre NVARCHAR(100) = NULL,
    @Activo BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    IF @IdTipoTarea IS NULL OR @IdTipoTarea <= 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    IF @Nombre IS NULL AND @Activo IS NULL
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    DECLARE @nombreNorm NVARCHAR(100) = NULL;
    IF @Nombre IS NOT NULL
    BEGIN
        SET @nombreNorm = LTRIM(RTRIM(@Nombre));
        IF @nombreNorm = N'' OR LEN(@nombreNorm) > 100
        BEGIN
            SELECT
                CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
                CAST(NULL AS INT) AS id,
                CAST(NULL AS NVARCHAR(20)) AS codigo,
                CAST(NULL AS NVARCHAR(100)) AS nombre,
                CAST(NULL AS BIT) AS activo;
            RETURN;
        END
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_TIPOS_TAREA
        WHERE ID_TIPO_TAREA = @IdTipoTarea
    )
    BEGIN
        SELECT
            CAST(N'NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    UPDATE dbo.PQ_PRD_TIPOS_TAREA
    SET
        NOMBRE = CASE WHEN @nombreNorm IS NOT NULL THEN @nombreNorm ELSE NOMBRE END,
        ACTIVO = CASE WHEN @Activo IS NOT NULL THEN @Activo ELSE ACTIVO END
    WHERE ID_TIPO_TAREA = @IdTipoTarea;

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        CAST(ID_TIPO_TAREA AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_TIPO_TAREA AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(100)))) AS nombre,
        CAST(ISNULL(ACTIVO, 0) AS BIT) AS activo
    FROM dbo.PQ_PRD_TIPOS_TAREA
    WHERE ID_TIPO_TAREA = @IdTipoTarea;
END
GO
