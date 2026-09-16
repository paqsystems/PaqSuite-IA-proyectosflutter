# Paso F1 — D6.5.3 PartesProduccion.Maquinas.Update (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | Tango `Dominio-d6-01` / `Dominio-d6-05` |

## Evidencia Agente

- Op `PartesProduccion.Maquinas.Update` (shape `MaquinaUpdate`)
- SP `000059` `PAQ_PartesProduccion_MaquinasUpdate`
- Patch: `nombre?` y/o `activa?`; código no editable; `NOT_FOUND`
- Tests: Update OK / sin campos / NOT_FOUND

## Evidencia Host

- `MaquinasService::update` dual-path
- `PUT /api/v1/partes-produccion/maquinas/{id}`
- Control: Get + Create + Update + `updateLocal` solo si `!shouldUseGateway`

## Pendiente lab

Deploy SP `000059` + rebuild PaqAgent + smoke PUT maquinas.
