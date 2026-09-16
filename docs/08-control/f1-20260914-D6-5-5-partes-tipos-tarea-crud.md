# Paso F1 — D6.5.5–8 PartesProduccion.TiposTarea CRUD (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-05` |

## Evidencia Agente

- Ops `PartesProduccion.TiposTarea.{Get,Create,Update,Delete}`
- SP `000061`–`000064`
- Payload `{ id, codigo, nombre, activo }`; Delete `{ id, eliminado }`
- Tests PartesGatewayRunner: **23 OK** (catálogo 10 ops)

## Evidencia Host

- `TiposTareaService` dual-path CRUD
- Rutas + `GET show` nuevo
- Control: `TiposTareaServiceGatewayControlTest`

## Pendiente lab

Deploy SP `000061`–`000064` + rebuild PaqAgent + smoke tipos-tarea.
