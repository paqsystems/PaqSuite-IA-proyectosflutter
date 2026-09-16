# Paso F1 — D6.11.1 PartesProduccion.PartesOperario.Entradas.List (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| SP | `2026_09_15_000123_paq_partes_produccion_partes_operario_entradas_list.sql` |
| Op | `PartesProduccion.PartesOperario.Entradas.List` |
| Shape | `ParteOperarioEntradasList` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildParteOperarioEntradasList` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_PartesOperarioEntradasList` (`@IdParteOperario`, `@UsuarioId`)
- RS0 meta `no_legajo` / `not_found`; RS1 `items[]`
- Dueño: resuelve legajo; parte de otro operario o inexistente → `NOT_FOUND` «Parte no encontrado o no editable»
- Sin tabla entradas → `items` vacío
- Item: `id`, `id_asignacion_item`, `id_orden_trabajo`, `codigo_ot`, `origen_carga`, `id_maquina`, `maquina_etiqueta`, `id_concepto_tiempo`, `concepto_nombre`, `es_productivo`, `minutos`, `fecha_hora_desde/hasta`, unidades, `notas`, `nro_orden_operacion` si la columna existe
- Tests envelope (UsuarioId obligatorio)

## Deploy

Deploy SP `000123` company + rebuild/restart PaqAgent + smoke GET entradas del parte.
Siguiente libre `000124` (Create nota).
