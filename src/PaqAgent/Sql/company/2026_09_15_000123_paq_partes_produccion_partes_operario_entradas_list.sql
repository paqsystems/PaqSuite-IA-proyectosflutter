CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_PartesOperarioEntradasList
    @IdParteOperario INT,
    @UsuarioId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NoLegajo INT = 0;
    DECLARE @NotFound INT = 0;
    DECLARE @IdOperario INT = NULL;
    DECLARE @OtJoin NVARCHAR(MAX) = N'';
    DECLARE @OtSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(50)) AS codigo_ot';
    DECLARE @ConceptoJoin NVARCHAR(MAX) = N'';
    DECLARE @ConceptoSelect NVARCHAR(MAX) = N'
        CAST(NULL AS NVARCHAR(100)) AS concepto_nombre,
        CAST(NULL AS BIT) AS es_productivo';
    DECLARE @MaquinaJoin NVARCHAR(MAX) = N'';
    DECLARE @MaquinaSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(200)) AS maquina_etiqueta';
    DECLARE @NroSelect NVARCHAR(MAX) = N'';
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX) = N'@IdParteOperario INT';

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

    IF @IdParteOperario IS NULL OR @IdParteOperario <= 0
        OR NOT EXISTS (
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

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NULL
        OR COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_PARTE_OPERARIO') IS NULL
        OR COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_PARTE_ENTRADA') IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS id WHERE 1 = 0;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_ORDENES_TRABAJO', N'ID_ORDEN_TRABAJO') IS NOT NULL
    BEGIN
        SET @OtJoin = N'
        LEFT JOIN dbo.PQ_PRD_ORDENES_TRABAJO ot ON ot.ID_ORDEN_TRABAJO = e.ID_ORDEN_TRABAJO';
        SET @OtSelect = N'LTRIM(RTRIM(CAST(ot.CODIGO_OT AS NVARCHAR(50)))) AS codigo_ot';
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'ID_CONCEPTO_TIEMPO') IS NOT NULL
    BEGIN
        SET @ConceptoJoin = N'
        LEFT JOIN dbo.PQ_PRD_CONCEPTOS_TIEMPO c ON c.ID_CONCEPTO_TIEMPO = e.ID_CONCEPTO_TIEMPO';
        SET @ConceptoSelect = N'
        LTRIM(RTRIM(CAST(c.NOMBRE AS NVARCHAR(100)))) AS concepto_nombre,
        CAST(CASE WHEN c.ES_PRODUCTIVO IS NULL THEN NULL WHEN CAST(c.ES_PRODUCTIVO AS INT) = 0 THEN 0 ELSE 1 END AS BIT) AS es_productivo';
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_MAQUINAS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_MAQUINAS', N'ID_MAQUINA') IS NOT NULL
    BEGIN
        SET @MaquinaJoin = N'
        LEFT JOIN dbo.PQ_PRD_MAQUINAS m ON m.ID_MAQUINA = e.ID_MAQUINA';
        SET @MaquinaSelect = N'
        NULLIF(LTRIM(RTRIM(
            CASE
                WHEN NULLIF(LTRIM(RTRIM(CAST(m.CODIGO_MAQUINA AS NVARCHAR(50)))), N'''') IS NULL
                     AND NULLIF(LTRIM(RTRIM(CAST(m.NOMBRE AS NVARCHAR(100)))), N'''') IS NULL
                    THEN N''''
                WHEN NULLIF(LTRIM(RTRIM(CAST(m.CODIGO_MAQUINA AS NVARCHAR(50)))), N'''') IS NULL
                    THEN LTRIM(RTRIM(CAST(m.NOMBRE AS NVARCHAR(100))))
                WHEN NULLIF(LTRIM(RTRIM(CAST(m.NOMBRE AS NVARCHAR(100)))), N'''') IS NULL
                    THEN LTRIM(RTRIM(CAST(m.CODIGO_MAQUINA AS NVARCHAR(50))))
                ELSE LTRIM(RTRIM(CAST(m.CODIGO_MAQUINA AS NVARCHAR(50)))) + N'' — '' + LTRIM(RTRIM(CAST(m.NOMBRE AS NVARCHAR(100))))
            END
        )), N'''') AS maquina_etiqueta';
    END

    IF COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'NRO_ORDEN_OPERACION') IS NOT NULL
        SET @NroSelect = N', CAST(e.NRO_ORDEN_OPERACION AS INT) AS nro_orden_operacion';

    SET @sql = N'
    SELECT
        CAST(e.ID_PARTE_ENTRADA AS INT) AS id,
        CAST(e.ID_ASIGNACION_ITEM AS INT) AS id_asignacion_item,
        CAST(e.ID_ORDEN_TRABAJO AS INT) AS id_orden_trabajo,
        ' + @OtSelect + N',
        CAST(e.ORIGEN_CARGA AS NVARCHAR(20)) AS origen_carga,
        CAST(e.ID_MAQUINA AS INT) AS id_maquina,
        ' + @MaquinaSelect + N',
        CAST(e.ID_CONCEPTO_TIEMPO AS INT) AS id_concepto_tiempo,
        ' + @ConceptoSelect + N',
        CAST(e.MINUTOS AS INT) AS minutos,
        CAST(e.FECHA_HORA_DESDE AS DATETIME) AS fecha_hora_desde,
        CAST(e.FECHA_HORA_HASTA AS DATETIME) AS fecha_hora_hasta,
        CAST(e.UNIDADES_HECHAS AS DECIMAL(18, 4)) AS unidades_hechas,
        CAST(e.UNIDADES_MERMA AS DECIMAL(18, 4)) AS unidades_merma,
        CAST(e.UNIDADES_RETRABAJO AS DECIMAL(18, 4)) AS unidades_retrabajo,
        CAST(e.NOTAS AS NVARCHAR(2000)) AS notas
        ' + @NroSelect + N'
    FROM dbo.PQ_PRD_PARTES_ENTRADAS e
    ' + @OtJoin + N'
    ' + @ConceptoJoin + N'
    ' + @MaquinaJoin + N'
    WHERE e.ID_PARTE_OPERARIO = @IdParteOperario
    ORDER BY e.ID_PARTE_ENTRADA;';

    EXEC sp_executesql
        @sql,
        @params,
        @IdParteOperario = @IdParteOperario;
END
GO
