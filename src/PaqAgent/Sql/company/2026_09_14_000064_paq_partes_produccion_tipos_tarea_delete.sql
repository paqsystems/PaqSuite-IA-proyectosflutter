CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TiposTareaDelete
    @IdTipoTarea INT
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NULL
    BEGIN
        SELECT
            CAST(N'tablaNoExiste' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF @IdTipoTarea IS NULL OR @IdTipoTarea <= 0
    BEGIN
        SELECT
            CAST(N'INVALID_PARAMETERS' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_TIPOS_TAREA
        WHERE ID_TIPO_TAREA = @IdTipoTarea
    )
    BEGIN
        SELECT
            CAST(N'NOT_FOUND' AS NVARCHAR(50)) AS resultCode,
            CAST(NULL AS INT) AS id,
            CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.PQ_PRD_ASIGNACIONES WHERE ID_TIPO_TAREA = @IdTipoTarea)
    BEGIN
        SELECT CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode, @IdTipoTarea AS id, CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS WHERE ID_TIPO_TAREA = @IdTipoTarea)
    BEGIN
        SELECT CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode, @IdTipoTarea AS id, CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO WHERE ID_TIPO_TAREA = @IdTipoTarea)
    BEGIN
        SELECT CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode, @IdTipoTarea AS id, CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM dbo.PQ_PRD_PARTES_ENTRADAS WHERE ID_TIPO_TAREA = @IdTipoTarea)
    BEGIN
        SELECT CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode, @IdTipoTarea AS id, CAST(0 AS BIT) AS eliminado;
        RETURN;
    END

    BEGIN TRY
        DELETE FROM dbo.PQ_PRD_TIPOS_TAREA
        WHERE ID_TIPO_TAREA = @IdTipoTarea;

        SELECT
            CAST(N'OK' AS NVARCHAR(50)) AS resultCode,
            @IdTipoTarea AS id,
            CAST(1 AS BIT) AS eliminado;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
        BEGIN
            SELECT CAST(N'REFERENCED' AS NVARCHAR(50)) AS resultCode, @IdTipoTarea AS id, CAST(0 AS BIT) AS eliminado;
            RETURN;
        END

        SELECT CAST(N'SQL_ERROR' AS NVARCHAR(50)) AS resultCode, @IdTipoTarea AS id, CAST(0 AS BIT) AS eliminado;
    END CATCH
END
GO
