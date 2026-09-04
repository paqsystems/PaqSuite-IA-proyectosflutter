# HU-002 — Gateway publicado en AWS

| Campo | Valor |
|-------|--------|
| Identificador | HU-002 |
| Estado | Pendiente |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Operador de infraestructura PaqSystems |
| Dependencias | D1, D2, D8 |
| Clasificación | HU COMPLEJA (infra + servicio) |
| Repo de implementación | este (`src/PaqGateway`) + infra AWS |
| TR | [TR-002](../../04-tareas/001-Conectividad/TR-002-paqgateway-app.md), [TR-003](../../04-tareas/001-Conectividad/TR-003-deploy-gateway-aws.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6 |

### Narrativa

Como **operador de infraestructura** quiero **un PaqGateway en Amazon, en la misma VPC que Laravel, con HTTPS/WSS en gateway.paqsuite.com**, para que los agentes de los clientes se conecten por el puerto 443 saliente y Laravel les mande jobs por red interna.

### Criterios de aceptación

1. Instancia (EC2 o equivalente) con .NET 8, systemd, reverse proxy TLS.
2. `https://gateway.paqsuite.com/agent-hub` acepta handshake SignalR.
3. Security Group: 443 público; SQL 1433 **no** abierto a Internet; Laravel alcanza `/internal/*` por red privada.
4. Secretos por entorno, no `change-me-in-production` en el servidor.
5. Existe un instructivo en este repo: [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) (checklist AWS ahora; comandos de publish al cerrar TR-003).
6. Health: proceso up + Laravel puede llamar `GET /internal/agents/{id}/status` con API key.

Online = heartbeat dentro de TTL (30 s / 90 s). Laravel habla por URL **interna** VPC. **No Tailscale.**

```gherkin
Feature: Gateway en AWS
  Scenario: Hub público
    When un agente válido se conecta a https://gateway.paqsuite.com/agent-hub
    Then el handshake WSS completa
    And el gateway registra el agentId como online
  Scenario: API interna no es pública
    When un cliente anónimo llama POST /internal/jobs/send sin API key
    Then responde 401 o 403
```
