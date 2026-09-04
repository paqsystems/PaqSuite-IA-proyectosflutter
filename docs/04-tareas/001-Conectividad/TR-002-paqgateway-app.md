# TR-002 — PaqGateway (aplicación)

| Campo | Valor |
|-------|--------|
| TR | TR-002 |
| Estado | Pendiente |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) (código; el deploy es TR-003) |
| Repo | este (`src/PaqGateway`, `src/PaqContracts`) |
| Orden D10 | 2 (junto con TR-003) |

El scaffold deja la solution vacía. Esta TR implementa el Gateway; **no** se hace en el scaffold.

### Tareas

- [ ] Proyecto ASP.NET Core .NET 8, hub `/agent-hub`.
- [ ] `PaqContracts`: job (con `traceId`), result, identity, heartbeat, estados (`success|failed|timeout|offline|degraded|cancelled`), errores (`AGENT_OFFLINE`, `AGENT_TIMEOUT`, …).
- [ ] Registro en memoria `agentId → connectionId` **más** `last_seen_at`; online = dentro de TTL (**heartbeat 30 s, TTL 90 s**, H8 / `AgentDefaults`).
- [ ] Autoridad del online: **Gateway**; Laravel consulta `GET /internal/agents/{agentId}/status` (H4).
- [ ] `POST /internal/jobs/send`, API key.
- [ ] Autenticación de agentes contra Laravel (cache corta de token).
- [ ] Timeouts, correlación `jobId` + `traceId`, logs sin secretos.
- [ ] Al shutdown/restart: jobs en vuelo → `cancelled` (sin reentrega silenciosa).
- [ ] `launchSettings`: HTTP local `127.0.0.1:5100` para dev. **No Tailscale.** Lab por tramos: [lab-local.md](../../06-operacion/lab-local.md).
- [ ] Test: job a agente mock / test host; rechazo sin API key; online/offline por TTL.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |
