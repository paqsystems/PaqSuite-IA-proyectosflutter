# Paso F1 — D6.11.5 PartesProduccion.PartesOperario.Entradas.Reclasificar (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000127_paq_partes_produccion_partes_operario_entradas_reclasificar_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Entradas.Reclasificar` |
| Shape | `ParteOperarioEntradasReclasificar` (orquestado) |

## Entregado

- Op en `PartesCatalog` (`(orchestrated)`) + `PartesEntradasReclasificarRunner`
- DI `Program.cs` + branch en `AgentGatewayConnector`
- Supervisor: **sin** filtro de dueño. Parte estado Reviewed + circuito de autorización activo
- `notas_revision` obligatorio. Circuito off → `CONFLICT`. Parte no Reviewed → `NOT_FOUND`
- Tests catalog + INVALID_PARAMETERS (`notas_revision`) + SQL_NOT_CONFIGURED
- Oleada D6.11 cerrada en Agente. Siguiente libre `000128`.

## Deploy

Rebuild/restart PaqAgent + smoke reclasificar entrada (supervisor).
