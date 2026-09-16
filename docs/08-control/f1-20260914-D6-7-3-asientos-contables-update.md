# Paso F1 — D6.7.3 AsientosContables.Update (Agente + Host)

## Agente

- Op `AsientosContables.Update` en `AsientosContablesCatalog` (shape Update, SP `(orchestrated)`)
- Nota SQL `000082` (sin SP monolítico)
- `AsientosContablesUpdateRunner` (espejo PatchService PHP + rowVersion CONFLICT + Registrado bloqueado + replace renglones + reload Get)
- Tests: `AsientosContablesUpdateRunnerTests` (INVALID_PARAMETERS, renglones_json, SQL_NOT_CONFIGURED)

## Host (Tango)

- `AsientosContablesService::update` dual-path Update
- Control: `AsientosContablesServiceGatewayUpdateControlTest`

## Pendiente lab

Rebuild/restart PaqAgent + smoke PATCH asiento contable en modo agente (nota `000082` no requiere deploy SP).
