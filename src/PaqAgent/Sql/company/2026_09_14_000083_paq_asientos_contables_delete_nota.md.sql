-- D6.7.4 AsientosContables.Delete
-- El DELETE se orquesta en PaqAgent (AsientosContablesDeleteRunner):
-- load cabecera + rowVersion → validaciones → TX deleteHijos → delete cabecera.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_AsientosContables_Delete cuando el alcance se estabilice.
SELECT CAST(N'D6.7.4 AsientosContables.Delete = runner orquestado (ver AsientosContablesDeleteRunner.cs)' AS NVARCHAR(200)) AS nota;
