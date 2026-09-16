CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TurnosUpdate
    @IdTurno INT,
    @Nombre NVARCHAR(50) = NULL,
    @HoraInicio TIME = NULL,
    @HoraFin TIME = NULL,
    @Activo BIT = NULL,
    @SetHoraInicio BIT = 0,
    @SetHoraFin BIT = 0
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

    IF @IdTurno IS NULL OR @IdTurno <= 0
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

    IF @Nombre IS NULL AND @Activo IS NULL AND ISNULL(@SetHoraInicio, 0) = 0 AND ISNULL(@SetHoraFin, 0) = 0
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

    DECLARE @nombreNorm NVARCHAR(50) = NULL;
    IF @Nombre IS NOT NULL
    BEGIN
        SET @nombreNorm = LTRIM(RTRIM(@Nombre));
        IF @nombreNorm = N'' OR LEN(@nombreNorm) > 50
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
    END

    IF ISNULL(@SetHoraInicio, 0) = 1
       AND ISNULL(@SetHoraFin, 0) = 1
       AND @HoraInicio IS NOT NULL
       AND @HoraFin IS NOT NULL
       AND @HoraFin <= @HoraInicio
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

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_TURNOS
        WHERE ID_TURNO = @IdTurno
    )
    BEGIN
        SELECT
            CAST(N'NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS nombre,
            CAST(NULL AS VARCHAR(8)) AS hora_inicio,
            CAST(NULL AS VARCHAR(8)) AS hora_fin,
            CAST(NULL AS BIT) AS activo;
        RETURN;
    END

    UPDATE dbo.PQ_PRD_TURNOS
    SET
        NOMBRE = CASE WHEN @nombreNorm IS NOT NULL THEN @nombreNorm ELSE NOMBRE END,
        HORA_INICIO = CASE WHEN ISNULL(@SetHoraInicio, 0) = 1 THEN @HoraInicio ELSE HORA_INICIO END,
        HORA_FIN = CASE WHEN ISNULL(@SetHoraFin, 0) = 1 THEN @HoraFin ELSE HORA_FIN END,
        ACTIVO = CASE WHEN @Activo IS NOT NULL THEN @Activo ELSE ACTIVO END
    WHERE ID_TURNO = @IdTurno;

    SELECT
        CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
        CAST(ID_TURNO AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_TURNO AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(50)))) AS nombre,
        CONVERT(VARCHAR(8), HORA_INICIO, 108) AS hora_inicio,
        CONVERT(VARCHAR(8), HORA_FIN, 108) AS hora_fin,
        CAST(ISNULL(ACTIVO, 0) AS BIT) AS activo
    FROM dbo.PQ_PRD_TURNOS
    WHERE ID_TURNO = @IdTurno;
END
GO
