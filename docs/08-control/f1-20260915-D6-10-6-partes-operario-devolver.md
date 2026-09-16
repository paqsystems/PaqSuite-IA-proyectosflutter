# Paso F1 — D6.10.6 PartesProduccion.PartesOperario.Devolver (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000117_paq_partes_produccion_partes_operario_devolver_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Devolver` |
| Shape | `ParteOperarioDevolver` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `PartesOperarioDevolverRunner`
- DI + branch Connector
- Circuito inactivo → `CONFLICT`
- Supervisor: sin filtro de propietario
- Solo Submitted → Open (limpia `FECHA_REVISION` / `ID_USUARIO_REVISION`)
- Success: `id` + `estado` (Open = 0)

## Deploy

Rebuild/restart PaqAgent + smoke POST devolver parte operario Submitted.
