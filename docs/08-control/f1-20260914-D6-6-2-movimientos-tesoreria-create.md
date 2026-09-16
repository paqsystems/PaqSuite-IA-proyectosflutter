# Paso F1 — D6.6.2 MovimientosTesoreria.Create (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-06` |

## Evidencia Agente

- Op `MovimientosTesoreria.Create` en `MovimientosTesoreriaCatalog` (shape Create, SP `(orchestrated)`)
- Nota SQL `000078` (sin SP monolítico)
- `MovimientosTesoreriaCreateRunner` + `MovimientosTesoreriaMatrizClases` (espejo AltaService PHP)
- Whitelist + DI (`Program.cs` / `AgentGatewayConnector`)
- Tests: `MovimientosTesoreriaCreateRunnerTests` (INVALID_PARAMETERS, parsing, partida, matriz, nComp)

## Evidencia Host

- `MovimientosTesoreriaService::store` / `storeInterno` dual-path Create
- Control: `MovimientosTesoreriaServiceGatewayCreateControlTest`

## Pendiente lab

Rebuild/restart PaqAgent + smoke POST movimiento tesorería en modo agente (nota `000078` no requiere deploy SP).
