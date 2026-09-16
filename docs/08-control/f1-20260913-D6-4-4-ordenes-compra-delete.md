# Paso F1 — D6.4.4 OrdenesCompra.Delete (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` / `Dominio-d6-04` |

## Evidencia Agente

- Op `OrdenesCompra.Delete` + `OrdenesCompraDeleteRunner` (anulación TX)
- Catálogo shape Delete + whitelist + DI
- Tests: `OrdenesCompraDeleteRunnerTests`
- Nota SQL: `000056_…_nota.md.sql`

## Evidencia Host

- `OrdenesCompraService::destroy` dual-path
- Control estructural: Get/Create/Update/Delete + `PRECONDICION_DELETE`

## Alcance MVP

Mirror de `OrdenesCompraDeleteService` (estados 1/2/3 → estado+4, STA19, audit).
Sin borrado físico.

## Pendiente lab

Rebuild/restart PaqAgent + smoke DELETE ordenes-compra en modo agente.
