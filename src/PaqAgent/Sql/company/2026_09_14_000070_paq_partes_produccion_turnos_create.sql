CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TurnosCreate
    @Codigo NVARCHAR(20),
    @Nombre NVARCHAR(50),
    @HoraInicio TIME = NULL,
    @HoraFin TIME = NULL,
    @Activo BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS nombre,
            CAST(NULL AS VARCHAR(8)) AS hora_inicio,
            CAST(NULL AS VARCHAR(8)) AS hora_fin,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    DECLARE @codigoNorm NVARCHAR(20) = LTRIM(RTRIM(@Codigo));
    DECLARE @nombreNorm NVARCHAR(50) = LTRIM(RTRIM(@Nombre));
    DECLARE @activoBit BIT = ISNULL(@Activo, 1);

    IF @codigoNorm IS NULL OR @codigoNorm = N'' OR LEN(@codigoNorm) > 20
       OR @nombreNorm IS NULL OR @nombreNorm = N'' OR LEN(@nombreNorm) > 50
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS nombre,
            CAST(NULL AS VARCHAR(8)) AS hora_inicio,
            CAST(NULL AS VARCHAR(8)) AS hora_fin,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    IF @HoraInicio IS NOT NULL AND @HoraFin IS NOT NULL AND @HoraFin <= @HoraInicio
    BEGIN
        SELECT
            CAST(N'INVALID_SCHEDULE' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS nombre,
            CAST(NULL AS VARCHAR(8)) AS hora_inicio,
            CAST(NULL AS VARCHAR(8)) AS hora_fin,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    IF EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_TURNOS
        WHERE CODIGO_TURNO = @codigoNorm
    )
    BEGIN
        SELECT
            CAST(N'DUPLICATE_CODE' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS nombre,
            CAST(NULL AS VARCHAR(8)) AS hora_inicio,
            CAST(NULL AS VARCHAR(8)) AS hora_fin,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    INSERT INTO dbo.PQ_PRD_TURNOS (CODIGO_TURNO, NOMBRE, HORA_INICIO, HORA_FIN, ACTIVO)
    VALUES (@codigoNorm, @nombreNorm, @HoraInicio, @HoraFin, @activoBit);

    DECLARE @newId INT = CAST(SCOPE_IDENTITY() AS INT);

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        @newId AS id,
        @codigoNorm AS codigo,
        @nombreNorm AS nombre,
        CONVERT(VARCHAR(8), @HoraInicio, 108) AS hora_inicio,
        CONVERT(VARCHAR(8), @HoraFin, 108) AS hora_fin,
        @activoBit AS activo;
END
GO
