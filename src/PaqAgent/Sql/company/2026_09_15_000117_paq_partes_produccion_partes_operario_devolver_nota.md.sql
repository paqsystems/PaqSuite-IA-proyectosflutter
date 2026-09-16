-- D6.10.6 PartesProduccion.PartesOperario.Devolver
-- Submitted→Open se orquesta en PaqAgent (PartesOperarioDevolverRunner):
--   TX company: circuito activo, sin filtro de propietario, ESTADO=1 → 0,
--   limpia FECHA_REVISION / ID_USUARIO_REVISION.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_PartesOperarioDevolver cuando el alcance se estabilice.
SELECT CAST(N'D6.10.6 PartesOperario.Devolver = runner orquestado (ver PartesOperarioDevolverRunner.cs)' AS NVARCHAR(200)) AS nota;
