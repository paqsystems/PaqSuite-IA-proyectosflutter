# Paso F1 — D6.10.11 PartesProduccion.PartesOperario.PartesRevisar.Get (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| SP | `2026_09_15_000122_paq_partes_produccion_partes_operario_partes_revisar_get.sql` |
| Op | `PartesProduccion.PartesOperario.PartesRevisar.Get` |
| Shape | `ParteOperarioPartesRevisarGet` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildParteOperarioPartesRevisarGet` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_PartesOperarioPartesRevisarGet` (RS0 cabecera, RS1 `entradas[]`)
- **Sin `usuario_id`.** Params `id_parte_operario`. No filtra dueño ni resuelve legajo del caller
- Cabecera: `id`, `id_operario`, `operario_nombre`, `fecha_parte`, `id_turno`, `turno_nombre`, `estado`, `observaciones`
- `entradas[]`: `id`, `id_concepto_tiempo`, `concepto_nombre`, `minutos`, `unidades_hechas`, `notas`, `notas_revision`
- Sin parte / sin tabla → `NOT_FOUND`. Sin tabla de entradas → `entradas` vacío
- Tests envelope (sin UsuarioId en SP)

## Deploy

Deploy SP `000122` company + rebuild/restart PaqAgent + smoke GET partes-a-revisar/{id}.
Siguiente libre `000123` (D6.11 entradas).
