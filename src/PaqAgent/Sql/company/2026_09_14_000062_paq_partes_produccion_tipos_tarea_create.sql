CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TiposTareaCreate
    @Codigo NVARCHAR(20),
    @Nombre NVARCHAR(100),
    @Activo BIT = 1
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

    DECLARE @codigoNorm NVARCHAR(20) = LTRIM(RTRIM(@Codigo));
    DECLARE @nombreNorm NVARCHAR(100) = LTRIM(RTRIM(@Nombre));
    DECLARE @activoBit BIT = ISNULL(@Activo, 1);

    IF @codigoNorm IS NULL OR @codigoNorm = N'' OR LEN(@codigoNorm) > 20
       OR @nombreNorm IS NULL OR @nombreNorm = N'' OR LEN(@nombreNorm) > 100
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    IF EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_TIPOS_TAREA
        WHERE CODIGO_TIPO_TAREA = @codigoNorm
    )
    BEGIN
        SELECT
            CAST(N'DUPLICATE_CODE' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    INSERT INTO dbo.PQ_PRD_TIPOS_TAREA (CODIGO_TIPO_TAREA, NOMBRE, ACTIVO)
    VALUES (@codigoNorm, @nombreNorm, @activoBit);

    DECLARE @newId INT = CAST(SCOPE_IDENTITY() AS INT);

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        @newId AS id,
        @codigoNorm AS codigo,
        @nombreNorm AS nombre,
        @activoBit AS activo;
END
GO
