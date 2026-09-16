# Paso F1 — D6.8.5 PartesProduccion.OrdenesTrabajo.Delete (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000093_paq_partes_produccion_ordenes_trabajo_delete_nota.md.sql` |
| Op | `PartesProduccion.OrdenesTrabajo.Delete` |
| Runner | `OrdenesTrabajoDeleteRunner` (orquestado; sin SP monolítico) |

## Entregado

- Nota SQL `000093` (documenta runner)
- `OrdenesTrabajoDeleteRunner`: baja por `id_orden_trabajo`; estado editable; vínculos asignaciones/partes; `usuario_codigo` EMP → FORBIDDEN
- `PartesCatalog` shape `OrdenTrabajoDelete` + Connector/DI
- Tests mínimos Delete

## Host

Tango `f-20260914-D6-8-5-partes-ordenes-trabajo-delete.md` — `OrdenesTrabajoService::destroy` dual-path.
