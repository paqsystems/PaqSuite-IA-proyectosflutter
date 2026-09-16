CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_MaquinasUpdate
    @IdMaquina INT,
    @Nombre NVARCHAR(100) = NULL,
    @Activa BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_MAQUINAS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
        RETURN;
    END

    IF @IdMaquina IS NULL OR @IdMaquina <= 0
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
        FROM dbo.PQ_PRD_MAQUINAS
        WHERE ID_MAQUINA = @IdMaquina
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

    UPDATE dbo.PQ_PRD_MAQUINAS
    SET
        NOMBRE = CASE WHEN @nombreNorm IS NOT NULL THEN @nombreNorm ELSE NOMBRE END,
        ACTIVA = CASE WHEN @Activa IS NOT NULL THEN @Activa ELSE ACTIVA END
    WHERE ID_MAQUINA = @IdMaquina;

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        CAST(ID_MAQUINA AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_MAQUINA AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(100)))) AS nombre,
        CAST(ISNULL(ACTIVA, 0) AS BIT) AS activa
    FROM dbo.PQ_PRD_MAQUINAS
    WHERE ID_MAQUINA = @IdMaquina;
END
GO
