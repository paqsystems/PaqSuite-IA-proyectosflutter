# Paso F1 — D6.8.3 PartesProduccion.OrdenesTrabajo.Create (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000091_paq_partes_produccion_ordenes_trabajo_create_nota.md.sql` |
| Op | `PartesProduccion.OrdenesTrabajo.Create` |
| Runner | `OrdenesTrabajoCreateRunner` (orquestado; sin SP monolítico) |

## Entregado

- Nota SQL `000091` (documenta runner)
- `OrdenesTrabajoCreateRunner`: numeración auto / manual, validación artículo + ops activas + std vigente, TX 1..N filas, shape `formatItem`+`items`
- `PartesCatalog` shape `OrdenTrabajoCreate` + Connector/DI
- Tests mínimos Create + catálogo 30 ops

## Host

Tango `f-20260914-D6-8-3-partes-ordenes-trabajo-create.md` — `OrdenesTrabajoService::store` dual-path.
