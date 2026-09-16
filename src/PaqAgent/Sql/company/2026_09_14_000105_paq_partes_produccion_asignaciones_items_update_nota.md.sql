-- D6.9.10 PartesProduccion.Asignaciones.Items.Update
-- Update orquestado en PaqAgent (AsignacionesItemsUpdateRunner):
--   TX: Draft only, OT Abierta, plan OT+STD, UPDATE item.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_PartesProduccion_AsignacionesItemsUpdate ...
SELECT CAST(N'D6.9.10 Asignaciones.Items.Update = runner orquestado (ver AsignacionesItemsUpdateRunner.cs)' AS NVARCHAR(200)) AS nota;
