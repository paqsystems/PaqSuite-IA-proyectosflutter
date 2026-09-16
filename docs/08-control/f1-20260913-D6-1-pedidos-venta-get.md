# Paso F1 — D6.1 PedidosVenta.Get (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-13 |
| Kickoff | `Dominio-d6-01` |

## Evidencia Agente

- `PedidosVentaCatalog` + `PedidosVentaGatewayRunner`
- SP `2026_09_13_000049_paq_pedidos_venta_get.sql`
- Whitelist + DI
- Tests: `PedidosVentaGatewayRunnerTests`

## Evidencia Host Tango

- `PedidosVentaService::show` dual-path (`PedidosVenta.Get`)
- Control: `PedidosVentaServiceGatewayGetControlTest`

## Pendiente lab

Deploy SP `000049` + restart PaqAgent + smoke `GET /api/v1/.../pedidos-venta/{talon}/{nro}` en modo agente.
