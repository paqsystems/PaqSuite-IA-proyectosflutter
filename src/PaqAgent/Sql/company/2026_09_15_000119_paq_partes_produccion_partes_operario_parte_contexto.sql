CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_PartesOperarioParteContexto
    @FechaParte DATE,
    @IdTurno INT = NULL,
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdOperario INT = NULL;
    DECLARE @IdParteOperario INT = NULL;

    IF @FechaParte IS NULL OR @UsuarioId IS NULL OR @UsuarioId <= 0
    BEGIN
        SELECT CAST(NULL AS INT) AS id_parte_operario;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_OPERARIO', N'U') IS NULL
        OR OBJECT_ID(N'dbo.PQ_SUELD_LEGAJOS', N'U') IS NULL
        OR COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID_USUARIO') IS NULL
        OR COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID') IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS id_parte_operario;
        RETURN;
    END

    SELECT TOP 1 @IdOperario = CAST(ID AS INT)
    FROM dbo.PQ_SUELD_LEGAJOS
    WHERE ID_USUARIO = @UsuarioId;

    IF @IdOperario IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS id_parte_operario;
        RETURN;
    END

    SELECT TOP 1 @IdParteOperario = CAST(p.ID_PARTE_OPERARIO AS INT)
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    WHERE p.ID_OPERARIO = @IdOperario
      AND CAST(p.FECHA_PARTE AS DATE) = @FechaParte
      AND (
            (@IdTurno IS NULL AND (p.ID_TURNO IS NULL OR p.ID_TURNO = 0))
            OR (@IdTurno IS NOT NULL AND p.ID_TURNO = @IdTurno)
          )
    ORDER BY p.ID_PARTE_OPERARIO DESC;

    SELECT @IdParteOperario AS id_parte_operario;
END
GO
