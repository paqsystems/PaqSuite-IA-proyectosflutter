# TR-005 — PaqAgent (servicio)

| Campo | Valor |
|-------|--------|
| TR | TR-005 |
| Estado | Finalizado |
| HU | [HU-004](../../03-historias-usuario/001-Conectividad/HU-004-agente-heartbeat.md) |
| SPEC | SPEC-AGW-001 §6.3 / §8; D16 |
| Repo | **este** (`src/PaqAgent`, `PaqContracts`) |
| Orden D10 | 3 |
| Dependencia | HU-002 Finalizado (Gateway lab o AWS) |
| C1 | [c1-20260905-TR-005.md](../../08-control/c1-20260905-TR-005.md) — Apto; P1–P8 |
| D1 | [d1-20260905-TR-005.md](../../08-control/d1-20260905-TR-005.md) — confirmado |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| P1 | Auth al hub | Query `agentId`/`clientId`/`agentToken` (M8). **No** Bearer ni `RegisterAgent`. |
| P2 | Timings | Heartbeat 30 s / TTL 90 s → `AgentDefaults` |
| P3 | Reconnect | Backoff 5/10/20/30/60 s (`WithAutomaticReconnect`) |
| P4 | Config | `appsettings` + `appsettings.local.json` (gitignored); sin `dev-agent-token` |
| P5 | last_seen | Solo vía Gateway heartbeat; Laravel lee status API |
| P6 | Readiness + jobs | `Readiness` en heartbeat; `ExecuteJob`/`CompleteJob` stub (SQL profundo = TR-006) |
| P7 | Hosting | Worker + Windows Service; lab `dotnet run` |
| P8 | Prueba | Online con token OK; rechazo token inválido; lab-local tramos 2–3 |
| Lab | Primero | Gateway local `:5100` + stub Dev |

### Tareas

- [x] Worker Service .NET 8 + `UseWindowsService()`; lab `dotnet run`.
- [x] Cliente SignalR a `GatewayUrl/agent-hub` con query M8; método hub `Heartbeat`; reconexión P3.
- [x] Heartbeat **30 s**, online Gateway TTL **90 s** (`AgentDefaults`).
- [x] Solo `appsettings` + `appsettings.local.json` (sin secretos de prod en git).
- [x] Lab: actualizar [lab-local.md](../../06-operacion/lab-local.md) tramos 2–3. **Sin Tailscale. Sin `dev-agent-token`.**
- [x] Readiness en heartbeat (P6); identidad: machineName, sqlServerName (si config), version.
- [x] Logs Serilog archivo; URL de hub con token redactado; no loguear password.
- [x] Handlers hub `ExecuteJob` / `CompleteJob` (stub seguro; sin SQL libre).
- [x] Prueba: token válido → status `online` (lab smoke); unit tests HubUrl/options; token vacío no conecta.

### Traza

| | |
|--|--|
| Archivos | `src/PaqAgent/Program.cs`; `Options/AgentOptions.cs`; `HubUrlBuilder.cs`; `Services/AgentGatewayConnector.cs`; `appsettings*.json`; `appsettings.local.json.example`; `tests/PaqAgent.Tests/*`; `docs/06-operacion/lab-local.md` |
| Comandos | `dotnet test PaqAgentGateway.sln` — PaqAgent.Tests **5 passed**; PaqGateway.Tests 5 (1 flaky TTL re-run OK). Smoke lab: Gateway+PaqAgent → status **online** (`lab-agent-01`). |
| Notas | CA HU-004: (1) query M8 + identidad en logs/heartbeat; (2) heartbeat 30 s; (3) online vía Gateway; (4) reconnect delays; (5) sin config / token vacío → no online + log error sin token; (6) GET status online en smoke. Job stub TR-005; diagnostics e2e = TR-006. |
| Pendientes | Humano Finalizado. Windows Service en cliente real (ops). TR-006 diagnostics. |
| F1 | [f1-20260905-TR-005.md](../../08-control/f1-20260905-TR-005.md) — Aprobado con observaciones |
| F | [f-20260905-TR-005.md](../../08-control/f-20260905-TR-005.md) — OK; puede Finalizar |

Siguiente: humano puede **Finalizar** TR-005 / HU-004. Luego HU-005 / TR-006.

### Prueba manual — lab tramo 2 (CA HU-004)

Objetivo: PaqAgent conecta al Gateway local, queda `online`, y (opcional) pasa a `offline` al cortar.

**Prerrequisitos**

- Repo en `C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ` (o raíz local).
- `src/PaqAgent/appsettings.local.json` presente (copiar desde `.example` si falta).
- Valores lab típicos: `agentId=lab-agent-01`, `clientId=lab`, `agentToken=lab-token-manual`, `gatewayUrl=http://127.0.0.1:5100/agent-hub`.
- Gateway en Development con stub auth (`UseDevAuthStub=true`) y API key `lab-internal-api-key`.
- **Sin Tailscale.** No usar `dev-agent-token`.

**Pasos (PowerShell)**

1. Terminal A — Gateway:

```powershell
cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ
dotnet run --project src/PaqGateway
```

2. Terminal B — PaqAgent:

```powershell
dotnet run --project src/PaqAgent
```

Esperado en B: log `Conectado al Gateway` / heartbeat; URL de hub en log con `agentToken=***` (nunca el token en claro).

3. Terminal C — status online:

```powershell
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }
Invoke-RestMethod http://127.0.0.1:5100/internal/agents/lab-agent-01/status -Headers $h
```

Esperado: `"status":"online"` (y `lastSeenAt` reciente).

4. Opcional — offline por TTL: Ctrl+C en B; esperar ~90 s; repetir el `Invoke-RestMethod` → `"status":"offline"`.

5. Opcional — token vacío: en `appsettings.local.json` vaciar `agentToken`, reiniciar B → no debe conectar / no online; log de error **sin** imprimir token.

**Registro:** anotar fecha y resultado en Traza. Detalle también en [lab-local.md](../../06-operacion/lab-local.md) tramo 2.

### Verificación CA HU-004

| CA | Cómo |
|----|------|
| 1 Start + query + identidad | `AgentGatewayConnector` + logs machineName/version |
| 2 Heartbeat 30 s | loop + `AgentDefaults.HeartbeatSeconds` |
| 3 Online = TTL Gateway | smoke / prueba manual status `online` |
| 4 Reconnect backoff | `WithAutomaticReconnect(5..60)` |
| 5 Token inválido / vacío | early return + log; Closed log; no `dev-agent-token` |
| 6 GET status online | smoke + **§ Prueba manual** |

### Cierre

| Campo | Valor |
|-------|--------|
| Finalizado | 2026-09-05 (humano) |
| F1 / F | Aprobados con observaciones |

Siguiente D10: **HU-005 / TR-006**.
