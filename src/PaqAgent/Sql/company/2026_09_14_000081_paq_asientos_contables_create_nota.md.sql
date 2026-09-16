-- D6.7.2 AsientosContables.Create
-- El alta se orquesta en PaqAgent (AsientosContablesCreateRunner):
--   TX company: cabecera ASIENTO_ANALITICO_CN → renglón → importe → auxiliar → subauxiliar.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_AsientosContables_Create cuando el alcance se estabilice.
SELECT CAST(N'D6.7.2 AsientosContables.Create = runner orquestado (ver AsientosContablesCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
