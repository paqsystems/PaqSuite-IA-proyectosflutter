# Paso F1 — D6.5.1 PartesProduccion.Maquinas.Get (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-05` |

## Evidencia Agente

- Op `PartesProduccion.Maquinas.Get` en `PartesCatalog` (shape `MaquinaGet`)
- SP `000057` `PAQ_PartesProduccion_MaquinasGet`
- `PartesGatewayRunner`: valida `id_maquina`, mapea `{ id, codigo, nombre, activa }`, `NOT_FOUND`
- Tests: `PartesGatewayRunnerTests` (catálogo 3 ops + Get OK / missing id / NOT_FOUND)

## Evidencia Host

- `MaquinasService::show` dual-path
- Ruta `GET /api/v1/partes-produccion/maquinas/{id}`
- Control: `MaquinasServiceGatewayGetControlTest`

## Alcance MVP

Solo Get de máquina por id. Mutaciones siguen Eloquent local hasta D6.5.x.

## Pendiente lab

Deploy SP `000057` company + rebuild/restart PaqAgent + smoke GET maquinas en modo agente.
