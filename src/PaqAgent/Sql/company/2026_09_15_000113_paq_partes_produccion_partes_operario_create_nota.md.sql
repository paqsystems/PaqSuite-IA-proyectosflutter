-- D6.10.2 PartesProduccion.PartesOperario.Create
-- El alta se orquesta en PaqAgent (PartesOperarioCreateRunner):
--   TX company: resolver operario, validar turno, unicidad fecha/turno/operario, INSERT ESTADO=0.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_PartesOperarioCreate cuando el alcance se estabilice.
SELECT CAST(N'D6.10.2 PartesOperario.Create = runner orquestado (ver PartesOperarioCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
