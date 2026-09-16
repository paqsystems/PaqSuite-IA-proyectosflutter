# Paso F1 — D6.9.5 PartesProduccion.Asignaciones.Publicar (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Nota | `2026_09_14_000100_paq_partes_produccion_asignaciones_publicar_nota.md.sql` |
| Op | `PartesProduccion.Asignaciones.Publicar` |
| Shape | `AsignacionPublicar` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `AsignacionesPublicarRunner` (alta complejidad)
- Plan mínimo: ≥1 item y cada item ≥1 operario
- Congela `UNIDADES_HORA_STD` desde `PQ_PRD_ARTICULO_OPERACION_STD` vigente
- Draft→Published (`ESTADO=1` + `FECHA_PUBLICACION`)
- Tests mínimos INVALID_PARAMETERS / SQL_NOT_CONFIGURED

## Deploy

Rebuild/restart PaqAgent + smoke POST publicar.
