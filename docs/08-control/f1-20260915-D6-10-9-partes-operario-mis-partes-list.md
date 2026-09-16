# Paso F1 — D6.10.9 PartesProduccion.PartesOperario.MisPartes.List (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| SP | `2026_09_15_000120_paq_partes_produccion_partes_operario_mis_partes_list.sql` |
| Op | `PartesProduccion.PartesOperario.MisPartes.List` |
| Shape | `ParteOperarioMisPartesList` |

## Entregado

- Op en `PartesCatalog` + mapper `BuildParteOperarioMisPartesList` en `PartesGatewayRunner`
- SP `PAQ_PartesProduccion_PartesOperarioMisPartesList` (RS0 meta+verificación, RS1 cabeceras, RS2 planificado)
- Params `usuario_id` / filtros fecha-turno-estado / paginación cabecera y planificado
- `supervisor_*` labels quedan null (el host enriquece desde dictionary)
- Vacío / sin tabla / sin legajo → envelope `page_size` 0
- Tests envelope + INVALID_PARAMETERS

## Deploy

Deploy SP `000120` company + rebuild/restart PaqAgent + smoke GET mis-partes.
