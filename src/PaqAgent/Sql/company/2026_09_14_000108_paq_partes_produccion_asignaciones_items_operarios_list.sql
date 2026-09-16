CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_AsignacionesItemsOperariosList
    @IdAsignacion INT,
    @IdItem INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @IdAsignacion IS NULL OR @IdAsignacion <= 0 OR @IdItem IS NULL OR @IdItem <= 0
    BEGIN
        RAISERROR(N'id_asignacion e id_item deben ser > 0', 16, 1);
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS', N'U') IS NULL
    BEGIN
        SELECT CAST(1 AS INT) AS not_found;
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_operario,
            CAST(NULL AS NVARCHAR(50)) AS nro_legajo,
            CAST(NULL AS NVARCHAR(200)) AS nombre_completo,
            CAST(NULL AS NVARCHAR(50)) AS rol_plan
        WHERE 1 = 0;
        RETURN;
    END

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS
        WHERE ID_ASIGNACION = @IdAsignacion AND ID_ASIGNACION_ITEM = @IdItem
    )
    BEGIN
        SELECT CAST(1 AS INT) AS not_found;
        SELECT
            CAST(NULL AS INT) AS id
        WHERE 1 = 0;
        RETURN;
    END

    SELECT CAST(0 AS INT) AS not_found;

    IF OBJECT_ID(N'dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_operario,
            CAST(NULL AS NVARCHAR(50)) AS nro_legajo,
            CAST(NULL AS NVARCHAR(200)) AS nombre_completo,
            CAST(NULL AS NVARCHAR(50)) AS rol_plan
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @sql NVARCHAR(MAX) = N'
SELECT
    CAST(o.ID_ASIGITEM_OPERARIO AS INT) AS id,
    CAST(o.ID_OPERARIO AS INT) AS id_operario,
    CAST(NULL AS NVARCHAR(50)) AS nro_legajo,
    CAST(NULL AS NVARCHAR(200)) AS nombre_completo,
    CAST(o.ROL_PLAN AS NVARCHAR(50)) AS rol_plan
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS o
WHERE o.ID_ASIGNACION_ITEM = @IdItem';

    IF OBJECT_ID(N'dbo.PQ_SUELD_LEGAJOS', N'U') IS NOT NULL
    BEGIN
        SET @sql = N'
SELECT
    CAST(o.ID_ASIGITEM_OPERARIO AS INT) AS id,
    CAST(o.ID_OPERARIO AS INT) AS id_operario,
    CAST(l.NRO_LEGAJO AS NVARCHAR(50)) AS nro_legajo,
    LTRIM(RTRIM(
        CASE
            WHEN l.APELLIDO IS NULL AND l.NOMBRE IS NULL THEN CAST(o.ID_OPERARIO AS NVARCHAR(50))
            ELSE ISNULL(l.APELLIDO, N'''') + N'', '' + ISNULL(l.NOMBRE, N'''')
        END
    )) AS nombre_completo,
    CAST(o.ROL_PLAN AS NVARCHAR(50)) AS rol_plan
FROM dbo.PQ_PRD_ASIGNACIONES_ITEMS_OPERARIOS o
LEFT JOIN dbo.PQ_SUELD_LEGAJOS l ON l.ID = o.ID_OPERARIO
WHERE o.ID_ASIGNACION_ITEM = @IdItem';
    END

    EXEC sp_executesql @sql, N'@IdItem INT', @IdItem = @IdItem;
END
