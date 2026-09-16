-- D6.9.3 PartesProduccion.Asignaciones.Create
-- El alta se orquesta en PaqAgent (AsignacionesCreateRunner):
--   TX company: INSERT PQ_PRD_ASIGNACIONES (Draft) + validaciones fecha/turno/tipo_tarea.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesCreate cuando el alcance se estabilice.
SELECT CAST(N'D6.9.3 Asignaciones.Create = runner orquestado (ver AsignacionesCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
