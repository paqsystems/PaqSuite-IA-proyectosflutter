CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TurnosDelete
    @IdTurno INT
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF @IdTurno IS NULL OR @IdTurno <= 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
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
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_ASIGNACIONES', N'ID_TURNO') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_ASIGNACIONES
            WHERE ID_TURNO = @IdTurno
       )
    BEGIN
        SELECT
            CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
            @IdTurno AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_PARTES', N'ID_TURNO') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_PARTES
            WHERE ID_TURNO = @IdTurno
       )
    BEGIN
        SELECT
            CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
            @IdTurno AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_OPERARIO', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_PARTES_OPERARIO', N'ID_TURNO') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_PARTES_OPERARIO
            WHERE ID_TURNO = @IdTurno
       )
    BEGIN
        SELECT
            CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
            @IdTurno AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_CABECERA', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_PARTES_CABECERA', N'ID_TURNO') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_PARTES_CABECERA
            WHERE ID_TURNO = @IdTurno
       )
    BEGIN
        SELECT
            CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
            @IdTurno AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    BEGIN TRY
        DELETE FROM dbo.PQ_PRD_TURNOS
        WHERE ID_TURNO = @IdTurno;

        SELECT
            CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
            @IdTurno AS id,
            CAST(1 AS BIT) AS eliminado;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
        BEGIN
            SELECT
                CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
                @IdTurno AS id,
                CAST(0 AS BIT) AS eliminado;
            RETURN;
        END

        SELECT
            CAST(N'SQL_ERROR' AS NVARCHAR(50)) AS resultCode,
            @IdTurno AS id,
            CAST(0 AS BIT) AS eliminado;
    END CATCH
END
GO
