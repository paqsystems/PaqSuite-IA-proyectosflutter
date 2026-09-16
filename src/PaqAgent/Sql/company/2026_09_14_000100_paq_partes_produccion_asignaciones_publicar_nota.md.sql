-- D6.9.5 PartesProduccion.Asignaciones.Publicar
-- La publicación se orquesta en PaqAgent (AsignacionesPublicarRunner):
--   TX: validar plan (items+operarios), congelar UNIDADES_HORA_STD desde STD vigente,
--   UPDATE ESTADO=1 + FECHA_PUBLICACION.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesPublicar ...
SELECT CAST(N'D6.9.5 Asignaciones.Publicar = runner orquestado (ver AsignacionesPublicarRunner.cs)' AS NVARCHAR(200)) AS nota;
