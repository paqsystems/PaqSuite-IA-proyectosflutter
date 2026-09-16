-- D6.8.6 PartesProduccion.OrdenesTrabajo.PatchEstado
-- Patch estado orquestado en PaqAgent (OrdenesTrabajoPatchEstadoRunner):
-- Params: _database, id_orden_trabajo, estado (0|1|2), usuario_id opcional.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_OrdenesTrabajoPatchEstado cuando el alcance se estabilice.
SELECT CAST(N'D6.8.6 OrdenesTrabajo.PatchEstado = runner orquestado (ver OrdenesTrabajoPatchEstadoRunner.cs)' AS NVARCHAR(200)) AS nota;
