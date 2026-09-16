# Paso F1 — D6.10.4 PartesProduccion.PartesOperario.Enviar (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000115_paq_partes_produccion_partes_operario_enviar_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Enviar` |
| Shape | `ParteOperarioEnviar` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `PartesOperarioEnviarRunner`
- DI + branch Connector
- Circuito inactivo → `CONFLICT` («El circuito de autorización está desactivado. Los partes permanecen en estado Abierto.»)
- Solo propietario + estado Open; si no Open → `CONFLICT`
- Entradas: al menos una con concepto y minutos ≥ 1; `origen_carga=libre` exige OT
- Success: `id` + `estado` (Submitted = 1)

## Deploy

Rebuild/restart PaqAgent + smoke POST enviar parte operario Open.
