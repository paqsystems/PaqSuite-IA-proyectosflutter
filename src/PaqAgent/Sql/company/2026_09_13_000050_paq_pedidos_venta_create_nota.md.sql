-- D6.2 PedidosVenta.Create
-- El alta MVP se orquesta en PaqAgent (PedidosVentaCreateRunner):
--   crypto GVA43.PROXIMO + TX INSERT GVA21/GVA03 + STA19.
-- Sin Delta6 / sin cliente ocasional 000000.
-- Futuro: consolidar a dbo.PAQ_PedidosVenta_Create (MUST SP host) cuando el alcance se estabilice.
SELECT CAST(N'D6.2 PedidosVenta.Create = runner orquestado (ver PedidosVentaCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
