# Paso F1 — D6.11.2 PartesProduccion.PartesOperario.Entradas.Create (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000124_paq_partes_produccion_partes_operario_entradas_create_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Entradas.Create` |
| Shape | `ParteOperarioEntradasCreate` (orquestado) |

## Entregado

- Op en `PartesCatalog` (`(orchestrated)`) + `PartesEntradasCreateRunner`
- DI `Program.cs` + branch en `AgentGatewayConnector`
- Parte Open + dueño (`usuario_id`); si no → `NOT_FOUND`
- Espejo PHP `storeLocal`: OT o ítem de asignación; minutos o intervalo; máquina si existe catálogo; split no productivo opcional
- Tests catalog + INVALID_PARAMETERS + SQL_NOT_CONFIGURED

## Deploy

Rebuild/restart PaqAgent + smoke POST alta entrada. Siguiente libre `000125`.
