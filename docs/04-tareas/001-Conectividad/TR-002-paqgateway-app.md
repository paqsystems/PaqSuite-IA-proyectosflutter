# TR-002 — PaqGateway (aplicación)

| Campo | Valor |
|-------|--------|
| TR | TR-002 |
| Estado | Finalizado |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) (CA 1–8; deploy = [TR-003](TR-003-deploy-gateway-aws.md)) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6–§7 v1.2 |
| **Repo** | **este** (`src/PaqGateway`, `src/PaqContracts`, `tests/`) — **no** TANGO |
| Orden D10 | 2 |
| C1 | [c1-20260904-TR-002.md](../../08-control/c1-20260904-TR-002.md) — Apto; M1–M9 |
| D1 | [d1-20260904-TR-002.md](../../08-control/d1-20260904-TR-002.md) — confirmado |
| Lab | [lab-local.md](../../06-operacion/lab-local.md) |

### Decisiones cerradas (post-C1, 2026-09-04)

| ID | Tema | Decisión |
|----|------|----------|
| M1 | Header API key interna | `X-Paq-Internal-Api-Key` ← `Gateway:InternalApiKey` |
| M2 | `jobs/send` | Síncrono → `JobResult` |
| M3 | Cache token | 60 s |
| M4 | Authenticate Laravel | path + body camelCase; 200/401/403 |
| M5 | Stub Dev | `UseDevAuthStub` solo Development |
| M6 | `/status` | online\|degraded\|offline |
| M7 | JSON | camelCase |
| M8 | Auth hub | query `agentId`/`clientId`/`agentToken` |
| M9 | RPC | `ExecuteJob` / `CompleteJob` |

### Tareas

- [x] Ampliar `PaqContracts`: defaults, DTOs, D12, errores, hub method names.
- [x] SignalR hub `/agent-hub` (sin WeatherForecast).
- [x] `launchSettings` `http://127.0.0.1:5100`.
- [x] Registro en memoria + TTL.
- [x] Hub auth M8 + heartbeat + CompleteJob.
- [x] API key M1 en `/internal/*`.
- [x] `GET /internal/agents/{agentId}/status`.
- [x] `POST /internal/jobs/send` síncrono.
- [x] Logs sin tokens.
- [x] Shutdown → `cancelled` (`JobShutdownService`).
- [x] Config Gateway/LaravelApi; stub Dev.
- [x] Tests: 401 sin key; TTL; job mock; offline; Production rechaza stub.
- [x] Sin Tailscale / fallback SQL / lista hardcodeada / `dev-agent-token` default.

### Traza

| | |
|--|--|
| Archivos | `src/PaqContracts/Contracts.cs`; `src/PaqGateway/Program.cs`; `Options/`; `Services/` (registry, auth, jobs); `Hubs/AgentHub.cs`; `Middleware/InternalApiKeyMiddleware.cs`; `Hosting/JobShutdownService.cs`; `appsettings*.json`; `tests/PaqGateway.Tests/GatewayTests.cs` |
| Comandos | `dotnet test PaqAgentGateway.sln --filter FullyQualifiedName~PaqGateway` → **5 passed** |
| Notas | CA HU-002 1–8 (app): hub lab, handshake+online TTL, status, jobs/send+API key, auth stub Dev, cancel on shutdown, tests. CA 9–13 = TR-003. |
| Pendientes | Auth Laravel real (TANGO). Shutdown cancelled sin test dedicado. Commit cuando autoricen. F1: [f1-20260905-TR-002.md](../../08-control/f1-20260905-TR-002.md). F: [f-20260905-TR-002.md](../../08-control/f-20260905-TR-002.md). Deploy = TR-003. |

Siguiente: humano puede **Finalizar TR-002**. HU-002 completa con TR-003. Luego TR-005 o C1 TR-003.
