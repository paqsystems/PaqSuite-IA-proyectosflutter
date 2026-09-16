CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_ConceptosTiempoList
    @FilterActivo BIT = NULL,
    @FilterEsProductivo BIT = NULL,
    @FilterIdTipoTarea INT = NULL,
    @FilterHabitual BIT = NULL,
    @FilterCodigo NVARCHAR(20) = NULL,
    @FilterNombre NVARCHAR(120) = NULL,
    @Sort NVARCHAR(50) = NULL,
    @Dir NVARCHAR(4) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(20)) AS codigo,
            CAST(NULL AS NVARCHAR(120)) AS nombre,
            CAST(NULL AS BIT) AS es_productivo,
            CAST(NULL AS BIT) AS activo
        WHERE 1 = 0;
        RETURN;
    END

    DECLARE @HasTipoTarea BIT = CASE
        WHEN COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'HABITUAL') IS NOT NULL
         AND COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'ID_TIPO_TAREA') IS NOT NULL
        THEN 1 ELSE 0 END;

    DECLARE @SortCol NVARCHAR(50) = UPPER(LTRIM(RTRIM(ISNULL(NULLIF(@Sort, N''), N'CODIGO_CONCEPTO'))));
    DECLARE @DirNorm NVARCHAR(4) = CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(@Dir, N'ASC')))) = N'DESC' THEN N'DESC' ELSE N'ASC' END;

    IF @HasTipoTarea = 1
    BEGIN
        IF @SortCol NOT IN (
            N'CODIGO_CONCEPTO', N'NOMBRE', N'ACTIVO', N'ES_PRODUCTIVO',
            N'ID_CONCEPTO_TIEMPO', N'HABITUAL', N'ID_TIPO_TAREA'
        )
            SET @SortCol = N'CODIGO_CONCEPTO';

        DECLARE @sqlModern NVARCHAR(MAX) = N'
        SELECT
            CAST(c.ID_CONCEPTO_TIEMPO AS INT) AS id,
            LTRIM(RTRIM(CAST(c.CODIGO_CONCEPTO AS NVARCHAR(20)))) AS codigo,
            LTRIM(RTRIM(CAST(c.NOMBRE AS NVARCHAR(120)))) AS nombre,
            CAST(ISNULL(c.ES_PRODUCTIVO, 0) AS BIT) AS es_productivo,
            CAST(ISNULL(c.ACTIVO, 0) AS BIT) AS activo,
            CAST(ISNULL(c.HABITUAL, 0) AS BIT) AS habitual,
            CAST(c.ID_TIPO_TAREA AS INT) AS id_tipo_tarea,
            CASE
                WHEN tt.ID_TIPO_TAREA IS NULL THEN CAST(NULL AS NVARCHAR(20))
                ELSE LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(20))))
            END AS tipo_tarea_codigo,
            CASE
                WHEN tt.ID_TIPO_TAREA IS NULL THEN CAST(NULL AS NVARCHAR(100))
                ELSE LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
            END AS tipo_tarea_nombre,
            CASE
                WHEN tt.ID_TIPO_TAREA IS NULL THEN CAST(NULL AS NVARCHAR(150))
                WHEN LTRIM(RTRIM(CAST(ISNULL(tt.CODIGO_TIPO_TAREA, N'''') AS NVARCHAR(20)))) = N''''
                     AND LTRIM(RTRIM(CAST(ISNULL(tt.NOMBRE, N'''') AS NVARCHAR(100)))) = N''''
                    THEN CAST(NULL AS NVARCHAR(150))
                WHEN LTRIM(RTRIM(CAST(ISNULL(tt.CODIGO_TIPO_TAREA, N'''') AS NVARCHAR(20)))) = N''''
                    THEN LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
                WHEN LTRIM(RTRIM(CAST(ISNULL(tt.NOMBRE, N'''') AS NVARCHAR(100)))) = N''''
                    THEN LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(20))))
                ELSE LTRIM(RTRIM(CAST(tt.CODIGO_TIPO_TAREA AS NVARCHAR(20))))
                     + N'' — ''
                     + LTRIM(RTRIM(CAST(tt.NOMBRE AS NVARCHAR(100))))
            END AS tipo_tarea_label
        FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO AS c
        LEFT JOIN dbo.PQ_PRD_TIPOS_TAREA AS tt
            ON tt.ID_TIPO_TAREA = c.ID_TIPO_TAREA
        WHERE (@FilterActivo IS NULL OR c.ACTIVO = @FilterActivo)
          AND (@FilterEsProductivo IS NULL OR c.ES_PRODUCTIVO = @FilterEsProductivo)
          AND (@FilterIdTipoTarea IS NULL OR @FilterIdTipoTarea <= 0 OR c.ID_TIPO_TAREA = @FilterIdTipoTarea)
          AND (@FilterHabitual IS NULL OR c.HABITUAL = @FilterHabitual)
          AND (@FilterCodigo IS NULL OR c.CODIGO_CONCEPTO LIKE N''%'' + @FilterCodigo + N''%'')
          AND (@FilterNombre IS NULL OR c.NOMBRE LIKE N''%'' + @FilterNombre + N''%'')
        ORDER BY c.' + QUOTENAME(@SortCol) + N' ' + @DirNorm + N';';

        EXEC sp_executesql
            @sqlModern,
            N'@FilterActivo BIT, @FilterEsProductivo BIT, @FilterIdTipoTarea INT, @FilterHabitual BIT, @FilterCodigo NVARCHAR(20), @FilterNombre NVARCHAR(120)',
            @FilterActivo = @FilterActivo,
            @FilterEsProductivo = @FilterEsProductivo,
            @FilterIdTipoTarea = @FilterIdTipoTarea,
            @FilterHabitual = @FilterHabitual,
            @FilterCodigo = @FilterCodigo,
            @FilterNombre = @FilterNombre;
        RETURN;
    END

    IF @SortCol NOT IN (N'CODIGO_CONCEPTO', N'NOMBRE', N'ACTIVO', N'ES_PRODUCTIVO', N'ID_CONCEPTO_TIEMPO')
        SET @SortCol = N'CODIGO_CONCEPTO';

    DECLARE @sqlLegacy NVARCHAR(MAX) = N'
    SELECT
        CAST(ID_CONCEPTO_TIEMPO AS INT) AS id,
        LTRIM(RTRIM(CAST(CODIGO_CONCEPTO AS NVARCHAR(20)))) AS codigo,
        LTRIM(RTRIM(CAST(NOMBRE AS NVARCHAR(120)))) AS nombre,
        CAST(ISNULL(ES_PRODUCTIVO, 0) AS BIT) AS es_productivo,
        CAST(ISNULL(ACTIVO, 0) AS BIT) AS activo
    FROM dbo.PQ_PRD_CONCEPTOS_TIEMPO
    WHERE (@FilterActivo IS NULL OR ACTIVO = @FilterActivo)
      AND (@FilterEsProductivo IS NULL OR ES_PRODUCTIVO = @FilterEsProductivo)
      AND (@FilterCodigo IS NULL OR CODIGO_CONCEPTO LIKE N''%'' + @FilterCodigo + N''%'')
      AND (@FilterNombre IS NULL OR NOMBRE LIKE N''%'' + @FilterNombre + N''%'')
    ORDER BY ' + QUOTENAME(@SortCol) + N' ' + @DirNorm + N';';

    EXEC sp_executesql
        @sqlLegacy,
        N'@FilterActivo BIT, @FilterEsProductivo BIT, @FilterCodigo NVARCHAR(20), @FilterNombre NVARCHAR(120)',
        @FilterActivo = @FilterActivo,
        @FilterEsProductivo = @FilterEsProductivo,
        @FilterCodigo = @FilterCodigo,
        @FilterNombre = @FilterNombre;
END
GO
