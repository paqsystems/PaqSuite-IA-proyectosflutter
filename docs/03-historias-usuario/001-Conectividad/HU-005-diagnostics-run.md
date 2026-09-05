# HU-005 — Diagnóstico de punta a punta

| Campo | Valor |
|-------|--------|
| Identificador | HU-005 |
| Estado | Pendiente de Revisión |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Operador PaqSystems / soporte |
| Dependencias | HU-004 Finalizado |
| Clasificación | HU SIMPLE |
| Repo de implementación | este + `PaqSuite-IA-TANGO` (`AgentGatewayClient`) |
| TR | [TR-006](../../04-tareas/001-Conectividad/TR-006-diagnostics-e2e.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §7 / §11 |
| C1 | [c1-20260905-TR-006.md](../../08-control/c1-20260905-TR-006.md) — Apto; Q1–Q8 |

### Narrativa

Como **soporte** quiero **ejecutar `diagnostics.run` desde PaqSuite (o un endpoint interno)** para saber si el agente está vivo y si SQL local responde, sin ir al servidor del cliente.

### Criterios de aceptación

1. Laravel (o un cliente interno autorizado) envía job `diagnostics.run` al `agentId` del tenant (con `traceId`).
2. Respuesta `success` incluye al menos: versión, sqlConnectionOk, agentId, y readiness (`network_ok` … o `operational` / motivo de `degraded`).
3. Si el agente está caído / heartbeat fuera de TTL: `offline`, sin intentar SQL remoto.
4. Timeout configurable (default 30 s) → `timeout`.
5. Si hay sesión gateway pero SQL/esquema fallan → `degraded` (no ocultar falla de red con falla de esquema).

Sin Tailscale en la prueba e2e. Sin fallback SQL por `host`.

Siguiente: **paso D1** de TR-006.

```gherkin
Feature: diagnostics.run
  Scenario: Agente sano
    When se envía diagnostics.run al agentId piloto
    Then status es success
    And sqlConnectionOk es true
    And readiness llega a operational
  Scenario: Servicio detenido
    Given el servicio PaqAgent está Stopped
    When se envía diagnostics.run
    Then status es offline
  Scenario: SQL caído con red OK
    Given el agente autenticado y SQL local inaccesible
    When se envía diagnostics.run
    Then status es degraded
```
