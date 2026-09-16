-- D6.10.5 PartesProduccion.PartesOperario.Aprobar
-- Submitted→Reviewed se orquesta en PaqAgent (PartesOperarioAprobarRunner):
--   TX company: circuito activo, sin filtro de propietario, ESTADO=1 → 2,
--   FECHA_REVISION / ID_USUARIO_REVISION.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_PartesOperarioAprobar cuando el alcance se estabilice.
SELECT CAST(N'D6.10.5 PartesOperario.Aprobar = runner orquestado (ver PartesOperarioAprobarRunner.cs)' AS NVARCHAR(200)) AS nota;
