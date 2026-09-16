# Paso F1 — D6.9.2 PartesProduccion.Asignaciones.Get (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| SP | `2026_09_14_000097_paq_partes_produccion_asignaciones_get.sql` |
| Op | `PartesProduccion.Asignaciones.Get` |
| Shape | `AsignacionGet` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildAsignacionGet` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_AsignacionesGet` (`@IdAsignacion`) — cabecera + observaciones; join turno/tipo_tarea
- Empty → NOT_FOUND
- Tests params + payload + NOT_FOUND
- Supervisor labels: null en agente (USERS es host)

## Deploy

Deploy SP `000097` company + rebuild/restart PaqAgent + smoke GET asignación por id.
