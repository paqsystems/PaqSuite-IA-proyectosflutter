/*
================================================================================
  PAQ_Seguridad_*_List — listados administración Seguridad (CC #9 / modo agente)

  Consumidor: PaqAgent → Gateway → Laravel (SeguridadGatewayService)
  Conexión: dictionary (sin _database)

  SPs:
    PAQ_Seguridad_Roles_List
    PAQ_Seguridad_Users_List
    PAQ_Seguridad_Empresas_List
    PAQ_Seguridad_Permisos_List
    PAQ_Seguridad_GruposEmpresarios_List

  Salida: UN result set con filas; el runner serializa en data.items
================================================================================
*/

CREATE OR ALTER PROCEDURE dbo.PAQ_Seguridad_Roles_List
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SET LOCK_TIMEOUT 8000;

    IF OBJECT_ID(N'dbo.pq_rol', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(100)) AS nombreRol,
            CAST(NULL AS NVARCHAR(100)) AS descripcionRol,
            CAST(NULL AS BIT) AS accesoTotal,
            CAST(NULL AS BIT) AS enUso
        WHERE 1 = 0;
        RETURN;
    END;

    DECLARE @ColRolPk          SYSNAME;
    DECLARE @ColRolNombre      SYSNAME;
    DECLARE @ColRolDesc        SYSNAME;
    DECLARE @ColRolAcceso      SYSNAME;
    DECLARE @ColPermisoRol     SYSNAME;
    DECLARE @Sql               NVARCHAR(MAX);

    SELECT @ColRolPk = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'IDRol')
        THEN N'IDRol' ELSE N'id' END;

    SELECT @ColRolNombre = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'NombreRol')
        THEN N'NombreRol' ELSE N'nombre_rol' END;

    SELECT @ColRolDesc = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'DescripcionRol')
        THEN N'DescripcionRol' ELSE N'descripcion_rol' END;

    SELECT @ColRolAcceso = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'AccesoTotal')
        THEN N'AccesoTotal' ELSE N'acceso_total' END;

    SELECT @ColPermisoRol = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDRol')
        THEN N'IDRol' ELSE N'id_rol' END;

    SET @Sql = N'
SELECT
    r.' + QUOTENAME(@ColRolPk) + N' AS id,
    r.' + QUOTENAME(@ColRolNombre) + N' AS nombreRol,
    r.' + QUOTENAME(@ColRolDesc) + N' AS descripcionRol,
    CAST(r.' + QUOTENAME(@ColRolAcceso) + N' AS BIT) AS accesoTotal,
    CAST(CASE WHEN EXISTS (
        SELECT 1 FROM dbo.pq_permiso AS p
        WHERE p.' + QUOTENAME(@ColPermisoRol) + N' = r.' + QUOTENAME(@ColRolPk) + N'
    ) THEN 1 ELSE 0 END AS BIT) AS enUso
FROM dbo.pq_rol AS r
ORDER BY r.' + QUOTENAME(@ColRolNombre) + N';';

    EXEC sys.sp_executesql @Sql;
END;
GO

CREATE OR ALTER PROCEDURE dbo.PAQ_Seguridad_Users_List
    @Codigo        NVARCHAR(50)  = NULL,
    @Nombre        NVARCHAR(255) = NULL,
    @Email         NVARCHAR(255) = NULL,
    @Activo        BIT           = NULL,
    @Inhabilitado  BIT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SET LOCK_TIMEOUT 8000;

    IF OBJECT_ID(N'dbo.USERS', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(50)) AS codigo,
            CAST(NULL AS NVARCHAR(255)) AS name,
            CAST(NULL AS NVARCHAR(255)) AS email,
            CAST(NULL AS BIT) AS activo,
            CAST(NULL AS BIT) AS inhabilitado,
            CAST(NULL AS BIT) AS es_supervisor,
            CAST(NULL AS DATETIMEOFFSET) AS created_at
        WHERE 1 = 0;
        RETURN;
    END;

    DECLARE @ColPermisoRol     SYSNAME;
    DECLARE @ColPermisoUsuario SYSNAME;
    DECLARE @ColRolPk          SYSNAME;
    DECLARE @ColRolAcceso      SYSNAME;
    DECLARE @HasPermiso        BIT = CASE WHEN OBJECT_ID(N'dbo.pq_permiso', N'U') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @HasRol            BIT = CASE WHEN OBJECT_ID(N'dbo.pq_rol', N'U') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @Sql               NVARCHAR(MAX);
    DECLARE @SupervisorExpr    NVARCHAR(MAX) = N'CAST(0 AS BIT)';

    IF @HasPermiso = 1 AND @HasRol = 1
    BEGIN
        SELECT @ColPermisoRol = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDRol')
            THEN N'IDRol' ELSE N'id_rol' END;

        SELECT @ColPermisoUsuario = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDUsuario')
            THEN N'IDUsuario' ELSE N'id_usuario' END;

        SELECT @ColRolPk = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'IDRol')
            THEN N'IDRol' ELSE N'id' END;

        SELECT @ColRolAcceso = CASE
            WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'AccesoTotal')
            THEN N'AccesoTotal' ELSE N'acceso_total' END;

        SET @SupervisorExpr = N'CAST(CASE WHEN EXISTS (
            SELECT 1
            FROM dbo.pq_permiso AS p
            INNER JOIN dbo.pq_rol AS r ON p.' + QUOTENAME(@ColPermisoRol) + N' = r.' + QUOTENAME(@ColRolPk) + N'
            WHERE p.' + QUOTENAME(@ColPermisoUsuario) + N' = u.id
              AND r.' + QUOTENAME(@ColRolAcceso) + N' = 1
        ) THEN 1 ELSE 0 END AS BIT)';
    END;

    SET @Sql = N'
SELECT
    u.id,
    u.codigo,
    u.name_user AS name,
    u.email,
    CAST(ISNULL(u.activo, 1) AS BIT) AS activo,
    CAST(ISNULL(u.inhabilitado, 0) AS BIT) AS inhabilitado,
    ' + @SupervisorExpr + N' AS es_supervisor,
    u.created_at
FROM dbo.USERS AS u
WHERE 1 = 1';

    IF @Codigo IS NOT NULL AND LTRIM(RTRIM(@Codigo)) <> N''
        SET @Sql = @Sql + N' AND u.codigo LIKE N''%'' + @pCodigo + N''%''';

    IF @Nombre IS NOT NULL AND LTRIM(RTRIM(@Nombre)) <> N''
        SET @Sql = @Sql + N' AND u.name_user LIKE N''%'' + @pNombre + N''%''';

    IF @Email IS NOT NULL AND LTRIM(RTRIM(@Email)) <> N''
        SET @Sql = @Sql + N' AND u.email LIKE N''%'' + @pEmail + N''%''';

    IF @Activo IS NOT NULL
        SET @Sql = @Sql + N' AND ISNULL(u.activo, 1) = @pActivo';

    IF @Inhabilitado IS NOT NULL
        SET @Sql = @Sql + N' AND ISNULL(u.inhabilitado, 0) = @pInhabilitado';

    SET @Sql = @Sql + N' ORDER BY u.codigo;';

    EXEC sys.sp_executesql
        @Sql,
        N'@pCodigo NVARCHAR(50), @pNombre NVARCHAR(255), @pEmail NVARCHAR(255), @pActivo BIT, @pInhabilitado BIT',
        @pCodigo = @Codigo,
        @pNombre = @Nombre,
        @pEmail = @Email,
        @pActivo = @Activo,
        @pInhabilitado = @Inhabilitado;
END;
GO

CREATE OR ALTER PROCEDURE dbo.PAQ_Seguridad_Empresas_List
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SET LOCK_TIMEOUT 8000;

    IF OBJECT_ID(N'dbo.pq_empresa', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(255)) AS nombreEmpresa,
            CAST(NULL AS NVARCHAR(255)) AS nombreBd,
            CAST(NULL AS INT) AS habilita,
            CAST(NULL AS NVARCHAR(MAX)) AS imagen,
            CAST(NULL AS NVARCHAR(50)) AS theme,
            CAST(NULL AS DATETIMEOFFSET) AS created_at,
            CAST(NULL AS DATETIMEOFFSET) AS updated_at
        WHERE 1 = 0;
        RETURN;
    END;

    DECLARE @ColEmpresaPk      SYSNAME;
    DECLARE @ColEmpresaNombre  SYSNAME;
    DECLARE @ColEmpresaBd      SYSNAME;
    DECLARE @ColEmpresaHab     SYSNAME;
    DECLARE @HasImagen         BIT = 0;
    DECLARE @HasTheme          BIT = 0;
    DECLARE @HasCreatedAt      BIT = 0;
    DECLARE @HasUpdatedAt      BIT = 0;
    DECLARE @Sql               NVARCHAR(MAX);

    SELECT @ColEmpresaPk = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'IDEmpresa')
        THEN N'IDEmpresa' ELSE N'id' END;

    SELECT @ColEmpresaNombre = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'NombreEmpresa')
        THEN N'NombreEmpresa' ELSE N'nombre_empresa' END;

    SELECT @ColEmpresaBd = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'NombreBD')
        THEN N'NombreBD' ELSE N'nombre_bd' END;

    SELECT @ColEmpresaHab = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'Habilita')
        THEN N'Habilita' ELSE N'habilita' END;

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'imagen')
        SET @HasImagen = 1;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'theme')
        SET @HasTheme = 1;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'created_at')
        SET @HasCreatedAt = 1;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'updated_at')
        SET @HasUpdatedAt = 1;

    SET @Sql = N'
SELECT
    e.' + QUOTENAME(@ColEmpresaPk) + N' AS id,
    e.' + QUOTENAME(@ColEmpresaNombre) + N' AS nombreEmpresa,
    e.' + QUOTENAME(@ColEmpresaBd) + N' AS nombreBd,
    e.' + QUOTENAME(@ColEmpresaHab) + N' AS habilita,
    ' + CASE WHEN @HasImagen = 1 THEN N'e.imagen' ELSE N'CAST(NULL AS NVARCHAR(MAX))' END + N' AS imagen,
    ' + CASE WHEN @HasTheme = 1 THEN N'ISNULL(e.theme, N''default'')' ELSE N'N''default''' END + N' AS theme,
    ' + CASE WHEN @HasCreatedAt = 1 THEN N'e.created_at' ELSE N'CAST(NULL AS DATETIMEOFFSET)' END + N' AS created_at,
    ' + CASE WHEN @HasUpdatedAt = 1 THEN N'e.updated_at' ELSE N'CAST(NULL AS DATETIMEOFFSET)' END + N' AS updated_at
FROM dbo.pq_empresa AS e
ORDER BY e.' + QUOTENAME(@ColEmpresaNombre) + N';';

    EXEC sys.sp_executesql @Sql;
END;
GO

CREATE OR ALTER PROCEDURE dbo.PAQ_Seguridad_Permisos_List
    @IdUsuario INT = NULL,
    @IdEmpresa INT = NULL,
    @IdRol     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SET LOCK_TIMEOUT 8000;

    IF OBJECT_ID(N'dbo.pq_permiso', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS INT) AS idUsuario,
            CAST(NULL AS INT) AS idEmpresa,
            CAST(NULL AS INT) AS idRol,
            CAST(NULL AS NVARCHAR(50)) AS usuarioCode,
            CAST(NULL AS NVARCHAR(255)) AS usuarioName,
            CAST(NULL AS NVARCHAR(255)) AS nombreEmpresa,
            CAST(NULL AS NVARCHAR(100)) AS nombreRol
        WHERE 1 = 0;
        RETURN;
    END;

    DECLARE @ColPermisoId      SYSNAME = N'id';
    DECLARE @ColPermisoUsuario SYSNAME;
    DECLARE @ColPermisoEmpresa SYSNAME;
    DECLARE @ColPermisoRol     SYSNAME;
    DECLARE @ColEmpresaPk      SYSNAME;
    DECLARE @ColEmpresaNombre  SYSNAME;
    DECLARE @ColRolPk          SYSNAME;
    DECLARE @ColRolNombre      SYSNAME;
    DECLARE @Sql               NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'id')
        SET @ColPermisoId = N'id';

    SELECT @ColPermisoUsuario = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDUsuario')
        THEN N'IDUsuario' ELSE N'id_usuario' END;

    SELECT @ColPermisoEmpresa = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDEmpresa')
        THEN N'IDEmpresa' ELSE N'id_empresa' END;

    SELECT @ColPermisoRol = CASE
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_permiso' AND COLUMN_NAME = N'IDRol')
        THEN N'IDRol' ELSE N'id_rol' END;

    SELECT @ColEmpresaPk = CASE
        WHEN OBJECT_ID(N'dbo.pq_empresa', N'U') IS NOT NULL
         AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'IDEmpresa')
        THEN N'IDEmpresa' ELSE N'id' END;

    SELECT @ColEmpresaNombre = CASE
        WHEN OBJECT_ID(N'dbo.pq_empresa', N'U') IS NOT NULL
         AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_empresa' AND COLUMN_NAME = N'NombreEmpresa')
        THEN N'NombreEmpresa' ELSE N'nombre_empresa' END;

    SELECT @ColRolPk = CASE
        WHEN OBJECT_ID(N'dbo.pq_rol', N'U') IS NOT NULL
         AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'IDRol')
        THEN N'IDRol' ELSE N'id' END;

    SELECT @ColRolNombre = CASE
        WHEN OBJECT_ID(N'dbo.pq_rol', N'U') IS NOT NULL
         AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_rol' AND COLUMN_NAME = N'NombreRol')
        THEN N'NombreRol' ELSE N'nombre_rol' END;

    SET @Sql = N'
SELECT
    p.' + QUOTENAME(@ColPermisoId) + N' AS id,
    p.' + QUOTENAME(@ColPermisoUsuario) + N' AS idUsuario,
    p.' + QUOTENAME(@ColPermisoEmpresa) + N' AS idEmpresa,
    p.' + QUOTENAME(@ColPermisoRol) + N' AS idRol,
    u.codigo AS usuarioCode,
    u.name_user AS usuarioName,
    e.' + QUOTENAME(@ColEmpresaNombre) + N' AS nombreEmpresa,
    r.' + QUOTENAME(@ColRolNombre) + N' AS nombreRol
FROM dbo.pq_permiso AS p
LEFT JOIN dbo.USERS AS u ON u.id = p.' + QUOTENAME(@ColPermisoUsuario) + N'
LEFT JOIN dbo.pq_empresa AS e ON e.' + QUOTENAME(@ColEmpresaPk) + N' = p.' + QUOTENAME(@ColPermisoEmpresa) + N'
LEFT JOIN dbo.pq_rol AS r ON r.' + QUOTENAME(@ColRolPk) + N' = p.' + QUOTENAME(@ColPermisoRol) + N'
WHERE 1 = 1';

    IF @IdUsuario IS NOT NULL AND @IdUsuario > 0
        SET @Sql = @Sql + N' AND p.' + QUOTENAME(@ColPermisoUsuario) + N' = @pIdUsuario';

    IF @IdEmpresa IS NOT NULL AND @IdEmpresa > 0
        SET @Sql = @Sql + N' AND p.' + QUOTENAME(@ColPermisoEmpresa) + N' = @pIdEmpresa';

    IF @IdRol IS NOT NULL AND @IdRol > 0
        SET @Sql = @Sql + N' AND p.' + QUOTENAME(@ColPermisoRol) + N' = @pIdRol';

    SET @Sql = @Sql + N' ORDER BY p.' + QUOTENAME(@ColPermisoId) + N';';

    EXEC sys.sp_executesql
        @Sql,
        N'@pIdUsuario INT, @pIdEmpresa INT, @pIdRol INT',
        @pIdUsuario = @IdUsuario,
        @pIdEmpresa = @IdEmpresa,
        @pIdRol = @IdRol;
END;
GO

CREATE OR ALTER PROCEDURE dbo.PAQ_Seguridad_GruposEmpresarios_List
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
    SET LOCK_TIMEOUT 8000;

    IF OBJECT_ID(N'dbo.pq_grupo_empresario', N'U') IS NULL
    BEGIN
        SELECT
            CAST(NULL AS INT) AS id,
            CAST(NULL AS NVARCHAR(255)) AS descripcion,
            CAST(NULL AS INT) AS cantidadEmpresas
        WHERE 1 = 0;
        RETURN;
    END;

    DECLARE @PivotGrupoCol   SYSNAME = N'id_grupo';
    DECLARE @Sql             NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_grupo_empresario_empresas' AND COLUMN_NAME = N'ID_Grupo')
        SET @PivotGrupoCol = N'ID_Grupo';
    ELSE IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'pq_grupo_empresario_empresas' AND COLUMN_NAME = N'id_grupo')
        SET @PivotGrupoCol = N'id_grupo';

    IF OBJECT_ID(N'dbo.pq_grupo_empresario_empresas', N'U') IS NULL
    BEGIN
        SELECT
            g.id,
            g.descripcion,
            CAST(0 AS INT) AS cantidadEmpresas
        FROM dbo.pq_grupo_empresario AS g
        ORDER BY g.descripcion;
        RETURN;
    END;

    SET @Sql = N'
SELECT
    g.id,
    g.descripcion,
    CAST(COUNT(ge.' + QUOTENAME(@PivotGrupoCol) + N') AS INT) AS cantidadEmpresas
FROM dbo.pq_grupo_empresario AS g
LEFT JOIN dbo.pq_grupo_empresario_empresas AS ge ON ge.' + QUOTENAME(@PivotGrupoCol) + N' = g.id
GROUP BY g.id, g.descripcion
ORDER BY g.descripcion;';

    EXEC sys.sp_executesql @Sql;
END;
GO
