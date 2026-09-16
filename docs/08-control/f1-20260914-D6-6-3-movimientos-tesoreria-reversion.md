# Paso F1 — D6.6.3 MovimientosTesoreria.Reversion (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-06` |

## Evidencia Agente

- Op `MovimientosTesoreria.Reversion` en `MovimientosTesoreriaCatalog` (shape Reversion, SP `(orchestrated)`)
- Nota SQL `000079` (sin SP monolítico)
- `MovimientosTesoreriaReversionRunner` (espejo ReversionService PHP; reusa helpers Create)
- Whitelist + DI (`Program.cs` / `AgentGatewayConnector`)
- Tests: `MovimientosTesoreriaReversionRunnerTests` (INVALID_PARAMETERS, SQL_NOT_CONFIGURED, EncodeRowVersion)

## Evidencia Host

- `MovimientosTesoreriaService::revertir` dual-path Reversion
- Control: `MovimientosTesoreriaServiceGatewayReversionControlTest`

## Pendiente lab

Rebuild/restart PaqAgent + smoke POST revertir movimiento en modo agente (nota `000079` no requiere deploy SP).
