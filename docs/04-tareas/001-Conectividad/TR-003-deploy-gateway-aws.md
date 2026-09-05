# TR-003 — Deploy Gateway en AWS

| Campo | Valor |
|-------|--------|
| TR | TR-003 |
| Estado | Finalizado |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) (CA 9–13) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6.1 |
| **Repo** | **este** (`docs/06-operacion/` + plantillas) + **cuenta AWS** (ops humano) |
| Orden D10 | 2 |
| Dependencia | [TR-002](TR-002-paqgateway-app.md) **Finalizado** |
| C1 | [c1-20260905-TR-003.md](../../08-control/c1-20260905-TR-003.md) — Apto; N1–N6 |
| D1 | [d1-20260905-TR-003.md](../../08-control/d1-20260905-TR-003.md) — confirmado |
| Runbook | [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) · [deploy/](../../06-operacion/deploy/) |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| N1 | Compute | EC2 |
| N2 | TLS | Nginx en la EC2 |
| N3 | DNS | `gateway.paqsystems.com` → EIP (ops; SPEC decía paqsuite.com) |
| N4 | Kestrel | `0.0.0.0:5100` + SG Laravel; `/internal` no en Nginx público |
| N5 | Stub | `UseDevAuthStub=false` en prod |
| N6 | Publish | `artifacts/paqgateway` → `/opt/paqgateway` + systemd |

### Tareas

- [x] `dotnet publish` Release documentado; smoke local OK (`artifacts/paqgateway`).
- [x] Plantillas SG/red/systemd/Nginx/env en runbook (aplicación en EC2 = ops).
- [x] systemd `paqgateway.service` + Nginx TLS/Upgrade + DNS documentados.
- [x] Env prod documentado (N5); sin `change-me-in-production` en plantillas.
- [~] Verificar WSS público fuera de Tailscale — **parcial 2026-09-05** (ver Traza / resultado abajo). No marcar éxito completo (caso A) aún.
- [x] Internal desde Laravel en VPC (**humano / AWS** — Forge → `10.0.1.224:5100` 200).
- [x] Runbook [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) con pasos §10 reales.
- [x] Sin Tailscale / sin SQL a Internet (documentado).
- [x] Instalación exhaustiva + ficha EC2 *Paq-Gateway-IA* (2026-09-05).

### Traza

| | |
|--|--|
| Archivos | `deploy-gateway-aws.md`; `urls-deploy.md`; `deploy/*`; commit `fa96af5` |
| Comandos | publish OK; ops systemd/certbot/curls; LabAgentMock prod (abajo) |
| Notas | CA 9, 11–13 OK. CA 10 **parcial (caso B)**. Hostname `gateway.paqsystems.com`. |
| Pendientes | Caso A WSS (token real + authenticate TANGO); EIP opcional; keys path local. |
| F1 | [f1-20260905-TR-003.md](../../08-control/f1-20260905-TR-003.md) — Aprobado con observaciones |
| F | [f-20260905-TR-003.md](../../08-control/f-20260905-TR-003.md) — OK con salvedad CA 10 |

### Resultado prueba manual CA 10 (2026-09-05)

```text
PC oficina → https://gateway.paqsystems.com/agent-hub (sin Tailscale)
curl hub:400 ssl:0
dotnet run --project tools/LabAgentMock -- https://gateway.paqsystems.com/agent-hub lab-agent-01 lab lab-token-manual

Salida:
  Conectando a https://gateway.paqsystems.com/agent-hub?agentId=...&clientId=lab&agentToken=lab-token-manual
  Conectado. agentId=lab-agent-01. Ctrl+C para salir.
  Conexión cerrada
  Listo.
(Sin líneas "Heartbeat OK")
```

**Interpretación:** camino público TLS + negotiate/WSS **alcanza** el hub (CA 10 parcial). Cierre inmediato sin heartbeat encaja con **rechazo de auth** en prod (`UseDevAuthStub=false` + token lab / Laravel authenticate) — **caso B**. No es éxito completo (caso A: heartbeats + status `online`).

Si el operador cortó con Ctrl+C al instante, repetir y dejar correr ~30–40 s: si aparecen `Heartbeat OK`, reevaluar (inesperado con token lab).

---

## Prueba manual CA 10 — WSS público (LabAgentMock)

Objetivo: demostrar handshake SignalR hacia el hub de producción **sin Tailscale** (CA HU-002 #10 / checklist runbook §8 #3).

**URL canónica:** `https://gateway.paqsystems.com/agent-hub`  
**Herramienta:** `tools/LabAgentMock` (mock de lab; **no** es PaqAgent / TR-005).  
**Red:** PC de oficina / casa con salida HTTPS 443 a Internet (no VPN Tailscale como camino).

### Premisas

| Caso | Qué necesitás | Resultado válido |
|------|----------------|------------------|
| **A — Éxito completo** | Agente dado de alta (TR-001) + Laravel `POST /api/internal/gateway/authenticate` cableado + keys Gateway↔Laravel OK | `Conectado` + heartbeats; status `online` desde Forge |
| **B — Camino WSS hasta auth** | Solo Gateway prod actual (`UseDevAuthStub=false`) sin authenticate TANGO o token inválido | Fallo al `StartAsync` (401/403/error auth). **Cuenta como evidencia parcial:** TLS+WSS+Nginx llegaron al hub; falta auth producto |

En prod **no** uses el stub Dev ni `lab-token-manual` esperando éxito (el stub está apagado).

### Pasos (PowerShell, raíz del repo)

1. Confirmar DNS/TLS (opcional, 10 s):

```powershell
curl.exe -sS -o NUL -w "hub:%{http_code} ssl:%{ssl_verify_result}`n" https://gateway.paqsystems.com/agent-hub
```

Esperado: `hub:400` (o similar ≠ cert error) y `ssl:0`.

2. Arrancar el mock contra prod (reemplazar ids/token reales del alta TR-001 si vas por caso A):

```powershell
dotnet run --project tools/LabAgentMock -- `
  https://gateway.paqsystems.com/agent-hub `
  <agentId> `
  <clientId> `
  <agentToken>
```

Ejemplo solo para **caso B** (esperar rechazo de auth; no inventar que “funcionó”):

```powershell
dotnet run --project tools/LabAgentMock -- `
  https://gateway.paqsystems.com/agent-hub `
  lab-agent-01 `
  lab `
  lab-token-manual
```

3. Interpretar la consola del mock:

| Salida | Interpretación | Casilla CA 10 |
|--------|----------------|---------------|
| `Conectado. agentId=...` y `Heartbeat OK ...` | Handshake WSS OK | Marcar **[x]** éxito completo (caso A) |
| Excepción al conectar / `Conexión cerrada` por auth | Camino público llegó al Gateway; auth bloquea | Anotar en Traza como **parcial**; no marcar éxito completo hasta caso A |
| Timeout / DNS / cert / connection refused | Problema de red/TLS/Nginx | **No** OK; revisar SG 443, DNS, `nginx`, `paqgateway` |

4. Si hubo **caso A** (conectado): en **Forge** (consola AWS), con la API key de `Gateway__InternalApiKey`:

```bash
curl -sS -H "X-Paq-Internal-Api-Key: <Gateway__InternalApiKey>" \
  http://10.0.1.224:5100/internal/agents/<agentId>/status
```

Esperado mientras el mock sigue arriba: `"status":"online"` (o equivalente contrato). Al cortar el mock (Ctrl+C) y esperar TTL (~90 s): pasa a `offline`.

5. Registrar resultado en Traza (fecha, caso A/B, agentId, salida resumida). Marcar la tarea WSS `[x]` solo con caso A, o dejar `[ ]` + nota “parcial B” si solo hubo rechazo auth.

### Qué no hacer

- No apuntar el mock a Tailscale ni a `http://10.0.1.224:5100` para esta prueba (eso no es el hub público de agentes).
- No poner `UseDevAuthStub=true` en el servidor para “hacer pasar” CA 10.
- No commitear `agentToken` reales en la TR ni en el repo.

### Referencias

- Runbook checklist: [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) §8 #3  
- Lab local (mismo mock, URL localhost): [lab-local.md](../../06-operacion/lab-local.md)  
- F1/F: observación CA 10 pendiente hasta esta prueba

### Cierre

| Campo | Valor |
|-------|--------|
| Finalizado | 2026-09-05 (humano) |
| Salvedad | CA 10 caso B (WSS+TLS OK; auth token lab rechaza). Caso A = TR auth Laravel / piloto posterior. |
| F1 / F | Aprobados con observaciones |

Siguiente D10: **HU-004 / TR-005** (PaqAgent).
