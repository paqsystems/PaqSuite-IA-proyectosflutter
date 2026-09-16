# Paso F1 — D6.8.4 PartesProduccion.OrdenesTrabajo.Update (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000092_paq_partes_produccion_ordenes_trabajo_update_nota.md.sql` |
| Op | `PartesProduccion.OrdenesTrabajo.Update` |
| Runner | `OrdenesTrabajoUpdateRunner` (orquestado; sin SP monolítico) |

## Entregado

- Nota SQL `000092` (documenta runner)
- `OrdenesTrabajoUpdateRunner`: modos por `id_orden_trabajo` y por `codigo_ot` (sync multi-op); helpers std/ops/artículo; sin numeración
- `PartesCatalog` shape `OrdenTrabajoUpdate` + Connector/DI
- Tests mínimos Update + catálogo 32 ops

## Host

Tango `f-20260914-D6-8-4-partes-ordenes-trabajo-update.md` — `OrdenesTrabajoService::update` / `updateByCodigo` dual-path.
