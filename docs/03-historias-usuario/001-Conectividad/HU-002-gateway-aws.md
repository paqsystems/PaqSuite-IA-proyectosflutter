# HU-002 — Gateway publicado (lab + AWS)

| Campo | Valor |
|-------|--------|
| Identificador | HU-002 |
| Estado | Finalizado |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Operador de infraestructura PaqSystems |
| Dependencias | D1, D2, D8, D16; HU-001 (contrato `agent_id` / token en catálogo) |
| Clasificación | HU COMPLEJA (servicio .NET + infra AWS) |
| Repo de implementación | **este** (`src/PaqGateway`, `src/PaqContracts`) + cuenta AWS (deploy) |
| TR | [TR-002](../../04-tareas/001-Conectividad/TR-002-paqgateway-app.md) (app), [TR-003](../../04-tareas/001-Conectividad/TR-003-deploy-gateway-aws.md) (deploy) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6–§7 (v1.2) |
| A1 | [a1-20260904-SPEC-AGW-001.md](../../08-control/a1-20260904-SPEC-AGW-001.md) — Apto con observaciones |
| C1 (app) | [c1-20260904-TR-002.md](../../08-control/c1-20260904-TR-002.md) — TR-002 Apto / **Finalizado** |
| C1 (deploy) | [c1-20260905-TR-003.md](../../08-control/c1-20260905-TR-003.md) — TR-003 Apto; N1–N6 |

Origen: SPEC-AGW-001. Una HU = una capacidad observable. Paso B regeneró desde SPEC v1.2; B1 enriqueció sin inventar. Obligatoria **D1** antes de D (COMPLEJA).

### Narrativa

Como **operador de infraestructura PaqSystems** quiero **un PaqGateway (.NET 8) alcanzable por los agentes (hub SignalR) y por Laravel (API interna con API key)**, con online basado en heartbeat+TTL, para que el caño Agent↔Gateway↔Laravel funcione en lab y luego en AWS sin Tailscale ni SQL expuesto a Internet.

### Alcance

**In**

- Hub SignalR `/agent-hub` (lab: `http://127.0.0.1:5100`; prod: `https://gateway.paqsuite.com` WSS/443).
- API interna: `POST /internal/jobs/send`, `GET /internal/agents/{agentId}/status`, protegidas con API key.
- Auth de agentes contra Laravel (`POST /api/internal/gateway/authenticate`) como fuente de verdad del token; cache corta permitida.
- Online = último heartbeat dentro del TTL (defaults SPEC: heartbeat 30 s, TTL 90 s). Autoridad: Gateway.
- Registro en memoria MVP: `agentId` → connection + `lastSeenAt` (+ `lastSeenIp` observación, no rutea).
- Jobs en vuelo al reinicio → `cancelled` (auditado), sin reentrega silenciosa.
- Una instancia MVP; sin Redis/multi-instancia.
- Deploy AWS (misma VPC que Laravel): TLS, SG (443 público; `/internal/*` privado; 1433 no a Internet); secretos por entorno; instructivo [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md).
- Contrato de job (§7): `traceId` obligatorio; `jobId` lo asigna el Gateway; estados `success|failed|timeout|offline|degraded|cancelled`.

**Out** (SPEC §8 / fase 2)

- Multi-instancia Gateway / Redis backplane; N agentes por tenant.
- Auto-update del agente; operaciones de negocio más allá del caño (esa es HU-005/006).
- Tailscale como camino de producto; abrir SQL 1433 a Internet.
- Persistencia obligatoria de `last_seen_*` en Laravel (autoridad = Gateway).
- Inventar pantalla ABM del Gateway.

### Reglas (solo SPEC)

1. Laravel habla al Gateway por URL **interna** VPC, no por hostname público ni Tailscale.
2. No hardcodear lista de clientes/tokens en `appsettings` de producción.
3. Prohibido dejar secretos `change-me-in-production` en el servidor.
4. Online ≠ “hay socket”: hace falta heartbeat dentro del TTL.
5. Sin fallback SQL en esta HU (corte duro = HU-007).

### Criterios de aceptación

**TR-002 (aplicación / lab)**

1. Proyecto ASP.NET Core .NET 8 con hub `/agent-hub` escuchando en lab `127.0.0.1:5100`.
2. Agente válido completa handshake SignalR; el Gateway lo registra y lo marca online mientras el heartbeat esté dentro del TTL.
3. Heartbeat default 30 s / TTL 90 s (`PaqContracts.AgentDefaults`).
4. `GET /internal/agents/{agentId}/status` con API key refleja online/offline (y degraded si aplica al contrato).
5. `POST /internal/jobs/send` con API key acepta job con `traceId` + `agentId` (+ contrato §7); sin API key → 401 o 403.
6. Gateway valida token del agente contra Laravel (path D4/SPEC); cache corta permitida; sin lista hardcodeada de prod.
7. Al apagar/reiniciar el proceso, jobs en vuelo pasan a `cancelled` (sin reentrega silenciosa).
8. Hay prueba automatizada o de host: rechazo sin API key; online/offline por TTL; job hacia agente mock/test host.

**TR-003 (deploy AWS)**

9. Instancia (EC2 o equivalente) en la misma VPC que Laravel, .NET 8, systemd, reverse proxy TLS.
10. `https://gateway.paqsuite.com/agent-hub` acepta handshake WSS.
11. SG: 443 alcanzable desde Internet; 1433 no a Internet; Laravel alcanza `/internal/*` por red privada.
12. Secretos solo por entorno; instructivo de deploy actualizado en este repo.
13. Desde host Laravel (red privada): `GET /internal/agents/{id}/status` con API key responde OK de health del camino interno.

### Gherkin

```gherkin
Feature: PaqGateway lab y AWS
  Scenario: Hub lab
    Given PaqGateway en http://127.0.0.1:5100
    When un agente válido se conecta a /agent-hub
    Then el handshake completa
    And el agentId queda online mientras el heartbeat esté dentro del TTL

  Scenario: API interna exige clave
    When un cliente anónimo llama POST /internal/jobs/send sin API key
    Then responde 401 o 403

  Scenario: Hub público AWS
    When un agente válido se conecta a https://gateway.paqsuite.com/agent-hub
    Then el handshake WSS completa
    And el gateway registra el agentId como online

  Scenario: Reinicio cancela jobs en vuelo
    Given un job en vuelo en el Gateway
    When el proceso Gateway se reinicia
    Then ese job queda cancelled
    And no se reentrega en silencio al reconectar
```

### Dudas / supuestos / decisión pendiente (→ TR / C1)

| ID | Tema | Origen |
|----|------|--------|
| Q-G1 | Body y códigos exactos de `authenticate` Laravel | SPEC §6.4 / A1 |
| Q-G2 | Segundos de cache de token (sugerencia A1: 60 s configurable) | A1 |
| Q-G3 | Schema JSON exacto de `/status` | A1 |
| A1-03 | Nombre del header de API key interna | A1 |
| A1-04 | `jobs/send` síncrono vs async (sugerencia A1: síncrono MVP) | A1 |
| H10 | EC2 vs equivalente; Nginx vs ALB | informe 08 |
| H12 | DNS + VPC listos para cierre real TR-003 | informe 08 |
| A1-S1 | Stub de auth Laravel en lab documentado | A1 |

No inventar features. Cerrar en TR-002/TR-003 + C1.

### Veredicto B1

**Lista para TR: Sí con observaciones** (observaciones = tabla de dudas; no bloquean C si C1 las fija o adopta defaults A1).

### Cierre

| Campo | Valor |
|-------|--------|
| Finalizado | 2026-09-05 (humano) |
| TR-002 | Finalizado (app CA 1–8) |
| TR-003 | Finalizado (deploy CA 9–13; CA 10 salvedad caso B) |
| Hostname ops | `https://gateway.paqsystems.com/agent-hub` (SPEC histórico `paqsuite.com`) |

Siguiente D10: **HU-004 / TR-005**.
