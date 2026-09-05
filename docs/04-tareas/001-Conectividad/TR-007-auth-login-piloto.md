# TR-007 — Operación piloto (`auth.login`)

| Campo | Valor |
|-------|--------|
| TR | TR-007 |
| Estado | Especificado |
| HU | [HU-006](../../03-historias-usuario/001-Conectividad/HU-006-auth-login.md) |
| Repos | **este** (`PaqAgent`) + **TANGO** (`AuthService`) |
| Orden D10 | 5 |
| Dependencia | TR-006 Finalizado |
| C1 | [c1-20260905-TR-007.md](../../08-control/c1-20260905-TR-007.md) — Apto; Q1–Q9 |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| Q1 | Fallback SQL con `agent_id` | Prohibido; `offline`/`timeout` → `AGENT_OFFLINE` |
| Q2 | Parámetros job | Solo `{ codigo }`; password en Laravel via `password_hash` |
| Q3 | `data` success | `status=OK`, `user.password_hash`, `es_admin`, `redirectTo`, `empresas` |
| Q4 | Agente | Handler `auth.login` + whitelist con `diagnostics.run` |
| Q5 | TANGO | `AuthService` + JobResult `errorCode`; sin fallback |
| Q6 | SP | `PAQ_Auth_Login` (reutilizar / embeber); no migraciones masivas |
| Q7 | Prueba | Lab primero; AWS opcional |
| Q8 | Timeout | 30 s |
| Q9 | Prohibidos | Tailscale, SQL libre, `dev-agent-token` |

### Tareas

**Este repo**

- [ ] Handler `auth.login` (multi result set → `data` Q3) + lista blanca `diagnostics.run` \| `auth.login`.
- [ ] Script/migración SP `PAQ_Auth_Login` (reutilizar existente; ColRolPK si aplica). Sin migraciones masivas.
- [ ] Otras operations → `OPERATION_NOT_ALLOWED`.
- [ ] Tests: no listada rechazada; login feliz con SQL lab (o mock SP).
- [ ] Lab: documentar en Traza / lab-local.

**TANGO**

- [ ] `AuthService`: `sendJob(auth.login, {codigo})` + `traceId`; mapear `errorCode`/`errorMessage`.
- [ ] Quitar fallback SQL cuando `agent_id` presente (excepción, offline, timeout, status raro).
- [ ] Tests unitarios camino Gateway (success / failed / offline sin SQL directo).
- [ ] Sin Tailscale; sin SQL libre desde AWS.

**Ops / e2e**

- [ ] Lab: Gateway + Agente + SQL con SP + login tenant modo agente.
- [ ] (Opcional) AWS.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Laravel en `PaqSuite-IA-TANGO`. C1 Q1–Q9. |
| Pendientes | D1 → D |
