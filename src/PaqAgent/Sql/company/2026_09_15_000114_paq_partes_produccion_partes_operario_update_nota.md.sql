-- D6.10.3 PartesProduccion.PartesOperario.Update
-- La edición Open se orquesta en PaqAgent (PartesOperarioUpdateRunner):
--   TX company: propietario + ESTADO=0, UPDATE observaciones / FECHA_MODIF / USUARIO_MODIF.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_PartesOperarioUpdate cuando el alcance se estabilice.
SELECT CAST(N'D6.10.3 PartesOperario.Update = runner orquestado (ver PartesOperarioUpdateRunner.cs)' AS NVARCHAR(200)) AS nota;
