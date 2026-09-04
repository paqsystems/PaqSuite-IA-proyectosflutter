# TR-001 — Alta modo agente (Laravel)

| Campo | Valor |
|-------|--------|
| TR | TR-001 |
| Estado | Pendiente |
| HU | [HU-001](../../03-historias-usuario/001-Conectividad/HU-001-alta-cliente-agente.md) |
| **Repo** | **`PaqSuite-IA-TANGO`** (no se scaffoldea ni implementa en este repo) |
| Orden D10 | 1 |

### Tareas

- [ ] Migración: `host` y `port` nullable; `agent_id` / `client_id` usables como camino principal; espacio para `last_seen_at` / `last_seen_ip` (auditoría).
- [ ] Token: **columnas en `empresas_conexion`** (hash o cifrado). **Sin tabla `agents`** en el MVP (default scaffold H3).
- [ ] Alta (UI o comando artisan documentado) genera `agentId`, `clientId`, token; no pide IP; 1 agente por tenant.
- [ ] Persistencia del token. Mostrar token una vez.
- [ ] Validación: modo agente = `agent_id` + token; `host` no required.
- [ ] Tests: alta sin host es válida; alta agente sin token es inválida.
- [ ] Sin Tailscale. Sin exigir IP pública.

### Traza (completar al ejecutar en TANGO)

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Trabajo Laravel: repo TANGO, mismos IDs de HU/TR. |
| Pendientes | |
