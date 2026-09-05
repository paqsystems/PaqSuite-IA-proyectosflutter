# TR-001 — Alta modo agente (Laravel)



| Campo | Valor |

|-------|--------|

| TR | TR-001 |

| Estado | Finalizado |

| HU | [HU-001](../../03-historias-usuario/001-Conectividad/HU-001-alta-cliente-agente.md) |

| **Repo** | **`PaqSuite-IA-TANGO`** (no se scaffoldea ni implementa en este repo) |

| Orden D10 | 1 |

| C1 | [c1-20260904-TR-001.md](../../08-control/c1-20260904-TR-001.md) — observaciones **cerradas** abajo |



### Decisiones cerradas (post-C1, 2026-09-04)



No son dudas de producto abiertas: defaults de MVP para que D no invente.



| ID | Tema | Decisión |

|----|------|----------|

| M1 | Canal de alta | **MUST:** comando Artisan documentado (testeable). **SHOULD:** si ya existe UI/ABM de `empresas_conexion`, agregar modo agente ahí; **no** inventar pantalla nueva solo por el MVP. |

| M2 | Columna token | Columna en `empresas_conexion`: **`agent_token`** (texto/secreto **cifrado** con `APP_KEY`, mismo criterio que el `password` SQL legacy). No plaintext en BD. No tabla `agents`. |

| M3 | Formato ids | `client_id` = slug de `cliente` normalizado (ej. `Tec-Metal001` / `tecmetal` según convención existente del tenant). `agent_id` = `{client_id}-agent-01` (un agente por tenant, D13). Generados en el alta; no los inventa el operador a mano (pueden mostrarse). |

| M4 | `last_seen_at` / `last_seen_ip` | Columnas **nullable** en migración. TR-001 **no** las escribe en el alta. Autoridad online = Gateway (H4). |

| M5 | `activo` | Alta modo agente deja **`activo=true`**. |



### Tareas



- [x] Migración: `host` y `port` nullable; `agent_id` / `client_id` usables; columnas `agent_token` (cifrado), `last_seen_at` / `last_seen_ip` nullable (auditoría).

- [x] Alta (Artisan MUST; UI solo si ya hay ABM) genera `agent_id` / `client_id` según M3; pide `cliente` + `nombre`; no pide IP ni password SQL.

- [x] Alta deja `activo=true`, `host`/`port` null; muestra el token en claro **una sola vez** (no se vuelve a leer de BD).

- [x] Un solo agente activo por `cliente` (D13): si ya hay `agent_id`, rechazar o exigir flujo explícito de reemplazo documentado en Traza.

- [x] Validación: modo agente = `agent_id` + `agent_token`; `host` no required.

- [x] Tests: alta sin host es válida; alta agente sin token es inválida.

- [x] Sin Tailscale. Sin exigir IP pública. Sin `dev-agent-token`.



### Traza (completar al ejecutar en TANGO)



| | |

|--|--|

| Archivos | `backend/database/migrations/tenants_catalog/2026_09_04_180000_empresas_conexion_modo_agente.php`; `backend/app/Models/EmpresaConexion.php`; `backend/app/Services/EmpresasConexion/EmpresaConexionAltaAgenteService.php`; `backend/app/Console/Commands/EmpresasConexionAltaAgenteCommand.php`; tests Unit (`EmpresaConexionModoAgenteTest`, `EmpresaConexionAltaAgenteServiceTest`) + Feature (`EmpresasConexionAltaAgenteCommandTest`) |

| Comandos | `php artisan migrate --database=tenants_catalog --path=database/migrations/tenants_catalog --force`; `php artisan empresas-conexion:alta-agente {cliente} {nombre} [--force-replace]`; `php artisan test --filter="EmpresaConexionAltaAgenteServiceTest\|EmpresaConexionModoAgenteTest\|EmpresasConexionAltaAgenteCommandTest"` |

| Notas | Rama TANGO `FRAMEWORK`. Token cifrado con `Crypt::encryptString` (APP_KEY). Reemplazo: `--force-replace`. Sin UI nueva (no había ABM). Criterios HU-001: (1) Artisan pide cliente+nombre y genera ids+token; (2) sin host/port/password; (3) token una vez en consola; (4) activo=true, host null; (5) sin agent_id no es modo agente (`esModoAgenteValido`). |

| Pendientes | Commit/push en TANGO (no pedido). F1: [f1-20260904-TR-001.md](../../08-control/f1-20260904-TR-001.md) — Aprobado con observaciones. Sigue paso F. Sin UI ABM (M1 SHOULD). |

| Reemplazo | Si ya hay `agent_id`, el comando falla salvo `--force-replace` (regenera `agent_id`/`client_id`/token). |

