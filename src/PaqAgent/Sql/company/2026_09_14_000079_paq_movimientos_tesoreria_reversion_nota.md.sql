-- D6.6.3 MovimientosTesoreria.Reversion
-- La reversión se orquesta en PaqAgent (MovimientosTesoreriaReversionRunner):
--   TX company: load SBA04 + rowVersion + SBA05 invertidos + SBA01 saldos + SBA27 + SITUACION=A + asiento opcional.
-- Sin SP monolítico. Futuro: consolidar a dbo.PAQ_MovimientosTesoreria_Reversion cuando el alcance se estabilice.
SELECT CAST(N'D6.6.3 MovimientosTesoreria.Reversion = runner orquestado (ver MovimientosTesoreriaReversionRunner.cs)' AS NVARCHAR(200)) AS nota;
