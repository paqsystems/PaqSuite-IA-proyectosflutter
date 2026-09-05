# TR-008 — Corte duro modo agente (legacy SQL directo permanece)

| Campo | Valor |
|-------|--------|
| TR | TR-008 |
| Estado | Pendiente de Revisión |
| HU | [HU-007](../../03-historias-usuario/001-Conectividad/HU-007-corte-duro-modo-agente.md) |
| **Repo** | **`PaqSuite-IA-TANGO`** (no se scaffoldea ni implementa en este repo) |
| Orden D10 | 6 |
| Dependencia | TR-007 Finalizado |
| C1 | [c1-20260905-TR-008.md](../../08-control/c1-20260905-TR-008.md) — Apto; Q1–Q7 |
| D1 | [d1-20260905-TR-008.md](../../08-control/d1-20260905-TR-008.md) — confirmado; D ejecutado |
| F1 | [f1-20260905-TR-008.md](../../08-control/f1-20260905-TR-008.md) — Aprobado con observaciones |
| F | [f-20260905-TR-008.md](../../08-control/f-20260905-TR-008.md) — apto Finalizado con salvedad |

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

- [x] `AuthController` (login): mapear `ERROR_AGENT_OFFLINE` → HTTP **503**; respuesta con `AGENT_OFFLINE`.
- [x] Auditoría: con `agent_id`, offline/timeout/excepción Gateway → error claro **sin** SQL por `host` (corregir si queda alguno).
- [x] Renombrar logs engañosos “fallback a SQL directo” → degradado/AGENT_OFFLINE (`agent_id`, sin secretos).
- [x] Tenant **sin** `agent_id`: conservar SQL directo solo en caminos legacy existentes (Auth).
- [x] Test: login/modo agente offline → 503 + AGENT_OFFLINE.
- [x] Test/grep de control: no reintroducir fallback SQL tras fallo Gateway con `agent_id`.
- [x] **Prohibido:** Tailscale, fallback modo agente, `host` como llave de ruteo con `agent_id`.

### Criterios HU-007 (verificación D + lab)

| CA | Cómo se verificó |
|----|------------------|
| 1 | Unitario AuthController 503; lab AuthService `tecser` → **5030 / AGENT_OFFLINE** (HTTP e2e salvedad ResolveTenant/Tailscale) |
| 2 | Auditoría services + AuthService sin SQL tras Gateway con `agent_id` |
| 3 | Logs: `AGENT_OFFLINE, sin fallback SQL` + `agent_id` |
| 4 | `AuthServiceGatewayModeTest` + `AgentModeNoSqlFallbackControlTest` (8/8 F1) |
| 5 | Una sola llamada a `validatePasswordViaSqlDirecto` bajo `!shouldUseGateway` |

### Traza (ejecutado en TANGO, rama `FRAMEWORK`)

| | |
|--|--|
| Archivos | `AuthController.php`; logs Pedidos/Stock/Saldos/Clientes/Comprobantes/Articulos/Robinet*; tests `AuthControllerLoginAgentOfflineTest`, `AgentModeNoSqlFallbackControlTest` |
| Comandos | PHPUnit TR-008 → **8 passed** (D y F1); `rg "fallback a SQL directo"` → 0; Tinker AuthService offline → 5030; Gateway `jobs/send` `01OFFLINE` → `offline`/`AGENT_OFFLINE`, luego `success` con agente up |
| Notas | Lab: liberar `:8000` de smoke FRAMEWORK. Tenancy modo agente sin SQL aplicado (nota 20260905). HTTP `tecser` → 5030 AGENT_OFFLINE en `:8002`. Sin commit aún. |
| Pendientes | Commit TANGO / docs ciclo cuando lo pidan; **Finalizado** solo humano; tenancy sin SQL modo agente **aplicado en TANGO** (`ResolveTenant`/`ResolveDictionaryConnection`) — **después** revisar el mismo comportamiento en **PaqSuite-IA-FRAMEWORK** |
