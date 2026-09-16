# Paso F1 — D6.10.8 PartesProduccion.PartesOperario.ParteContexto (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| SP | `2026_09_15_000119_paq_partes_produccion_partes_operario_parte_contexto.sql` |
| Op | `PartesProduccion.PartesOperario.ParteContexto` |
| Shape | `ParteOperarioParteContexto` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildParteOperarioParteContexto` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_PartesOperarioParteContexto` (`@FechaParte`, `@IdTurno`, `@UsuarioId`)
- Resuelve operario en `PQ_SUELD_LEGAJOS.ID_USUARIO`
- Vacío / sin tabla / sin legajo → success `id_parte_operario: null`
- Sin `id_turno`: match `ID_TURNO` null o 0; con turno: match exacto; si hay varios, mayor `ID_PARTE_OPERARIO`
- Tests params + payload + null + INVALID_PARAMETERS

## Deploy

Deploy SP `000119` company + rebuild/restart PaqAgent + smoke GET mis-partes/parte-contexto.
