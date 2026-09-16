# Paso F1 — D6.5.2 PartesProduccion.Maquinas.Create (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-05` |

## Evidencia Agente

- Op `PartesProduccion.Maquinas.Create` en `PartesCatalog` (shape `MaquinaCreate`)
- SP `000058` `PAQ_PartesProduccion_MaquinasCreate` (`resultCode` OK / DUPLICATE_CODE)
- `PartesGatewayRunner`: trim/validación codigo/nombre, default `activa=true`
- Tests: Create OK / missing codigo / DUPLICATE_CODE

## Evidencia Host

- `MaquinasService::store` dual-path
- `POST /api/v1/partes-produccion/maquinas` vía service
- Control: Get + Create + `DUPLICATE_CODE` + `storeLocal` solo si `!shouldUseGateway`

## Alcance MVP

Alta máquina (código único, nombre, activa). Update/Delete siguen Eloquent local.

## Pendiente lab

Deploy SP `000058` company + rebuild/restart PaqAgent + smoke POST maquinas en modo agente.
