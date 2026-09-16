CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_OperacionesUpdate
    @IdOperacion INT,
    @Nombre NVARCHAR(100) = NULL,
    @Activa BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_OPERACIONES', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
        RETURN;
    END

    IF @IdOperacion IS NULL OR @IdOperacion <= 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
        RETURN;
    END

    IF @Nombre IS NULL AND @Activa IS NULL
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
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
                CAST(NULL AS BIT) AS activa;
            RETURN;
        END
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_OPERACIONES
        WHERE ID_OPERACION = @IdOperacion
    )
    BEGIN
        SELECT
            CAST(N'NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
        RETURN;
    END

    UPDATE dbo.PQ_PRD_OPERACIONES
    SET
        NOMBRE = CASE WHEN @nombreNorm IS NOT NULL THEN @nombreNorm ELSE NOMBRE END,
        ACTIVA = CASE WHEN @Activa IS NOT NULL THEN @Activa ELSE ACTIVA END
    WHERE ID_OPERACION = @IdOperacion;

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        CAST(ID_OPERACION AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_OPERACION AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(100)))) AS nombre,
        CAST(ISNULL(ACTIVA, 0) AS BIT) AS activa
    FROM dbo.PQ_PRD_OPERACIONES
    WHERE ID_OPERACION = @IdOperacion;
END
GO
