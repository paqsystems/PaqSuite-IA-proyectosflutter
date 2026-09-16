/*
================================================================================
  PAQ_Auth_ChangePassword
  Actualiza password_hash de dbo.USERS (cambio de contraseña autenticado).

  Consumidor: PaqAgent → Agent Gateway → Laravel
  Laravel valida la contraseña actual con Hash::check (auth.login) y envía
  el hash bcrypt nuevo. Este SP NO recibe password en claro.

  Parámetros job: user_id, codigo, password_hash
  El UPDATE exige id + codigo para no cambiar la fila de otro usuario.

  Salida: UN result set (1 fila)
    status: OK | NOT_FOUND | INVALID_PARAMETERS | SQL_ERROR
================================================================================
*/
CREATE OR ALTER PROCEDURE dbo.PAQ_Auth_ChangePassword
    @UserId       INT,
    @Codigo       NVARCHAR(100),
    @PasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET LOCK_TIMEOUT 8000;

    DECLARE @Status       NVARCHAR(30)  = N'OK';
    DECLARE @ErrorMessage NVARCHAR(500) = NULL;
    DECLARE @Updated      INT           = 0;

    BEGIN TRY
        IF @UserId IS NULL OR @UserId <= 0
           OR @Codigo IS NULL OR LTRIM(RTRIM(@Codigo)) = N''
           OR @PasswordHash IS NULL OR LEN(LTRIM(RTRIM(@PasswordHash))) < 20
        BEGIN
            SELECT N'INVALID_PARAMETERS' AS [status], CAST(NULL AS NVARCHAR(500)) AS error_message;
            RETURN;
        END;

        UPDATE dbo.USERS
        SET password_hash = @PasswordHash
        WHERE id = @UserId
          AND codigo = @Codigo;

        SET @Updated = @@ROWCOUNT;

        IF @Updated = 0
        BEGIN
            SELECT N'NOT_FOUND' AS [status], CAST(NULL AS NVARCHAR(500)) AS error_message;
            RETURN;
        END;

        SELECT N'OK' AS [status], CAST(NULL AS NVARCHAR(500)) AS error_message;
    END TRY
    BEGIN CATCH
        SELECT
            N'SQL_ERROR' AS [status],
            CAST(N'PAQ_Auth_ChangePassword error ' + CAST(ERROR_NUMBER() AS NVARCHAR(20)) AS NVARCHAR(500)) AS error_message;
    END CATCH;
END;
GO
