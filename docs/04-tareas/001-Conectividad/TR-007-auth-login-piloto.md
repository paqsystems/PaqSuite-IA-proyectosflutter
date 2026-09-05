# TR-007 — Operación piloto (`auth.login`)

| Campo | Valor |
|-------|--------|
| TR | TR-007 |
| Estado | Pendiente de Revisión |
| HU | [HU-006](../../03-historias-usuario/001-Conectividad/HU-006-auth-login.md) |
| Repos | **este** (`PaqAgent`) + **TANGO** (`AuthService`) |
| Orden D10 | 5 |
| Dependencia | TR-006 Finalizado |
| C1 | [c1-20260905-TR-007.md](../../08-control/c1-20260905-TR-007.md) — Apto; Q1–Q9 |
| D1 | [d1-20260905-TR-007.md](../../08-control/d1-20260905-TR-007.md) — confirmado |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| Q1 | Fallback SQL con `agent_id` | Prohibido; `offline`/`timeout` → `AGENT_OFFLINE` (**TANGO**) |
| Q2 | Parámetros job | Solo `{ codigo }`; password en Laravel via `password_hash` |
| Q3 | `data` success | `status=OK`, `user.password_hash`, `es_admin`, `redirectTo`, `empresas` |
| Q4 | Agente | Handler `auth.login` + whitelist con `diagnostics.run` |
| Q5 | TANGO | `AuthService` + JobResult `errorCode`; sin fallback |
| Q6 | SP | `PAQ_Auth_Login` versionado en `src/PaqAgent/Sql/` |
| Q7 | Prueba | Lab primero; AWS opcional |
| Q8 | Timeout | 30 s |
| Q9 | Prohibidos | Tailscale, SQL libre, `dev-agent-token` |

### Tareas

**Este repo**

- [x] Handler `auth.login` (multi result set → `data` Q3) + lista blanca `diagnostics.run` \| `auth.login`.
- [x] Script/migración SP `PAQ_Auth_Login` (reutilizado del legado + ColRolPK). Sin migraciones masivas.
- [x] Otras operations → `OPERATION_NOT_ALLOWED`.
- [x] Tests: AuthLoginRunner (OK / NOT_FOUND / INVALID / sin SQL).
- [x] Lab: lab-local + evidencia SQL OK abajo.
- [x] `sql.encrypt` configurable (lab SSMS “Opcional” ⇒ `false`).

**TANGO**

- [x] `AuthService`: `sendJob(auth.login, {codigo})` + mapear `errorCode`/`errorMessage`.
- [x] Quitar fallback SQL cuando `agent_id` presente (excepción, offline, timeout, status raro).
- [x] Tests unitarios camino Gateway (`AuthServiceGatewayModeTest`).
- [x] Sin Tailscale; sin SQL libre desde AWS.

**Ops / e2e**

- [x] Lab REST sin SQL (degraded / INVALID / whitelist) — 2026-09-05.
- [x] Lab REST con SQL + SP + codigo `PQ` — 2026-09-05 (abajo).
- [ ] Lab TANGO→Gateway login e2e (opcional tras deploy keys).
- [ ] (Opcional) AWS.

### Traza (este repo)

| | |
|--|--|
| Archivos | `JobOperations.AuthLogin`; `PaqAgent/Auth/*`; `SqlConnectionStringFactory`; `Sql/dictionary/*`; `lab-local.md`; tests |
| Comandos | `dotnet test tests/PaqAgent.Tests` → **12 passed** |
| Notas | `encrypt=false` alineado a SSMS lab; diccionario `diccionario_000205_012` @ `192.168.41.2` |
| Pendientes | Humano: Finalizado (F1/F hechos). E2e login Laravel/AWS opcionales. |

### Traza (TANGO)

| | |
|--|--|
| Archivos | `AuthService.php` (sin fallback modo agente); `AuthServiceGatewayModeTest.php` |
| Comandos | `php vendor/bin/phpunit tests/Unit/Services/AuthServiceGatewayModeTest.php` |
| Notas | `ERROR_AGENT_OFFLINE`; mapeo `errorCode` top-level JobResult |

### Aclaraciones de lab (lo que suele confundir)

| Concepto | Qué es | Dónde va |
|----------|--------|----------|
| `sql.server` / `sql.user` / `sql.password` | Cuenta **SQL Server** (ej. `Axoft`) para que el agente abra el diccionario | `appsettings.local.json` → bloque `sql` |
| `sql.database` | Base **diccionario Tango** (ej. `diccionario_000205_012`), **no** la DB de Laravel | Idem |
| `parameters.codigo` en el job | Código de usuario Tango (`USERS.codigo`, ej. `PQ`) | Body del `Invoke-RestMethod` / Laravel login |
| `empresas_conexion` | Tabla de **Laravel/TANGO** (alta agente, token) | No la usa PaqAgent para el job SQL |
| `sql.encrypt` | Si SSMS conecta con cifrado **Opcional**, usar `"encrypt": false` | `appsettings.local.json` |
| `AGENT_OFFLINE` | Gateway arriba pero **PaqAgent no conectado** (o zombie que no reinició) | Reiniciar agente; matar `PaqAgent.exe` huérfano si el build falla por DLL lock |

Ruta del local: `src/PaqAgent/appsettings.local.json` (gitignored). Plantilla: `.example`.

### Prueba manual — lab (solo este repo)

Tres terminales Cursor (Gateway → PaqAgent → Invoke). Antes de jobs: agente debe loguear `Conectado al Gateway`.

```powershell
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }
```

**Evidencia 2026-09-05 — sin SQL**

| Caso | traceId | Resultado |
|------|---------|-----------|
| `clientes.buscar` | `01AUTHBAD` | `failed` / `OPERATION_NOT_ALLOWED` |
| `auth.login` + codigo | `01AUTHLAB` | `degraded` / `SQL_NOT_CONFIGURED` |
| `auth.login` sin codigo | `01AUTHNOC2` | `failed` / `INVALID_PARAMETERS` |

**Evidencia 2026-09-05 — con SQL + SP + `encrypt: false` (humano)**

| Caso | traceId | Resultado |
|------|---------|-----------|
| `clientes.buscar` | `01AUTHBAD` | `failed` / `OPERATION_NOT_ALLOWED` |
| `auth.login` `codigo=PQ` | `01AUTHOK` | `success`, `data.status=OK`, `es_admin=True`, `redirectTo=selector`, empresas presentes |
| `diagnostics.run` | `01DIAGOK` | `success`, `sqlConnectionOk=True`, `readiness=operational`, `sqlServerName=192.168.41.2` |

SP: [src/PaqAgent/Sql/README.md](../../../src/PaqAgent/Sql/README.md). Detalle operativo: [lab-local.md](../../06-operacion/lab-local.md) tramo 4b.
