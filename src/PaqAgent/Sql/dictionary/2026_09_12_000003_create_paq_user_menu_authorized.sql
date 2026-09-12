/*
================================================================================
  PAQ_User_Menu_Authorized
  Menú autorizado por usuario + empresa (F3 / menu.authorized).

  Consumidor: PaqAgent → Gateway → Laravel (GatewayMenuAuthzService)
  Parámetros job: user_id, empresa_id

  Replica UserMenuQuery::authorizedForUserEmpresa (TANGO) con detección
  dinámica de columnas legacy (PascalCase) vs híbrido (snake_case).

  Salida: DOS result sets fijos
    1) Header — 1 fila: status, empresa_id, acceso_total, error_message
    2) Items  — 0..N filas si status = OK

  Estados header.status:
    OK                   — menú resuelto (items puede estar vacío)
    INVALID_PARAMETERS   — user/empresa <= 0
    SQL_ERROR            — fallo inesperado
================================================================================
*/
CREATE OR ALTER PROCEDURE dbo.PAQ_User_Menu_Authorized
    @UserId    INT,
    @EmpresaId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SET LOCK_TIMEOUT 8000;

    DECLARE @Status         NVARCHAR(30)  = N'OK';
    DECLARE @AccesoTotal    BIT           = 0;
    DECLARE @ErrorMessage   NVARCHAR(500) = NULL;

    DECLARE @ColPermisoRol       SYSNAME;
    DECLARE @ColPermisoEmpresa   SYSNAME;
    DECLARE @ColPermisoUsuario   SYSNAME;
    DECLARE @ColRolPk            SYSNAME;
    DECLARE @ColRolAccesoTotal   SYSNAME;

    DECLARE @ColMenuId           SYSNAME;
    DECLARE @ColMenuText         SYSNAME;
    DECLARE @ColMenuProc         SYSNAME;
    DECLARE @ColMenuEnabled      SYSNAME;
    DECLARE @ColMenuParent       SYSNAME;
    DECLARE @ColMenuOrder        SYSNAME;
    DECLARE @ColMenuRoute        SYSNAME;
    DECLARE @ColMenuIcon         SYSNAME = NULL;
    DECLARE @ColMenuTipoProceso  SYSNAME = NULL;

    DECLARE @ColAtrRol           SYSNAME;
    DECLARE @ColAtrMenu          SYSNAME;
    DECLARE @ColAtrAlta          SYSNAME;
    DECLARE @ColAtrBaja          SYSNAME;
    DECLARE @ColAtrModi          SYSNAME;
    DECLARE @ColAtrRepo          SYSNAME;

    DECLARE @HasMenus            BIT = 0;
    DECLARE @HasPermiso          BIT = 0;
    DECLARE @HasRol              BIT = 0;
    DECLARE @HasRolAtributo      BIT = 0;

    DECLARE @Sql                 NVARCHAR(MAX);
    DECLARE @AccesoOut           BIT;
    DECLARE @RowsAdded           INT;

    CREATE TABLE #AuthorizedMenuIds (id INT NOT NULL PRIMARY KEY);
    CREATE TABLE #MenuItems
    (
        id             INT            NOT NULL,
        [text]         NVARCHAR(150)  NOT NULL,
        parentId       INT            NULL,
        orden          INT            NOT NULL,
        routeName      NVARCHAR(100)  NULL,
        procedimiento  NVARCHAR(150)  NULL,
        icon_name      NVARCHAR(50)   NULL,
        tipo_proceso   NVARCHAR(10)   NULL
    );

    BEGIN TRY
        IF @UserId IS NULL OR @UserId <= 0 OR @EmpresaId IS NULL OR @EmpresaId <= 0
        BEGIN
            SET @Status = N'INVALID_PARAMETERS';
            SET @ErrorMessage = N'user_id y empresa_id deben ser > 0.';
            GOTO EmitResults;
        END

        SET @HasMenus = CASE WHEN OBJECT_ID(N'dbo.pq_menus', N'U') IS NOT NULL THEN 1 ELSE 0 END;
        SET @HasPermiso = CASE WHEN OBJECT_ID(N'dbo.pq_permiso', N'U') IS NOT NULL THEN 1 ELSE 0 END;
        SET @HasRol = CASE WHEN OBJECT_ID(N'dbo.pq_rol', N'U') IS NOT NULL THEN 1 ELSE 0 END;
        SET @HasRolAtributo = CASE WHEN OBJECT_ID(N'dbo.pq_rol_atributo', N'U') IS NOT NULL THEN 1 ELSE 0 END;

        IF @HasMenus = 0 OR @HasPermiso = 0 OR @HasRol = 0
        BEGIN
            GOTO EmitResults;
        END

        /* --- pq_permiso / pq_rol --- */
        SELECT @ColPermisoRol = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDRol')
            THEN N'IDRol' ELSE N'id_rol' END;

        SELECT @ColPermisoEmpresa = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDEmpresa')
            THEN N'IDEmpresa' ELSE N'id_empresa' END;

        SELECT @ColPermisoUsuario = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDUsuario')
            THEN N'IDUsuario' ELSE N'id_usuario' END;

        SELECT @ColRolPk = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'IDRol')
            THEN N'IDRol' ELSE N'id' END;

        SELECT @ColRolAccesoTotal = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'AccesoTotal')
            THEN N'AccesoTotal' ELSE N'acceso_total' END;

        /* --- pq_menus --- */
        SELECT @ColMenuId = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'ID')
            THEN N'ID' ELSE N'id' END;

        SELECT @ColMenuText = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'Text') THEN N'Text'
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'text') THEN N'text'
            ELSE N'descr' END;

        SELECT @ColMenuProc = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'Procedimiento')
            THEN N'Procedimiento' ELSE N'procedimiento' END;

        SELECT @ColMenuEnabled = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'Enabled')
            THEN N'Enabled' ELSE N'enabled' END;

        SELECT @ColMenuParent = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'IDParent') THEN N'IDParent'
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'Idparent') THEN N'Idparent'
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'idparent') THEN N'idparent'
            ELSE N'parent' END;

        SELECT @ColMenuOrder = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'Order') THEN N'Order'
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'order') THEN N'order'
            ELSE N'orden' END;

        SELECT @ColMenuRoute = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'routeName') THEN N'routeName'
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'route_name') THEN N'route_name'
            ELSE N'routeName' END;

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'icon_name')
            SET @ColMenuIcon = N'icon_name';
        ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'IconName')
            SET @ColMenuIcon = N'IconName';

        IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'tipo_proceso')
            SET @ColMenuTipoProceso = N'tipo_proceso';
        ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_menus' AND COLUMN_NAME = N'TipoProceso')
            SET @ColMenuTipoProceso = N'TipoProceso';

        /* acceso_total para user+empresa */
        SET @AccesoOut = 0;
        SET @Sql = N'
IF EXISTS (
    SELECT 1
    FROM dbo.pq_permiso AS p
    INNER JOIN dbo.pq_rol AS r
        ON p.' + QUOTENAME(@ColPermisoRol) + N' = r.' + QUOTENAME(@ColRolPk) + N'
    WHERE p.' + QUOTENAME(@ColPermisoUsuario) + N' = @pUserId
      AND p.' + QUOTENAME(@ColPermisoEmpresa) + N' = @pEmpresaId
      AND r.' + QUOTENAME(@ColRolAccesoTotal) + N' = 1
)
    SET @pAccesoOut = 1;';

        EXEC sys.sp_executesql
            @Sql,
            N'@pUserId INT, @pEmpresaId INT, @pAccesoOut BIT OUTPUT',
            @pUserId = @UserId,
            @pEmpresaId = @EmpresaId,
            @pAccesoOut = @AccesoOut OUTPUT;

        SET @AccesoTotal = @AccesoOut;

        IF @AccesoTotal = 1
        BEGIN
            SET @Sql = N'
INSERT INTO #AuthorizedMenuIds (id)
SELECT m.' + QUOTENAME(@ColMenuId) + N'
FROM dbo.pq_menus AS m
WHERE m.' + QUOTENAME(@ColMenuEnabled) + N' = 1
  AND m.' + QUOTENAME(@ColMenuText) + N' IS NOT NULL
  AND LEN(LTRIM(RTRIM(m.' + QUOTENAME(@ColMenuText) + N'))) > 0';

            EXEC sys.sp_executesql @Sql;
        END
        ELSE IF @HasRolAtributo = 1
        BEGIN
            SELECT @ColAtrRol = CASE
                WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol_atributo' AND COLUMN_NAME = N'IDRol')
                THEN N'IDRol' ELSE N'id_rol' END;

            SELECT @ColAtrMenu = CASE
                WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol_atributo' AND COLUMN_NAME = N'IDOpcionMenu')
                THEN N'IDOpcionMenu' ELSE N'id_opcion_menu' END;

            SELECT @ColAtrAlta = CASE
                WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol_atributo' AND COLUMN_NAME = N'PermisoAlta')
                THEN N'PermisoAlta' ELSE N'permiso_alta' END;

            SELECT @ColAtrBaja = CASE
                WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol_atributo' AND COLUMN_NAME = N'PermisoBaja')
                THEN N'PermisoBaja' ELSE N'permiso_baja' END;

            SELECT @ColAtrModi = CASE
                WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol_atributo' AND COLUMN_NAME = N'PermisoModi')
                THEN N'PermisoModi' ELSE N'permiso_modi' END;

            SELECT @ColAtrRepo = CASE
                WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol_atributo' AND COLUMN_NAME = N'PermisoRepo')
                THEN N'PermisoRepo' ELSE N'permiso_repo' END;

            SET @Sql = N'
INSERT INTO #AuthorizedMenuIds (id)
SELECT DISTINCT a.' + QUOTENAME(@ColAtrMenu) + N'
FROM dbo.pq_permiso AS p
INNER JOIN dbo.pq_rol AS r
    ON p.' + QUOTENAME(@ColPermisoRol) + N' = r.' + QUOTENAME(@ColRolPk) + N'
INNER JOIN dbo.pq_rol_atributo AS a
    ON a.' + QUOTENAME(@ColAtrRol) + N' = r.' + QUOTENAME(@ColRolPk) + N'
WHERE p.' + QUOTENAME(@ColPermisoUsuario) + N' = @pUserId
  AND p.' + QUOTENAME(@ColPermisoEmpresa) + N' = @pEmpresaId
  AND (
        a.' + QUOTENAME(@ColAtrAlta) + N' = 1
     OR a.' + QUOTENAME(@ColAtrBaja) + N' = 1
     OR a.' + QUOTENAME(@ColAtrModi) + N' = 1
     OR a.' + QUOTENAME(@ColAtrRepo) + N' = 1
  );';

            EXEC sys.sp_executesql
                @Sql,
                N'@pUserId INT, @pEmpresaId INT',
                @pUserId = @UserId,
                @pEmpresaId = @EmpresaId;

            /* Ancestros (loop; evita CTE sobre la misma #temp) */
            SET @RowsAdded = 1;
            WHILE @RowsAdded > 0
            BEGIN
                SET @Sql = N'
INSERT INTO #AuthorizedMenuIds (id)
SELECT DISTINCT m.' + QUOTENAME(@ColMenuParent) + N'
FROM dbo.pq_menus AS m
INNER JOIN #AuthorizedMenuIds AS a ON a.id = m.' + QUOTENAME(@ColMenuId) + N'
WHERE m.' + QUOTENAME(@ColMenuParent) + N' IS NOT NULL
  AND m.' + QUOTENAME(@ColMenuParent) + N' > 0
  AND NOT EXISTS (
        SELECT 1 FROM #AuthorizedMenuIds AS x
        WHERE x.id = m.' + QUOTENAME(@ColMenuParent) + N'
  );';

                EXEC sys.sp_executesql @Sql;
                SET @RowsAdded = @@ROWCOUNT;
            END
        END

        IF EXISTS (SELECT 1 FROM #AuthorizedMenuIds)
        BEGIN
            SET @Sql = N'
INSERT INTO #MenuItems (id, [text], parentId, orden, routeName, procedimiento, icon_name, tipo_proceso)
SELECT
    m.' + QUOTENAME(@ColMenuId) + N',
    m.' + QUOTENAME(@ColMenuText) + N',
    CASE
        WHEN m.' + QUOTENAME(@ColMenuParent) + N' IS NULL OR m.' + QUOTENAME(@ColMenuParent) + N' <= 0 THEN NULL
        ELSE m.' + QUOTENAME(@ColMenuParent) + N'
    END,
    ISNULL(m.' + QUOTENAME(@ColMenuOrder) + N', 0),
    m.' + QUOTENAME(@ColMenuRoute) + N',
    m.' + QUOTENAME(@ColMenuProc) + N',
    ' + CASE WHEN @ColMenuIcon IS NULL THEN N'CAST(NULL AS NVARCHAR(50))' ELSE N'm.' + QUOTENAME(@ColMenuIcon) END + N',
    ' + CASE WHEN @ColMenuTipoProceso IS NULL THEN N'CAST(NULL AS NVARCHAR(10))' ELSE N'm.' + QUOTENAME(@ColMenuTipoProceso) END + N'
FROM dbo.pq_menus AS m
INNER JOIN #AuthorizedMenuIds AS a ON a.id = m.' + QUOTENAME(@ColMenuId) + N'
WHERE m.' + QUOTENAME(@ColMenuEnabled) + N' = 1
  AND m.' + QUOTENAME(@ColMenuText) + N' IS NOT NULL
  AND LEN(LTRIM(RTRIM(m.' + QUOTENAME(@ColMenuText) + N'))) > 0';

            EXEC sys.sp_executesql @Sql;
        END

EmitResults:
        SELECT
            @Status                   AS [status],
            @EmpresaId                AS [empresa_id],
            CAST(@AccesoTotal AS BIT) AS [acceso_total],
            @ErrorMessage             AS [error_message];

        SELECT
            i.id,
            i.[text],
            i.parentId,
            i.orden,
            i.routeName,
            i.procedimiento,
            i.icon_name,
            i.tipo_proceso
        FROM #MenuItems AS i
        WHERE @Status = N'OK'
        ORDER BY ISNULL(i.parentId, 0), i.orden, i.id;
    END TRY
    BEGIN CATCH
        SELECT
            N'SQL_ERROR' AS [status],
            @EmpresaId AS [empresa_id],
            CAST(0 AS BIT) AS [acceso_total],
            N'Error interno al resolver el menu autorizado.' AS [error_message];

        SELECT
            CAST(NULL AS INT) AS [id],
            CAST(NULL AS NVARCHAR(150)) AS [text],
            CAST(NULL AS INT) AS [parentId],
            CAST(NULL AS INT) AS [orden],
            CAST(NULL AS NVARCHAR(100)) AS [routeName],
            CAST(NULL AS NVARCHAR(150)) AS [procedimiento],
            CAST(NULL AS NVARCHAR(50)) AS [icon_name],
            CAST(NULL AS NVARCHAR(10)) AS [tipo_proceso]
        WHERE 1 = 0;
    END CATCH
END
GO
