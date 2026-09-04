# TR-005 — PaqAgent (servicio)

| Campo | Valor |
|-------|--------|
| TR | TR-005 |
| Estado | Pendiente |
| HU | [HU-004](../../03-historias-usuario/001-Conectividad/HU-004-agente-heartbeat.md) |
| Repo | este (`src/PaqAgent`) |
| Orden D10 | 3 |

### Tareas

- [ ] Worker Service .NET 8 instalable como Windows Service.
- [ ] SignalR client, Bearer, `RegisterAgent`, heartbeat (actualiza last_seen), reconexión Polly.
- [ ] Heartbeat **30 s**, TTL online **90 s** (`PaqContracts.AgentDefaults`).
- [ ] Lee **solo** `appsettings.local.json` + appsettings base sin secretos de producción.
- [ ] Lab (D10): usar/actualizar [lab-local.md](../../06-operacion/lab-local.md) (plantilla `appsettings.local.json`, tramos 2–3). **Sin Tailscale. Sin `dev-agent-token`.**
- [ ] Readiness interno: network_ok → gateway_authenticated → sql_connection_ok → schema_ready → operational.
- [ ] Logs archivo (conexión, jobs, errores, readiness); no loguear token/password.
- [ ] Identidad: machineName, sqlServerName, version.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Contratos compartidos (`PaqContracts`) se crean en TR-002 y los consume esta TR. |
| Pendientes | |
