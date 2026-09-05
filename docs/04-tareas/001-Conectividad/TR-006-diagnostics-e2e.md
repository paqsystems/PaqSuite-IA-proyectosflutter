# TR-006 — diagnostics.run e2e

| Campo | Valor |
|-------|--------|
| TR | TR-006 |
| Estado | Especificado (slice **este repo** en D; TANGO pendiente) |
| HU | [HU-005](../../03-historias-usuario/001-Conectividad/HU-005-diagnostics-run.md) |
| Repos | **este** (`PaqAgent`) + **TANGO** (`AgentGatewayClient`) |
| Orden D10 | 4 |
| Dependencia | TR-005 Finalizado |
| C1 | [c1-20260905-TR-006.md](../../08-control/c1-20260905-TR-006.md) — Apto; Q1–Q8 |
| D1 | [d1-20260905-TR-006.md](../../08-control/d1-20260905-TR-006.md) — confirmado |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| Q1 | Header Laravel→GW | `X-Paq-Internal-Api-Key` (M1); corregir TANGO |
| Q2 | `traceId` | Obligatorio en `sendJob`; generar en Laravel si falta |
| Q3 | Payload success | `agentId`, `agentVersion`, `sqlConnectionOk`, `readiness` (+ opcionales) |
| Q4 | Agente | `diagnostics.run` real (SQL ping); degraded si SQL down |
| Q5 | Laravel | `runDiagnostics` / `sendJob` + config URL interna |
| Q6 | Prueba | Lab primero; AWS en Traza ops |
| Q7 | Timeout | Default 30 s |
| Q8 | Logs duración | Agente + Laravel (+ Gateway ids) |

### Tareas

**Este repo**

- [x] `diagnostics.run` en PaqAgent: SQL ping + `data` Q3 + readiness; degraded si SQL inaccesible/ausente.
- [x] Sin SQL libre; otras operations → `OPERATION_NOT_ALLOWED` (auth.login = TR-007).
- [x] Logs de duración del job en agente (`durationMs`, traceId, jobId).
- [x] Lab: [lab-local.md](../../06-operacion/lab-local.md) tramo 4 + § prueba manual abajo.
- [x] Tests: `DiagnosticsRunnerTests` (degraded sin SQL / unreachable / success).

**TANGO**

- [ ] Header `X-Paq-Internal-Api-Key`; body con `traceId`.
- [ ] Método `runDiagnostics` (o equiv.) → `sendJob(..., diagnostics.run)`.
- [ ] Logs `traceId` / duración; sin secretos.
- [ ] Sin fallback SQL por `host` si `agent_id` presente (al menos en este camino).

**Ops / e2e**

- [x] Lab e2e manual (Cursor, 3 terminales) — evidencia `degraded`/`SQL_NOT_CONFIGURED` 2026-09-05.
- [ ] (Opcional) AWS: Forge → `http://10.0.1.224:5100` → agente Windows.

### Traza (este repo)

| | |
|--|--|
| Archivos | `PaqContracts.JobOperations`; `PaqAgent/Diagnostics/*`; `AgentGatewayConnector`; `lab-local.md`; tests |
| Comandos | `dotnet test tests/PaqAgent.Tests` → **8 passed** (2026-09-05) |
| Notas | Slice A del D1. TANGO no tocado. Lab sin SQL 2026-09-05: `degraded` / `SQL_NOT_CONFIGURED` OK. |
| Pendientes | D en TANGO; F1/F al cerrar ambos (o F1 parcial este repo si se acuerda). |

### Prueba manual — lab tramo 4 (solo este repo)

Sin TANGO. Sin Tailscale. **Dónde:** terminal integrada de **Cursor** (panel Terminal), en Windows, raíz del repo  
`C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ`.

**Abrí tres terminales nuevas** en Cursor (icono `+` / “New Terminal”). No asumas que Gateway o PaqAgent ya están corriendo de otra sesión.

#### Terminal 1 — Gateway

```powershell
cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ
dotnet run --project src/PaqGateway
```

Dejá esta terminal abierta (proceso en ejecución).

#### Terminal 2 — PaqAgent

```powershell
cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ
dotnet run --project src/PaqAgent
```

Requiere `src/PaqAgent/appsettings.local.json` (copiar desde `.example` si no existe).  
Esperado: log de conexión al Gateway. Dejá esta terminal abierta.

#### Terminal 3 — job diagnostics

En una **tercera** terminal de Cursor (también en la raíz del repo):

```powershell
cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }
$body = @{
  traceId = "01DIAGLAB"
  agentId = "lab-agent-01"
  clientId = "lab"
  operation = "diagnostics.run"
  timeoutSeconds = 30
  parameters = @{}
} | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5100/internal/jobs/send -Method Post -Headers $h -ContentType "application/json" -Body $body
```

| Condición (`appsettings.local.json`) | Esperado |
|--------------------------------------|----------|
| Sin bloque `sql` / server-database vacíos | `status=degraded`, `errorCode=SQL_NOT_CONFIGURED`, `sqlConnectionOk=false`, `readiness=gateway_authenticated` |
| SQL lab OK | `status=success`, `sqlConnectionOk=true`, `readiness=operational` |
| SQL server incorrecto | `status=degraded`, `errorCode=SQL_UNREACHABLE` |
| Terminal 2 detenida (agente parado) | `status=offline` |

**Evidencia 2026-09-05 (lab sin SQL):** `status=degraded`, `SQL_NOT_CONFIGURED`, `sqlConnectionOk=false`, `readiness=gateway_authenticated` — OK para el caso “sin SQL”.

Detalle paralelo: [lab-local.md](../../06-operacion/lab-local.md) tramo 4.

Siguiente: **D en TANGO** cuando indiquen.
