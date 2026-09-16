-- D6.8.5 PartesProduccion.OrdenesTrabajo.Delete
-- Delete orquestado en PaqAgent (OrdenesTrabajoDeleteRunner):
--   TX company: baja por id_orden_trabajo (estado editable + vínculos asignaciones/partes).
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_OrdenesTrabajoDelete cuando el alcance se estabilice.
SELECT CAST(N'D6.8.5 OrdenesTrabajo.Delete = runner orquestado (ver OrdenesTrabajoDeleteRunner.cs)' AS NVARCHAR(200)) AS nota;
