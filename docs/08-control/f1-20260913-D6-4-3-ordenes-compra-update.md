# Paso F1 — D6.4.3 OrdenesCompra.Update (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` / `Dominio-d6-04` |

## Evidencia Agente

- Op `OrdenesCompra.Update` + `OrdenesCompraUpdateRunner` (TX patch → Get)
- Catálogo shape Update + whitelist + DI
- Tests: `OrdenesCompraUpdateRunnerTests`
- Nota SQL: `000055_…_nota.md.sql`

## Evidencia Host

- `OrdenesCompraService::update` dual-path
- Control estructural: Get/Create/Update + `ROW_VERSION_CONFLICT`

## Alcance MVP

Mirror de `OrdenesCompraPatchService` (MODI_OC, estado&lt;4, solo-precios estado 3, cant/precio + STA19).
Sin cambio de `nOrdenCo`, sin add/delete renglones, sin planes.

## Pendiente lab

Rebuild/restart PaqAgent + smoke PATCH ordenes-compra en modo agente.
