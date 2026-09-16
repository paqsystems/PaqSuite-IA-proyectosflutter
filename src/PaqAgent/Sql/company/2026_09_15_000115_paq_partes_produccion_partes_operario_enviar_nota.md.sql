-- D6.10.4 PartesProduccion.PartesOperario.Enviar
-- Open→Submitted se orquesta en PaqAgent (PartesOperarioEnviarRunner):
--   TX company: circuito_autorizacion_activo, propietario + ESTADO=0,
--   entradas con concepto/minutos (origen_carga=libre exige OT), UPDATE ESTADO=1.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_PartesOperarioEnviar cuando el alcance se estabilice.
SELECT CAST(N'D6.10.4 PartesOperario.Enviar = runner orquestado (ver PartesOperarioEnviarRunner.cs)' AS NVARCHAR(200)) AS nota;
