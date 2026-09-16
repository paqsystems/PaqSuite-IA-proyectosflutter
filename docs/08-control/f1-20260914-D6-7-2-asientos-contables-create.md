# Paso F1 — D6.7.2 AsientosContables.Create (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-07` |

## Evidencia Agente

- Op `AsientosContables.Create` en `AsientosContablesCatalog` (shape Create, SP `(orchestrated)`)
- Nota SQL `000081` (sin SP monolítico)
- `AsientosContablesCreateRunner` (espejo AltaService PHP + numeración/referencias/partida doble/SinAsignar)
- Reload post-commit vía `AsientosContables.Get` + `creado=true` (`BuildDetalle`)
- Whitelist + DI (`Program.cs` / `AgentGatewayConnector`)
- Tests: `AsientosContablesCreateRunnerTests` (INVALID_PARAMETERS, parsing, partida, estado resumen)

## Evidencia Host

- `AsientosContablesService::store` dual-path Create
- Control: `AsientosContablesServiceGatewayCreateControlTest`

## Pendiente lab

Rebuild/restart PaqAgent + smoke POST asiento contable en modo agente (nota `000081` no requiere deploy SP).
