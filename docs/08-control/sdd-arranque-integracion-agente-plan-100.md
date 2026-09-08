# Arranque SDD — integración agente (plan 100%) · AgenteCliente

| Campo | Valor |
|-------|--------|
| Repo | `PaqSuite-IA-AgenteCliente-PAQ` |
| Fecha | 2026-09-08 |
| Plan maestro | [plan-20260908-integracion-tango-framework-agente.md](./plan-20260908-integracion-tango-framework-agente.md) |
| Host guide | [adaptar-un-host-al-agente.md](../00-contexto/adaptar-un-host-al-agente.md) |

Este archivo lista **qué procesar con el circuito SDD en este repo**.  
Acá: **whitelist**, runners, scripts SQL del cliente, instalador, PaqGateway.  
El **tablero** del plan 100% vive en este repo; el SDD de plataforma (F1–F9 package) se hace en **Framework**.

**Cómo arrancar un chat:** Cursor en **AgenteCliente** (modo AGENTE-GATEWAY) → “Hacé el paso A” / kickoff citando la oleada **Dx** o la op de plataforma que toque SQL.

**Hoy en whitelist:** `diagnostics.run`, `auth.login` solamente.

---

## SDD en Agente — plataforma (solo si la fase FW exige SQL del cliente)

| Tras fase FW | Posible trabajo SDD acá | Notas |
|--------------|-------------------------|--------|
| **F1** | Ninguno (canal ya MVP) | — |
| **F2** | `auth.session` **solo si** el SPEC de Framework lo exige | Si no: re-login ante cache miss |
| **F3** | Ops menú / empresas / authz (`menu.efectivo`, `user.empresas`, …) + SP | Contrato lo define FW/host |
| **F4** | Ops params / preferencias + SP | |
| **F5** | Ops layouts grillas + SP | |
| **F6** | Ops pivots + SP | |
| **F7** | Ops notifs / audit (+ mail tenant) + SP | |
| **F8** | Ops tasks / grupos + SP | |
| **F9** | Solo si satélite persiste en SQL tenant | Si no SQL → no TR acá |

Por op: constante `JobOperations` + handler + tests + script en `src/PaqAgent/Sql/…` + rebuild. Sin SQL libre.

---

## SDD en Agente — dominio (oleadas D1–D6)

Contrato JSON: Tango (o doc compartida). Implementación: **acá**.

| Oleada | Ops (prioridad) | Orden sugerido |
|--------|-----------------|----------------|
| **D1** | `tango.version`, `clientes.buscar` / `obtener`, `articulos.*`, `pedidos.pendientes`, `stock.consultar`, `saldos.consultar`, `comprobantes.recientes` | Tras F2 shell OK; ideal en paralelo a F3 |
| **D2** | `informes.*` | Tras D1 |
| **D3** | Acopios + parámetros | |
| **D4** | Robinet | |
| **D5** | Partes producción | |
| **D6** | Pedidos venta / OC / resto del inventario F0 | |

**No** whitelist masiva en un solo TR.

Path sin agente: el mismo SP lo llama el host por SQL directo (MUST SP).

---

## Fuera de este repo

| Trabajo | Repo / doc |
|---------|------------|
| F1–F9 package dual-path | Framework — `docs/08-control/sdd-arranque-integracion-agente-plan-100.md` |
| Adopción SDK + jobs UI | Tango — `docs/08-control/sdd-arranque-integracion-agente-plan-100.md` |

---

## Relacionado

- [plan-20260908-integracion-tango-framework-agente.md](./plan-20260908-integracion-tango-framework-agente.md)
- [SPEC-AGW-001](../02-producto/SPEC-AGW-001-producto.md)
- `JobOperations`: `src/PaqContracts/Contracts.cs`
