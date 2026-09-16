# Paso F1 — D6.9.7 PartesProduccion.Asignaciones.Cancelar (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000102_paq_partes_produccion_asignaciones_cancelar_nota.md.sql` |
| Op | `PartesProduccion.Asignaciones.Cancelar` |
| Shape | `AsignacionCancelar` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `AsignacionesCancelarRunner`
- Draft/Published → Cancelled; CONFLICT si ya cerrada/anulada
- Tests mínimos INVALID_PARAMETERS / SQL_NOT_CONFIGURED
- Siguiente libre: `000103` (Items)

## Deploy

Rebuild/restart PaqAgent + smoke POST cancelar.
