-- D6.8.6 PartesProduccion.OrdenesTrabajo.CambioMasivoEstado
-- Cambio masivo orquestado en PaqAgent (OrdenesTrabajoCambioMasivoEstadoRunner):
-- Params: _database, ids_json (array int), operacion (cerrar|reabrir), usuario_id opcional.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_OrdenesTrabajoCambioMasivoEstado cuando el alcance se estabilice.
SELECT CAST(N'D6.8.6 OrdenesTrabajo.CambioMasivoEstado = runner orquestado (ver OrdenesTrabajoCambioMasivoEstadoRunner.cs)' AS NVARCHAR(200)) AS nota;
