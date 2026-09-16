# Paso F1 — D6.11.4 PartesProduccion.PartesOperario.Entradas.Delete (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000126_paq_partes_produccion_partes_operario_entradas_delete_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Entradas.Delete` |
| Shape | `ParteOperarioEntradasDelete` (orquestado) |

## Entregado

- Op en `PartesCatalog` (`(orchestrated)`) + `PartesEntradasDeleteRunner`
- DI `Program.cs` + branch en `AgentGatewayConnector`
- Parte Open + dueño. Entrada inexistente → `NOT_FOUND` «Entrada no encontrada»
- Tests catalog + INVALID_PARAMETERS + SQL_NOT_CONFIGURED

## Deploy

Rebuild/restart PaqAgent + smoke DELETE entrada. Siguiente libre `000127`.
