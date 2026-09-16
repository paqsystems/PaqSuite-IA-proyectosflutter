# Paso F1 — D6.2 PedidosVenta.Create (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` |

## Evidencia Agente

- Op `PedidosVenta.Create` + `PedidosVentaCreateRunner` (TX)
- `EncriptacionTalonarios` (round-trip tests)
- Whitelist + DI
- Tests: `PedidosVentaCreateRunnerTests`

## Evidencia Host

- `PedidosVentaService::store` dual-path
- Control estructural actualizado

## Alcance MVP

Sin Delta6, sin ocasional `000000`. Path local sigue en `PedidosVentaAltaService`.

## Pendiente lab

Rebuild/restart PaqAgent + smoke PUT pedidos-venta en modo agente.
