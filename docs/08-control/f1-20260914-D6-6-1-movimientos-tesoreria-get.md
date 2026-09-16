# Paso F1 — D6.6.1 MovimientosTesoreria.Get (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-06` |

## Evidencia Agente

- Op `MovimientosTesoreria.Get` en `MovimientosTesoreriaCatalog`
- SP `000077` `PAQ_MovimientosTesoreria_Get` (RS0 SBA04 / RS1 SBA05 / RS2 SBA27)
- `MovimientosTesoreriaGatewayRunner`: params `cod_comp`+`n_comp`+`barra`+`_database`, `EncodeRowVersion`, `NOT_FOUND`
- Whitelist + DI
- Tests: `MovimientosTesoreriaGatewayRunnerTests` (catálogo + Get OK / missing params / NOT_FOUND)

## Evidencia Host

- `MovimientosTesoreriaService::show` dual-path
- Control: `MovimientosTesoreriaServiceGatewayGetControlTest`

## Pendiente lab

Deploy SP `000077` company + rebuild/restart PaqAgent + smoke GET movimiento tesorería en modo agente.
