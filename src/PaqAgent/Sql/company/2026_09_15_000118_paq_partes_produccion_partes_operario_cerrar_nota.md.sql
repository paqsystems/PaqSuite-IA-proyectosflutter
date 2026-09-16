-- D6.10.7 PartesProduccion.PartesOperario.Cerrar
-- Reviewed→Locked se orquesta en PaqAgent (PartesOperarioCerrarRunner):
--   TX company: circuito activo, sin filtro de propietario, ESTADO=2 → 3.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_PartesOperarioCerrar cuando el alcance se estabilice.
SELECT CAST(N'D6.10.7 PartesOperario.Cerrar = runner orquestado (ver PartesOperarioCerrarRunner.cs)' AS NVARCHAR(200)) AS nota;
