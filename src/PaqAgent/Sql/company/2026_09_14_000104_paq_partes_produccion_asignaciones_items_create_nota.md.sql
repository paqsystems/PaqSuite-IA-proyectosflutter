-- D6.9.9 PartesProduccion.Asignaciones.Items.Create
-- Alta orquestada en PaqAgent (AsignacionesItemsCreateRunner):
--   TX: Draft only, OT Abierta, plan OT+STD, INSERT item.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesItemsCreate ...
SELECT CAST(N'D6.9.9 Asignaciones.Items.Create = runner orquestado (ver AsignacionesItemsCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
