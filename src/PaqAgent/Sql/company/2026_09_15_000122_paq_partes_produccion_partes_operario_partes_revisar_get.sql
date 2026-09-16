CREATE OR ALTER PROCEDURE dbo.PAQ_PartesProduccion_PartesOperarioPartesRevisarGet
    @IdParteOperario INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TurnoJoin NVARCHAR(MAX) = N'';
    DECLARE @TurnoSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(100)) AS turno_nombre';
    DECLARE @LegajoJoin NVARCHAR(MAX) = N'';
    DECLARE @OperarioSelect NVARCHAR(MAX) = N'
        CAST(N''Op.'' + CAST(p.ID_OPERARIO AS NVARCHAR(20)) AS NVARCHAR(200)) AS operario_nombre';
    DECLARE @ConceptoJoin NVARCHAR(MAX) = N'';
    DECLARE @ConceptoSelect NVARCHAR(MAX) = N'CAST(NULL AS NVARCHAR(100)) AS concepto_nombre';
    DECLARE @sql NVARCHAR(MAX);
    DECLARE @params NVARCHAR(MAX) = N'@IdParteOperario INT';

    IF @IdParteOperario IS NULL OR @IdParteOperario <= 0
        OR OBJECT_ID(N'dbo.PQ_PRD_PARTES_OPERARIO', N'U') IS NULL
        OR NOT EXISTS (
            SELECT 1
            FROM dbo.PQ_PRD_PARTES_OPERARIO
            WHERE ID_PARTE_OPERARIO = @IdParteOperario
        )
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_operario,
            CAST(NULL AS NVARCHAR(200)) AS operario_nombre,
            CAST(NULL AS DATE) AS fecha_parte,
            CAST(NULL AS INT) AS id_turno,
            CAST(NULL AS NVARCHAR(100)) AS turno_nombre,
            CAST(NULL AS INT) AS estado,
            CAST(NULL AS NVARCHAR(2000)) AS observaciones
        WHERE 1 = 0;

        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_concepto_tiempo,
            CAST(NULL AS NVARCHAR(100)) AS concepto_nombre,
            CAST(NULL AS INT) AS minutos,
            CAST(NULL AS DECIMAL(18, 4)) AS unidades_hechas,
            CAST(NULL AS NVARCHAR(2000)) AS notas,
            CAST(NULL AS NVARCHAR(2000)) AS notas_revision
        WHERE 1 = 0;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_TURNOS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'ID_TURNO') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_TURNOS', N'NOMBRE') IS NOT NULL
    BEGIN
        SET @TurnoJoin = N'
        LEFT JOIN dbo.PQ_PRD_TURNOS t ON t.ID_TURNO = p.ID_TURNO';
        SET @TurnoSelect = N'LTRIM(RTRIM(CAST(t.NOMBRE AS NVARCHAR(100)))) AS turno_nombre';
    END

    IF OBJECT_ID(N'dbo.PQ_SUELD_LEGAJOS', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_SUELD_LEGAJOS', N'ID') IS NOT NULL
    BEGIN
        SET @LegajoJoin = N'
        LEFT JOIN dbo.PQ_SUELD_LEGAJOS l ON l.ID = p.ID_OPERARIO';
        SET @OperarioSelect = N'
        CASE
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(CAST(l.APELLIDO AS NVARCHAR(80)), N'''') + N'' '' + ISNULL(CAST(l.NOMBRE AS NVARCHAR(80)), N''''))), N'''') IS NULL
                THEN CAST(N''Op.'' + CAST(p.ID_OPERARIO AS NVARCHAR(20)) AS NVARCHAR(200))
            ELSE CAST(LTRIM(RTRIM(ISNULL(CAST(l.APELLIDO AS NVARCHAR(80)), N'''') + N'' '' + ISNULL(CAST(l.NOMBRE AS NVARCHAR(80)), N''''))) AS NVARCHAR(200))
        END AS operario_nombre';
    END

    SET @sql = N'
    SELECT TOP 1
        CAST(p.ID_PARTE_OPERARIO AS INT) AS id,
        CAST(p.ID_OPERARIO AS INT) AS id_operario,
        ' + @OperarioSelect + N',
        CAST(p.FECHA_PARTE AS DATE) AS fecha_parte,
        CAST(p.ID_TURNO AS INT) AS id_turno,
        ' + @TurnoSelect + N',
        CAST(p.ESTADO AS INT) AS estado,
        CAST(p.OBSERVACIONES AS NVARCHAR(2000)) AS observaciones
    FROM dbo.PQ_PRD_PARTES_OPERARIO p
    ' + @TurnoJoin + N'
    ' + @LegajoJoin + N'
    WHERE p.ID_PARTE_OPERARIO = @IdParteOperario;';

    EXEC sp_executesql
        @sql,
        @params,
        @IdParteOperario = @IdParteOperario;

    IF OBJECT_ID(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'U') IS NULL
        OR COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_PARTE_OPERARIO') IS NULL
        OR COL_LENGTH(N'dbo.PQ_PRD_PARTES_ENTRADAS', N'ID_PARTE_ENTRADA') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS id_concepto_tiempo,
            CAST(NULL AS NVARCHAR(100)) AS concepto_nombre,
            CAST(NULL AS INT) AS minutos,
            CAST(NULL AS DECIMAL(18, 4)) AS unidades_hechas,
            CAST(NULL AS NVARCHAR(2000)) AS notas,
            CAST(NULL AS NVARCHAR(2000)) AS notas_revision
        WHERE 1 = 0;
        RETURN;
    END

    IF OBJECT_ID(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'ID_CONCEPTO_TIEMPO') IS NOT NULL
       AND COL_LENGTH(N'dbo.PQ_PRD_CONCEPTOS_TIEMPO', N'NOMBRE') IS NOT NULL
    BEGIN
        SET @ConceptoJoin = N'
        LEFT JOIN dbo.PQ_PRD_CONCEPTOS_TIEMPO c ON c.ID_CONCEPTO_TIEMPO = e.ID_CONCEPTO_TIEMPO';
        SET @ConceptoSelect = N'LTRIM(RTRIM(CAST(c.NOMBRE AS NVARCHAR(100)))) AS concepto_nombre';
    END

    SET @sql = N'
    SELECT
        CAST(e.ID_PARTE_ENTRADA AS INT) AS id,
        CAST(e.ID_CONCEPTO_TIEMPO AS INT) AS id_concepto_tiempo,
        ' + @ConceptoSelect + N',
        CAST(e.MINUTOS AS INT) AS minutos,
        CAST(e.UNIDADES_HECHAS AS DECIMAL(18, 4)) AS unidades_hechas,
        CAST(e.NOTAS AS NVARCHAR(2000)) AS notas,
        CAST(e.NOTAS_REVISION AS NVARCHAR(2000)) AS notas_revision
    FROM dbo.PQ_PRD_PARTES_ENTRADAS e
    ' + @ConceptoJoin + N'
    WHERE e.ID_PARTE_OPERARIO = @IdParteOperario
    ORDER BY e.ID_PARTE_ENTRADA;';

    EXEC sp_executesql
        @sql,
        @params,
        @IdParteOperario = @IdParteOperario;
END
GO
