# Paso F1 — D6.9.4 PartesProduccion.Asignaciones.Update (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000099_paq_partes_produccion_asignaciones_update_nota.md.sql` |
| Op | `PartesProduccion.Asignaciones.Update` |
| Shape | `AsignacionUpdate` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `AsignacionesUpdateRunner`
- DI + branch Connector
- Solo Draft; CONFLICT si no; bloqueo cambio tipo_tarea con items
- Reload cabecera **con** observaciones
- Tests mínimos (INVALID_PARAMETERS / VALIDATION fecha / SQL_NOT_CONFIGURED)

## Deploy

Rebuild/restart PaqAgent + smoke PUT asignación Draft.
