-- D6.9.6 PartesProduccion.Asignaciones.Cerrar
-- El cierre se orquesta en PaqAgent (AsignacionesCerrarRunner):
--   TX: validar ESTADO=1 (Published), UPDATE ESTADO=2 + FECHA_CIERRE.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesCerrar ...
SELECT CAST(N'D6.9.6 Asignaciones.Cerrar = runner orquestado (ver AsignacionesCerrarRunner.cs)' AS NVARCHAR(200)) AS nota;
