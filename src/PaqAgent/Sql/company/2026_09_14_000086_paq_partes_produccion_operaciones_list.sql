CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_OperacionesList
    @FilterActiva BIT = NULL,
    @FilterCodigo NVARCHAR(20) = NULL,
    @FilterNombre NVARCHAR(100) = NULL,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_OPERACIONES', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activa
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'CODIGO_OPERACION'))));
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'ASC')))) = N'DESC' THEN N'DESC' ELSE N'ASC' END;

    IF @SortCol NOT IN (N'CODIGO_OPERACION', N'NOMBRE', N'ACTIVA', N'ID_OPERACION')
        SET @SortCol = N'CODIGO_OPERACION';

    DECLARE @sql NVARCHAR(MAX) = N'
    SELECT
        CAST(ID_OPERACION AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_OPERACION AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(100)))) AS nombre,
        CAST(ISNULL(ACTIVA, 0) AS BIT) AS activa
    FROM dbo.PQ_PRD_OPERACIONES
    WHERE (@FilterActiva IS NULL OR ACTIVA = @FilterActiva)
      AND (@FilterCodigo IS NULL OR CODIGO_OPERACION LIKE N''%'' + @FilterCodigo + N''%'')
      AND (@FilterNombre IS NULL OR NOMBRE LIKE N''%'' + @FilterNombre + N''%'')
    ORDER BY ' + QUOTENAME(@SortCol) + N' ' + @DirNorm + N';';

    EXEC sp_executesql
        @sql,
        N'@FilterActiva BIT, @FilterCodigo NVARCHAR(20), @FilterNombre NVARCHAR(100)',
        @FilterActiva = @FilterActiva,
        @FilterCodigo = @FilterCodigo,
        @FilterNombre = @FilterNombre;
END
GO
