# Paso F1 — D6.9.3 PartesProduccion.Asignaciones.Create (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000098_paq_partes_produccion_asignaciones_create_nota.md.sql` |
| Op | `PartesProduccion.Asignaciones.Create` |
| Shape | `AsignacionCreate` (orquestado) |

## Entregado

- Op en `PartesCatalog` (`(orchestrated)`) + `AsignacionesCreateRunner`
- DI `Program.cs` + branch en `AgentGatewayConnector`
- Validaciones: fecha ≥ hoy, `id_tipo_tarea`, turno/tipo existen si tablas; observaciones max 2000
- INSERT Draft (`ESTADO=0`) + reload cabecera **sin** observaciones (shape store host)
- Tests catalog + INVALID_PARAMETERS + SQL_NOT_CONFIGURED + fecha pasada
- Supervisor labels: null en agente

## Deploy

Rebuild/restart PaqAgent + smoke POST alta asignación Draft. Siguiente libre `000099`.
