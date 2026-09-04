# HU-004 — Agente conectado, autenticado y con heartbeat

| Campo | Valor |
|-------|--------|
| Identificador | HU-004 |
| Estado | Pendiente |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Sistema |
| Dependencias | HU-002; config del agente (lab: `appsettings.local.json` manual; producción: HU-003) |
| Clasificación | HU SIMPLE |
| Repo de implementación | este (`src/PaqAgent`) |
| TR | [TR-005](../../04-tareas/001-Conectividad/TR-005-paqagent-servicio.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) |

### Narrativa

Como **sistema** quiero que **el agente, al iniciar el servicio, abra WSS saliente al Gateway, se autentique y envíe heartbeat**, para que PaqSuite lo vea online sin que AWS inicie ninguna conexión hacia el cliente.

### Nota de lab (D1 / D10)

Para esta HU basta un `appsettings.local.json` escrito a mano con AgentId, ClientId, AgentToken, GatewayUrl y SQL local. El instalador GUI es HU-003 y viene después. **Sin Tailscale. Sin `dev-agent-token`.**

### Criterios de aceptación

1. Al start: conecta a `GatewayUrl`, Bearer token, `RegisterAgent` con agentId, clientId, machineName, sqlServerName, version.
2. Heartbeat periódico (default **30 s**); actualiza `last_seen_at` (y opcionalmente `last_seen_ip`).
3. Online en Gateway/Laravel = heartbeat dentro de TTL (**90 s**, D16 / H8), no solo socket. Autoridad del online: **Gateway**; Laravel consulta status API (H4).
4. Si cae la red: reconecta con backoff (5, 10, 20, 30, 60 s) sin intervención.
5. Token inválido: no queda “online”; log de error claro, sin imprimir el token.
6. Laravel `GET` status via Gateway = `online` cuando el servicio corre y el TTL está vigente.

```gherkin
Feature: Conexión saliente
  Scenario: Online
    Given el servicio PaqAgent running con token válido
    Then el Gateway lista ese agentId como online
    And Laravel consulta status online
  Scenario: Token inválido
    Given AgentToken incorrecto
    Then el agente no queda registrado
    And Laravel no lo muestra online
```
