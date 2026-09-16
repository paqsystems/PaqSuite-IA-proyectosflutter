# Paso F1 — D6.8.1 PartesProduccion.OrdenesTrabajo.List (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| SP | `2026_09_14_000089_paq_partes_produccion_ordenes_trabajo_list.sql` |
| Op | `PartesProduccion.OrdenesTrabajo.List` |
| Shape | `OrdenTrabajoList` |

## Entregado

- Op en `PartesCatalog` + mapper en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_OrdenesTrabajoList` (agrupado / por fila, paginación, filtros, lookup artículos)
- Tests params + empty envelope

## Deploy

Deploy SP `000089` company + rebuild/restart PaqAgent + smoke GET listado OT (agrupado y `agrupado=0`).
