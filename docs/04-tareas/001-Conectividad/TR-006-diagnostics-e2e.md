# TR-006 — diagnostics.run e2e

| Campo | Valor |
|-------|--------|
| TR | TR-006 |
| Estado | Pendiente |
| HU | [HU-005](../../03-historias-usuario/001-Conectividad/HU-005-diagnostics-run.md) |
| Repos | este + **TANGO** (`AgentGatewayClient`) |
| Orden D10 | 4 |

### Tareas

- [ ] Operación interna `diagnostics.run` en el agente (SQL ping + versión + readiness).
- [ ] Laravel: método `sendJob` / `runDiagnostics` contra el Gateway interno (con `traceId`).
- [ ] Prueba real: AWS Laravel → AWS Gateway → agente en un Windows con SQL, **sin Tailscale**.
- [ ] Logs de duración (tramos Laravel→GW→agente→SQL→retorno) suficientes para no adivinar. Mapa de fallas: [lab-local.md](../../06-operacion/lab-local.md) §2 y §6.
- [ ] Caso degraded documentado (SQL down con agente autenticado).
- [ ] Sin fallback SQL por `host` si el tenant tiene `agent_id`.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Parte Laravel en repo TANGO. |
| Pendientes | |
