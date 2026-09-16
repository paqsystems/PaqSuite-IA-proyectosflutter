# Paso F1 — D6.9.1 PartesProduccion.Asignaciones.List (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| SP | `2026_09_14_000096_paq_partes_produccion_asignaciones_list.sql` |
| Op | `PartesProduccion.Asignaciones.List` |
| Shape | `AsignacionList` |

## Entregado

- Op en `PartesCatalog` + mapper en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_AsignacionesList` (paginación, filtros, join turno/tipo_tarea)
- VALIDATION si `filter_fecha_desde` > `filter_fecha_hasta`
- Tests params + empty envelope + validación fechas
- Supervisor labels: null en agente (USERS es host)

## Deploy

Deploy SP `000096` company + rebuild/restart PaqAgent + smoke GET listado asignaciones.
