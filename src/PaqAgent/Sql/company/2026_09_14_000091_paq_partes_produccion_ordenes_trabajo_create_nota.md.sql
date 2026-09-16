-- D6.8.3 PartesProduccion.OrdenesTrabajo.Create
-- El alta se orquesta en PaqAgent (OrdenesTrabajoCreateRunner):
--   TX company: 1..N filas PQ_PRD_ORDENES_TRABAJO (NRO_ORDEN) + numeración OT + validaciones catálogo.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_OrdenesTrabajoCreate cuando el alcance se estabilice.
SELECT CAST(N'D6.8.3 OrdenesTrabajo.Create = runner orquestado (ver OrdenesTrabajoCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
