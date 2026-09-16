CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_OperacionesCreate
    @Codigo NVARCHAR(20),
    @Nombre NVARCHAR(100),
    @Activa BIT = 1
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

    DECLARE @codigoNorm NVARCHAR(20) = LTRIM(RTRIM(@Codigo));
    DECLARE @nombreNorm NVARCHAR(100) = LTRIM(RTRIM(@Nombre));
    DECLARE @activaBit BIT = ISNULL(@Activa, 1);

    IF @codigoNorm IS NULL OR @codigoNorm = N'' OR LEN(@codigoNorm) > 20
       OR @nombreNorm IS NULL OR @nombreNorm = N'' OR LEN(@nombreNorm) > 100
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
        RETURN;
    END

    IF EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_OPERACIONES
        WHERE CODIGO_OPERACION = @codigoNorm
    )
    BEGIN
        SELECT
            CAST(N'DUPLICATE_CODE' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa;
        RETURN;
    END

    INSERT INTO dbo.PQ_PRD_OPERACIONES (CODIGO_OPERACION, NOMBRE, ACTIVA)
    VALUES (@codigoNorm, @nombreNorm, @activaBit);

    DECLARE @newId INT = CAST(SCOPE_IDENTITY() AS INT);

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        @newId AS id,
        @codigoNorm AS codigo,
        @nombreNorm AS nombre,
        @activaBit AS activa;
END
GO
