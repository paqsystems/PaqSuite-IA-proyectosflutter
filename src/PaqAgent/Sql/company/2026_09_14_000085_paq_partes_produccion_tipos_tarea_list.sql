CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TiposTareaList
    @FilterActivo BIT = NULL,
    @FilterCodigo NVARCHAR(20) = NULL,
    @FilterNombre NVARCHAR(100) = NULL,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_TIPOS_TAREA', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(100)) AS nombre,
            CAST(NULL AS BIT) AS activo
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'CODIGO_TIPO_TAREA'))));
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'ASC')))) = N'DESC' THEN N'DESC' ELSE N'ASC' END;

    IF @SortCol NOT IN (N'CODIGO_TIPO_TAREA', N'NOMBRE', N'ACTIVO', N'ID_TIPO_TAREA')
        SET @SortCol = N'CODIGO_TIPO_TAREA';

    DECLARE @sql NVARCHAR(MAX) = N'
    SELECT
        CAST(ID_TIPO_TAREA AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_TIPO_TAREA AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(100)))) AS nombre,
        CAST(ISNULL(ACTIVO, 0) AS BIT) AS activo
    FROM dbo.PQ_PRD_TIPOS_TAREA
    WHERE (@FilterActivo IS NULL OR ACTIVO = @FilterActivo)
      AND (@FilterCodigo IS NULL OR CODIGO_TIPO_TAREA LIKE N''%'' + @FilterCodigo + N''%'')
      AND (@FilterNombre IS NULL OR NOMBRE LIKE N''%'' + @FilterNombre + N''%'')
    ORDER BY ' + QUOTENAME(@SortCol) + N' ' + @DirNorm + N';';

    EXEC sp_executesql
        @sql,
        N'@FilterActivo BIT, @FilterCodigo NVARCHAR(20), @FilterNombre NVARCHAR(100)',
        @FilterActivo = @FilterActivo,
        @FilterCodigo = @FilterCodigo,
        @FilterNombre = @FilterNombre;
END
GO
