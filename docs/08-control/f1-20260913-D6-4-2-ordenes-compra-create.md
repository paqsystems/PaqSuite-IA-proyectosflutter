# Paso F1 — D6.4.2 OrdenesCompra.Create (Agente + Host)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |
| Kickoff | `Dominio-d6-01` / `Dominio-d6-04` |

## Evidencia Agente

- Op `OrdenesCompra.Create` + `OrdenesCompraCreateRunner` (TX)
- Correlativo CPA56 + `EncriptacionTalonarios`
- Whitelist + DI
- Tests: `OrdenesCompraCreateRunnerTests`
- Nota SQL: `000054_…_nota.md.sql`

## Evidencia Host

- `OrdenesCompraService::store` dual-path
- Control estructural actualizado (Get + Create)

## Alcance MVP

Sin proveedor ocasional, sin CPA44 precio lista, sin renglones solo-texto, sin CPA104.
Path local sigue en `OrdenesCompraAltaService`.

## Pendiente lab

Rebuild/restart PaqAgent + smoke POST ordenes-compra en modo agente.
