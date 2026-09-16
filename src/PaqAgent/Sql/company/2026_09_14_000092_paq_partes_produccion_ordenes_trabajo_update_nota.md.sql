-- D6.8.4 PartesProduccion.OrdenesTrabajo.Update
-- Update orquestado en PaqAgent (OrdenesTrabajoUpdateRunner):
--   TX company: update por id_orden_trabajo O sync multi-op por codigo_ot (OrdenTrabajoSyncService).
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_OrdenesTrabajoUpdate cuando el alcance se estabilice.
SELECT CAST(N'D6.8.4 OrdenesTrabajo.Update = runner orquestado (ver OrdenesTrabajoUpdateRunner.cs)' AS NVARCHAR(200)) AS nota;
