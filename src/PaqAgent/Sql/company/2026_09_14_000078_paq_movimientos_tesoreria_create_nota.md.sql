-- D6.6.2 MovimientosTesoreria.Create
-- El alta se orquesta en PaqAgent (MovimientosTesoreriaCreateRunner):
--   TX company: SBA02 tipo + SBA04/SBA05 + saldos SBA01 + asiento opcional + cotización.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_MovimientosTesoreria_Create cuando el alcance se estabilice.
SELECT CAST(N'D6.6.2 MovimientosTesoreria.Create = runner orquestado (ver MovimientosTesoreriaCreateRunner.cs)' AS NVARCHAR(200)) AS nota;
