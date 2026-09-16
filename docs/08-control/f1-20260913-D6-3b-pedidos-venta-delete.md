# Paso F1 — D6.3b PedidosVenta.Delete (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` |

## Evidencia Agente

- Op `PedidosVenta.Delete` + `PedidosVentaDeleteRunner` (TX)
- Catálogo shape Delete + whitelist + DI
- Tests: `PedidosVentaDeleteRunnerTests`
- Nota SQL: `000052_…_nota.md.sql`

## Evidencia Host

- `PedidosVentaService::destroy` dual-path
- Control estructural: Get/Create/Update/Delete + `PRECONDICION_DELETE`

## Alcance MVP

Mirror de `PedidosVentaDeleteService` (precondiciones, `mantPed`, STA19, físico).
Path local sigue en `PedidosVentaDeleteService`.

## Pendiente lab

Rebuild/restart PaqAgent + smoke DELETE pedidos-venta en modo agente.
