# TR-008 — Corte duro modo agente (legacy SQL directo permanece)

| Campo | Valor |
|-------|--------|
| TR | TR-008 |
| Estado | Especificado |
| HU | [HU-007](../../03-historias-usuario/001-Conectividad/HU-007-corte-duro-modo-agente.md) |
| **Repo** | **`PaqSuite-IA-TANGO`** (no se scaffoldea ni implementa en este repo) |
| Orden D10 | 6 |
| Dependencia | TR-007 Finalizado |
| C1 | [c1-20260905-TR-008.md](../../08-control/c1-20260905-TR-008.md) — Apto; Q1–Q7 |
| D1 | [d1-20260905-TR-008.md](../../08-control/d1-20260905-TR-008.md) — pendiente confirmación |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| Q1 | Alcance | Auth + todos los services Gateway; AuthService ya OK en TR-007 |
| Q2 | HTTP login | `ERROR_AGENT_OFFLINE` → **503** (no 401) + señal `AGENT_OFFLINE` |
| Q3 | Logs | Quitar texto “fallback a SQL directo” si no hay fallback real |
| Q4 | Sin `agent_id` | SQL legacy solo donde ya exista (Auth); no inventar dual-path |
| Q5 | Tests | 503 login + no-fallback AuthService + control grep/regresión |
| Q6 | `host` | No ruteo/reintento con `agent_id` |
| Q7 | Prohibidos | Tailscale, fallback modo agente |

### Tareas (TANGO)

- [ ] `AuthController` (login): mapear `ERROR_AGENT_OFFLINE` → HTTP **503**; respuesta con `AGENT_OFFLINE`.
- [ ] Auditoría: con `agent_id`, offline/timeout/excepción Gateway → error claro **sin** SQL por `host` (corregir si queda alguno).
- [ ] Renombrar logs engañosos “fallback a SQL directo” → degradado/AGENT_OFFLINE (`agent_id`, sin secretos).
- [ ] Tenant **sin** `agent_id`: conservar SQL directo solo en caminos legacy existentes (Auth).
- [ ] Test: login/modo agente offline → 503 + AGENT_OFFLINE.
- [ ] Test/grep de control: no reintroducir fallback SQL tras fallo Gateway con `agent_id`.
- [ ] **Prohibido:** Tailscale, fallback modo agente, `host` como llave de ruteo con `agent_id`.

### Traza (completar al ejecutar en TANGO)

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Trabajo Laravel: repo TANGO. C1 Q1–Q7. |
| Pendientes | Confirmación D1 → D en TANGO |
