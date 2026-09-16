-- D6.9.11 PartesProduccion.Asignaciones.Items.Delete
-- Delete orquestado en PaqAgent (AsignacionesItemsDeleteRunner):
--   TX: Draft only, DELETE item por id_asignacion/id_item.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesItemsDelete ...
SELECT CAST(N'D6.9.11 Asignaciones.Items.Delete = runner orquestado (ver AsignacionesItemsDeleteRunner.cs)' AS NVARCHAR(200)) AS nota;
