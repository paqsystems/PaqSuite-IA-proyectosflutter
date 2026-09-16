# Paso F1 — D6.10.2 PartesProduccion.PartesOperario.Create (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000113_paq_partes_produccion_partes_operario_create_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Create` |
| Shape | `ParteOperarioCreate` (orquestado) |

## Entregado

- Op en `PartesCatalog` (`(orchestrated)`) + `PartesOperarioCreateRunner`
- DI `Program.cs` + branch en `AgentGatewayConnector`
- Resuelve operario en `PQ_SUELD_LEGAJOS`; `NO_LEGAJO` si no hay
- Valida turno si viene; unicidad `(FECHA_PARTE, ID_TURNO, ID_OPERARIO)` → `CONFLICT` «Ya existe un parte para esa fecha y turno.»
- INSERT `ESTADO=0` + reload `id` / `fecha_parte` / `id_turno` / `estado`
- Sin check de fecha futura (mismo alcance host)
- Tests catalog + INVALID_PARAMETERS + VALIDATION fecha + SQL_NOT_CONFIGURED

## Deploy

Rebuild/restart PaqAgent + smoke POST alta parte operario. Siguiente libre `000114`.
