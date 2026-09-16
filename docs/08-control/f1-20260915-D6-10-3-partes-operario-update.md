# Paso F1 — D6.10.3 PartesProduccion.PartesOperario.Update (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-15 |
| Nota | `2026_09_15_000114_paq_partes_produccion_partes_operario_update_nota.md.sql` |
| Op | `PartesProduccion.PartesOperario.Update` |
| Shape | `ParteOperarioUpdate` (orquestado) |

## Entregado

- Op en `PartesCatalog` + `PartesOperarioUpdateRunner`
- DI + branch Connector
- Solo propietario + estado Open; `CONFLICT` si no Open («Solo se puede editar un parte en estado Abierto.»)
- `NOT_FOUND` si no existe o no es dueño (sin 403 distinto)
- `NO_LEGAJO` si el usuario no tiene legajo
- Observaciones max 2000; reload `id` + `observaciones`
- Tests mínimos (INVALID_PARAMETERS / VALIDATION largo / SQL_NOT_CONFIGURED)

## Deploy

Rebuild/restart PaqAgent + smoke PUT parte operario Open. Siguiente libre `000115` (Enviar).
