# Paso F1 — D6.8.6 PartesProduccion.OrdenesTrabajo.PatchEstado / CambioMasivoEstado (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Notas | `000094` PatchEstado · `000095` CambioMasivoEstado |
| Ops | `PartesProduccion.OrdenesTrabajo.PatchEstado` · `PartesProduccion.OrdenesTrabajo.CambioMasivoEstado` |
| Runners | `OrdenesTrabajoPatchEstadoRunner` · `OrdenesTrabajoCambioMasivoEstadoRunner` (orquestados; sin SP monolítico) |

## Entregado

- Notas SQL `000094` / `000095`
- PatchEstado: transición 0|1|2 (Borrador con params + sin asignaciones); success formatItem
- CambioMasivoEstado: `cerrar` (1→2) / `reabrir` (2→1); success `{actualizadas}`
- `PartesCatalog` shapes + Connector/DI
- Tests mínimos estado

## Host

Tango `f-20260914-D6-8-6-partes-ordenes-trabajo-estado.md` — dual-path `patchEstado` / `cambioMasivoEstado`.
