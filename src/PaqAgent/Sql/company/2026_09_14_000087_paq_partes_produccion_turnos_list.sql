CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_TurnosList
    @FilterActivo BIT = NULL,
    @FilterCodigo NVARCHAR(20) = NULL,
    @FilterNombre NVARCHAR(50) = NULL,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(50)) AS nombre,
            CAST(NULL AS VARCHAR(8)) AS hora_inicio,
            CAST(NULL AS VARCHAR(8)) AS hora_fin,
            CAST(NULL AS BIT) AS activo
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'CODIGO_TURNO'))));
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'ASC')))) = N'DESC' THEN N'DESC' ELSE N'ASC' END;

    IF @SortCol NOT IN (N'CODIGO_TURNO', N'NOMBRE', N'ACTIVO', N'ID_TURNO', N'HORA_INICIO', N'HORA_FIN')
        SET @SortCol = N'CODIGO_TURNO';

    DECLARE @sql NVARCHAR(MAX) = N'
    SELECT
        CAST(ID_TURNO AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_TURNO AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(50)))) AS nombre,
        CONVERT(VARCHAR(8), HORA_INICIO, 108) AS hora_inicio,
        CONVERT(VARCHAR(8), HORA_FIN, 108) AS hora_fin,
        CAST(ISNULL(ACTIVO, 0) AS BIT) AS activo
    FROM dbo.PQ_PRD_TURNOS
    WHERE (@FilterActivo IS NULL OR ACTIVO = @FilterActivo)
      AND (@FilterCodigo IS NULL OR CODIGO_TURNO LIKE N''%'' + @FilterCodigo + N''%'')
      AND (@FilterNombre IS NULL OR NOMBRE LIKE N''%'' + @FilterNombre + N''%'')
    ORDER BY ' + QUOTENAME(@SortCol) + N' ' + @DirNorm + N';';

    EXEC sp_executesql
        @sql,
        N'@FilterActivo BIT, @FilterCodigo NVARCHAR(20), @FilterNombre NVARCHAR(50)',
        @FilterActivo = @FilterActivo,
        @FilterCodigo = @FilterCodigo,
        @FilterNombre = @FilterNombre;
END
GO
