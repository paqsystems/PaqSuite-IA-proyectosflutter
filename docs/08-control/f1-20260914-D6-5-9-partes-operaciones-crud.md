# Paso F1 — D6.5.9–12 PartesProduccion.Operaciones CRUD

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-14 |

## Evidencia Agente

- Ops `PartesProduccion.Operaciones.{Get,Create,Update,Delete}`
- SP `000065`–`000068`
- Payload `activa`; tests Partes **30 OK**

## Evidencia Host

- `OperacionesService` dual-path + `GET show`
- Control: `OperacionesServiceGatewayControlTest`

## Pendiente lab

Deploy `000065`–`000068` + smoke operaciones.
