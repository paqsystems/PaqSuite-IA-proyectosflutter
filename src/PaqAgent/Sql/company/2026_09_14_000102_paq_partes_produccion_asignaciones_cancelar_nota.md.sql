-- D6.9.7 PartesProduccion.Asignaciones.Cancelar
-- La anulación se orquesta en PaqAgent (AsignacionesCancelarRunner):
--   TX: validar ESTADO not in (2,3), UPDATE ESTADO=3.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesCancelar ...
SELECT CAST(N'D6.9.7 Asignaciones.Cancelar = runner orquestado (ver AsignacionesCancelarRunner.cs)' AS NVARCHAR(200)) AS nota;
