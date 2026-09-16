# Paso F1 — D6.7.1 AsientosContables.Get (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-07` |

## Evidencia Agente

- Op `AsientosContables.Get` en `AsientosContablesCatalog`
- SP `000080` `PAQ_AsientosContables_Get` (RS0 cabecera / RS1 renglones / RS2 auxiliares / RS3 subauxiliares)
- `AsientosContablesGatewayRunner`: params `nro_interno_analitico`+`_database`, `EncodeRowVersion` (base64 TIMESTAMP), árbol `tiposAuxiliar`, `NOT_FOUND`
- Whitelist + DI
- Tests: `AsientosContablesGatewayRunnerTests` (catálogo + missing params + SQL_NOT_CONFIGURED + NOT_FOUND + map+rowVersion)

## Evidencia Host

- `AsientosContablesService::show` dual-path
- Control: `AsientosContablesServiceGatewayGetControlTest`

## Pendiente lab

Deploy SP `000080` company + rebuild/restart PaqAgent + smoke GET asiento contable en modo agente.
