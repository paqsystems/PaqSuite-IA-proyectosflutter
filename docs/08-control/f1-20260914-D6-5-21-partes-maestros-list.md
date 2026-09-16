# Paso F1 — D6.5.21–25 Partes maestros List (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-08` |

## Evidencia Agente

- Ops `PartesProduccion.{Maquinas,TiposTarea,Operaciones,Turnos,ConceptosTiempo}.List` en `PartesCatalog`
- SP `000084`–`000088` (`PAQ_PartesProduccion_*List`)
- `PartesGatewayRunner`: shapes `*List`, envelope `{items,page,page_size,total,total_pages}`
- Tests: `PartesGatewayRunnerTests` (catálogo 27 ops + List params / empty)

## Evidencia Host

- `*Service::index` dual-path (5 familias)
- Controllers `index` delegan al service
- Control: `*ServiceGatewayListControlTest` (5)

## Pendiente lab

Deploy SP `000084`–`000088` company + rebuild/restart PaqAgent + smoke GET listados en modo agente.
