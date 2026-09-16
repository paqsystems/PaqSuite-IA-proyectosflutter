# Paso F1 — D6.9.6 PartesProduccion.Asignaciones.Cerrar (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000101_paq_partes_produccion_asignaciones_cerrar_nota.md.sql` |
| Op | `PartesProduccion.Asignaciones.Cerrar` |
| Shape | `AsignacionCerrar` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `AsignacionesCerrarRunner`
- Solo Published; CONFLICT si no; `ESTADO=2` + `FECHA_CIERRE`
- Tests mínimos INVALID_PARAMETERS / SQL_NOT_CONFIGURED

## Deploy

Rebuild/restart PaqAgent + smoke POST cerrar.
