# Paso F1 — D6.8.2 PartesProduccion.OrdenesTrabajo.Get (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| SP | `2026_09_14_000090_paq_partes_produccion_ordenes_trabajo_get.sql` |
| Op | `PartesProduccion.OrdenesTrabajo.Get` |
| Host F | Tango `f-20260914-D6-8-2-partes-ordenes-trabajo-get.md` |

## Hecho

- SP `PAQ_PartesProduccion_OrdenesTrabajoGet` (`@IdOrdenTrabajo` o `@CodigoOt`; join operación + artículo)
- Catalog + Runner shape `OrdenTrabajoGet` (by id = detail; by codigo = lote)
- Tests unitarios Get (by id, by codigo, INVALID_PARAMETERS, NOT_FOUND)

## Pendiente lab

Deploy SP `000090` company + rebuild/restart PaqAgent + smoke GET por id y por codigo.
