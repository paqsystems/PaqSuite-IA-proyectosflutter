# Paso F1 — D6.5.4 PartesProduccion.Maquinas.Delete (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-05` |

## Evidencia Agente

- Op `PartesProduccion.Maquinas.Delete` (shape `MaquinaDelete`)
- SP `000060` `PAQ_PartesProduccion_MaquinasDelete`
- Borrado físico; precheck `ASIGNACIONES_ITEMS` / `PARTES_ENTRADAS` + CATCH 547 → `REFERENCED`
- Tests: Delete OK / REFERENCED / missing id

## Evidencia Host

- `MaquinasService::destroy` dual-path
- `DELETE /api/v1/partes-produccion/maquinas/{id}` (409 si refs)
- Control: CRUD completo + `destroyLocal` solo si `!shouldUseGateway`

## Nota producto

HU/TR hablan de baja lógica (`activa=0`); el API HTTP vigente hace borrado físico. Este slice espeja el API actual. Baja lógica = usar Update `activa=false`.

## Pendiente lab

Deploy SP `000060` + rebuild PaqAgent + smoke DELETE maquinas.
