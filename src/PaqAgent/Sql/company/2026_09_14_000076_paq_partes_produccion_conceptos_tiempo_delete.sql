CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_ConceptosTiempoDelete
    @IdConceptoTiempo INT
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF @IdConceptoTiempo IS NULL OR @IdConceptoTiempo <= 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
        WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo
    )
    BEGIN
        SELECT
            CAST(N'NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_CONCEPTO_TIEMPO') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_PARTES_ENTRADAS
            WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo
       )
    BEGIN
        SELECT
            CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
            @IdConceptoTiempo AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'ID_CONCEPTO_TIEMPO') IS NOT NULL
       AND EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
            WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo
       )
    BEGIN
        SELECT
            CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
            @IdConceptoTiempo AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    BEGIN TRY
        DELETE FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
        WHERE ID_CONCEPTO_TIEMPO = @IdConceptoTiempo;

        SELECT
            CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
            @IdConceptoTiempo AS id,
            CAST(1 AS BIT) AS eliminado;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
        BEGIN
            SELECT
                CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode,
                @IdConceptoTiempo AS id,
                CAST(0 AS BIT) AS eliminado;
            RETURN;
        END

        SELECT
            CAST(N'SQL_ERROR' AS NVARCHAR(50)) AS resultCode,
            @IdConceptoTiempo AS id,
            CAST(0 AS BIT) AS eliminado;
    END CATCH
END
GO
