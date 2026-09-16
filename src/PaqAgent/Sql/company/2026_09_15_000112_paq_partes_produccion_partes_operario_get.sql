CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_PartesOperarioGet
    @IdParteOperario INT,
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NoLegajo INT = 0;
    DECLARE @NotFound INT = 0;
    DECLARE @IdOperario INT = NULL;
    DECLARE @TurnoJoin NVARCHAR(MAX) = N'';
    DECLARE @TurnoSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(100)) AS turno_nombre';
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX);

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_OPERARIO', N'U') IS NULL
    BEGIN
        SET @NotFound = 1;
        SELECT @NoLegajo AS no_legajo, @NotFound AS not_found;
        SELECT CAST(NULL AS INT) AS id WHERE 1 = 0;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_SUELD_LEGAJOS', N'U') IS NULL
       OR COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID_USUARIO') IS NULL
       OR COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID') IS NULL
    BEGIN
        SET @NoLegajo = 1;
        SELECT @NoLegajo AS no_legajo, @NotFound AS not_found;
        SELECT CAST(NULL AS INT) AS id WHERE 1 = 0;
        RETURN;
    END

    SELECT TOP 1 @IdOperario = CAST(ID AS INT)
    FROM dbo.PQ_SUELD_LEGAJOS
    WHERE ID_USUARIO = @UsuarioId;

    IF @IdOperario IS NULL
    BEGIN
        SET @NoLegajo = 1;
        SELECT @NoLegajo AS no_legajo, @NotFound AS not_found;
        SELECT CAST(NULL AS INT) AS id WHERE 1 = 0;
        RETURN;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_PARTES_OPERARIO
        WHERE ID_PARTE_OPERARIO = @IdParteOperario
          AND ID_OPERARIO = @IdOperario
    )
    BEGIN
        SET @NotFound = 1;
        SELECT @NoLegajo AS no_legajo, @NotFound AS not_found;
        SELECT CAST(NULL AS INT) AS id WHERE 1 = 0;
        RETURN;
    END

    SELECT 0 AS no_legajo, 0 AS not_found;

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'ID_TURNO') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'NOMBRE') IS NOT NULL
    BEGIN
        SET @TurnoJoin = N'
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = p.ID_TURNO';
        SET @TurnoSelect = N'LTRIM(RTRIM(CAST(t.NOMBRE AS NVARCHAR(100)))) AS turno_nombre';
    END

    SET @sql = N'
    SELECT TOP 1
        CAST(p.ID_PARTE_OPERARIO AS INT) AS id,
        CAST(p.FECHA_PARTE AS DATE) AS fecha_parte,
        CAST(p.ID_TURNO AS INT) AS id_turno,
        ' + @TurnoSelect + N',
        CAST(p.ESTADO AS INT) AS estado,
        CAST(p.OBSERVACIONES AS NVARCHAR(2000)) AS observaciones
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    ' + @TurnoJoin + N'
    WHERE p.ID_PARTE_OPERARIO = @IdParteOperario
      AND p.ID_OPERARIO = @IdOperario;';

    SET @params = N'@IdParteOperario INT, @IdOperario INT';

    EXEC sp_executesql
        @sql,
        @params,
        @IdParteOperario = @IdParteOperario,
        @IdOperario = @IdOperario;
END
GO
