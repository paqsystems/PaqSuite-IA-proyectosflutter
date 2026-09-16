# Paso F1 — D6.3 PedidosVenta.Update (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` |

## Evidencia Agente

- Op `PedidosVenta.Update` + `PedidosVentaUpdateRunner` (TX patch)
- Catálogo shape Update + whitelist + DI
- Tests: `PedidosVentaUpdateRunnerTests`
- Nota SQL: `000051_…_nota.md.sql`

## Evidencia Host

- `PedidosVentaService::update` dual-path
- Control estructural: Get/Create/Update + `ROW_VERSION_CONFLICT`

## Alcance MVP

Mirror de `PedidosVentaPatchService` (estados 1/2/6, cabecera parcial, cantPedid + STA19).
Sin Delta6 `reemplazar`. Path local sigue en `PedidosVentaPatchService`.

## Pendiente lab

Rebuild/restart PaqAgent + smoke PATCH pedidos-venta en modo agente.
