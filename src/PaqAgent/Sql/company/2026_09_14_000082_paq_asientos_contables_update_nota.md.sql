-- D6.7.3 AsientosContables.Update
-- El PATCH se orquesta en PaqAgent (AsientosContablesUpdateRunner):
-- load cabecera + rowVersion → validaciones → TX (replace renglones opcional + update cabecera) → reload Get.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_AsientosContables_Update cuando el alcance se estabilice.
SELECT CAST(N'D6.7.3 AsientosContables.Update = runner orquestado (ver AsientosContablesUpdateRunner.cs)' AS NVARCHAR(200)) AS nota;
