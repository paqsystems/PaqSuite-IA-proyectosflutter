# Paso F1 — D6.7.4 AsientosContables.Delete (Agente + Host)

## Agente

- Op `AsientosContables.Delete` en `AsientosContablesCatalog` (shape Delete, SP `(orchestrated)`)
- Nota SQL `000083` (sin SP monolítico)
- `AsientosContablesDeleteRunner` (espejo DeleteService PHP + rowVersion CONFLICT + Registrado bloqueado + deleteHijos)
- Success: `{ nroInternoAnalitico, eliminado: true }`
- Tests: `AsientosContablesDeleteRunnerTests` (INVALID_PARAMETERS, SQL_NOT_CONFIGURED, RowVersionMatches)

## Host (Tango)

- `AsientosContablesService::destroy` dual-path Delete
- Control: `AsientosContablesServiceGatewayDeleteControlTest`

## Pendiente lab

Rebuild/restart PaqAgent + smoke DELETE asiento contable en modo agente (nota `000083` no requiere deploy SP).
