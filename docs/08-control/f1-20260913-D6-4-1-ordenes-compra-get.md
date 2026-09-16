# Paso F1 — D6.4.1 OrdenesCompra.Get (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` |

## Evidencia Agente

- Op `OrdenesCompra.Get` + SP `PAQ_OrdenesCompra_Get` (`000053`)
- `OrdenesCompraCatalog` + `OrdenesCompraGatewayRunner` (4 RS)
- Whitelist + DI
- Tests: `OrdenesCompraGatewayRunnerTests`

## Evidencia Host

- `OrdenesCompraService::show` dual-path
- Control: `OrdenesCompraServiceGatewayGetControlTest`

## Pendiente lab

Deploy SP `000053` + rebuild/restart PaqAgent + smoke GET ordenes-compra en modo agente.
