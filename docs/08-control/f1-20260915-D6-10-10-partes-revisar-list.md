# Paso F1 — D6.10.10 PartesProduccion.PartesOperario.PartesRevisar.List (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| SP | `2026_09_15_000121_paq_partes_produccion_partes_operario_partes_revisar_list.sql` |
| Op | `PartesProduccion.PartesOperario.PartesRevisar.List` |
| Shape | `ParteOperarioPartesRevisarList` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildParteOperarioPartesRevisarList` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_PartesOperarioPartesRevisarList` (RS0 meta, RS1 items)
- **Sin `usuario_id`.** Filtros fecha-turno-operario-estado / paginación max 100
- Sin `filter_estado`: Open + Submitted + Reviewed. `fecha_envio` = `FECHA_MODIF` solo si Submitted
- Sin tabla → 200 con `items` vacío (`page_size` 50)
- Tests envelope (sin UsuarioId en SP)

## Deploy

Deploy SP `000121` company + rebuild/restart PaqAgent + smoke GET partes-a-revisar.
Siguiente libre `000122` (PartesRevisar.Get).
