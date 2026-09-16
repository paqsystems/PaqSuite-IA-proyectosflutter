# Paso F1 — D6.10.1 PartesProduccion.PartesOperario.Get (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| SP | `2026_09_15_000112_paq_partes_produccion_partes_operario_get.sql` |
| Op | `PartesProduccion.PartesOperario.Get` |
| Shape | `ParteOperarioGet` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildParteOperarioGet` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_PartesOperarioGet` (`@IdParteOperario`, `@UsuarioId`)
- Resuelve operario en `PQ_SUELD_LEGAJOS.ID_USUARIO`; filtra dueño
- Meta RS0: `no_legajo` / `not_found` → `NO_LEGAJO` / `NOT_FOUND`
- Success snake_case espejo show host: `id`, `fecha_parte`, `id_turno`, `turno_nombre`, `estado`, `observaciones`
- Tests params + payload + NO_LEGAJO + NOT_FOUND + INVALID_PARAMETERS

## Deploy

Deploy SP `000112` company + rebuild/restart PaqAgent + smoke GET parte operario por id.
