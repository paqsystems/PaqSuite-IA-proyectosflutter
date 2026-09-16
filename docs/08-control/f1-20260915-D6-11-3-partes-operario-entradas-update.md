# Paso F1 — D6.11.3 PartesProduccion.PartesOperario.Entradas.Update (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000125_paq_partes_produccion_partes_operario_entradas_update_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Entradas.Update` |
| Shape | `ParteOperarioEntradasUpdate` (orquestado) |

## Entregado

- Op en `PartesCatalog` (`(orchestrated)`) + `PartesEntradasUpdateRunner`
- DI `Program.cs` + branch en `AgentGatewayConnector`
- Parte Open + dueño. **No** toca `ID_ASIGNACION_ITEM` / `ORIGEN_CARGA`
- Patch de concepto, minutos, intervalo, unidades, notas, máquina
- Tests catalog + INVALID_PARAMETERS + SQL_NOT_CONFIGURED

## Deploy

Rebuild/restart PaqAgent + smoke PATCH entrada. Siguiente libre `000126`.
