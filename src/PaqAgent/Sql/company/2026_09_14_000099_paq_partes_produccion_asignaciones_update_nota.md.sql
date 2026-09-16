-- D6.9.4 PartesProduccion.Asignaciones.Update
-- La edición Draft se orquesta en PaqAgent (AsignacionesUpdateRunner):
--   TX company: validar ESTADO=0, bloquear cambio tipo_tarea con items, UPDATE cabecera.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesUpdate ...
SELECT CAST(N'D6.9.4 Asignaciones.Update = runner orquestado (ver AsignacionesUpdateRunner.cs)' AS NVARCHAR(200)) AS nota;
