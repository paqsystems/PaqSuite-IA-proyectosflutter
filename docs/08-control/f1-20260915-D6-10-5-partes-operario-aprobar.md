# Paso F1 — D6.10.5 PartesProduccion.PartesOperario.Aprobar (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000116_paq_partes_produccion_partes_operario_aprobar_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Aprobar` |
| Shape | `ParteOperarioAprobar` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `PartesOperarioAprobarRunner`
- DI + branch Connector
- Circuito inactivo → `CONFLICT` («El circuito de autorización está desactivado.»)
- Supervisor: sin filtro de propietario
- Solo Submitted → Reviewed (`FECHA_REVISION`, `ID_USUARIO_REVISION`)
- Success: `id` + `estado` (Reviewed = 2)

## Deploy

Rebuild/restart PaqAgent + smoke POST aprobar parte operario Submitted.
