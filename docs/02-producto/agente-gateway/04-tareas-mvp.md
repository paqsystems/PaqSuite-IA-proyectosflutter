# 04 — Tareas técnicas del MVP (TR)

Origen: [03-historias-usuario.md](03-historias-usuario.md).  
Cada TR se ejecuta con [06-prompt-ejecutar-hu.md](06-prompt-ejecutar-hu.md).  
No se inventan features. Si falta algo, se vuelve al SPEC.

Orden de ejecución = columna “Orden” del mapa (no el número de TR). El trabajo Laravel (TR-001, TR-007, TR-008) ocurre en `PaqSuite-IA-TANGO`.

---

## Mapa HU → TR (orden de ejecución)

| Orden | HU | TR | Repo |
|------:|----|----|------|
| 1 | HU-001 | TR-001 Alta `empresas_conexion` modo agente | TANGO |
| 2 | HU-002 | TR-002 PaqGateway (código) + TR-003 Deploy AWS | Este / infra |
| 3 | HU-004 | TR-005 PaqAgent conexión + heartbeat (config manual OK) | Este |
| 4 | HU-005 | TR-006 Job `diagnostics.run` e2e | Este + TANGO |
| 5 | HU-006 | TR-007 Operación piloto live | Este + TANGO |
| 6 | HU-007 | TR-008 Corte duro modo agente (legacy SQL directo sigue) | TANGO |
| 7 | HU-003 | TR-004 Auto-instalador | Este |
| 8 | HU-008 | TR-009 Documentación de instalación | Este |

Contratos compartidos (`PaqContracts`) se crean en TR-002 y los consume TR-005.  
Lab: TR-005 puede usar `appsettings.local.json` manual (D10). El instalador (TR-004) viene **después** de la vertical.

---

## TR-001 — Alta modo agente (Laravel)

**Repo:** `PaqSuite-IA-TANGO`  
**HU:** HU-001

### Tareas

- [ ] Migración: `host` y `port` nullable; `agent_id` / `client_id` usables como camino principal; espacio para `last_seen_at` / `last_seen_ip` (auditoría).
- [ ] Alta (UI o comando artisan documentado) genera `agentId`, `clientId`, token; no pide IP; 1 agente por tenant.
- [ ] Persistencia del token (hash o cifrado). Mostrar token una vez.
- [ ] Validación: modo agente = `agent_id` + token; `host` no required.
- [ ] Tests: alta sin host es válida; alta agente sin token es inválida.

### Traza (completar al ejecutar)

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-002 — PaqGateway (aplicación)

**Repo:** este  
**HU:** HU-002 (código; el deploy es TR-003)

### Tareas

- [ ] Proyecto ASP.NET Core .NET 8, hub `/agent-hub`.
- [ ] `PaqContracts`: job (con `traceId`), result, identity, heartbeat, estados (`success|failed|timeout|offline|degraded|cancelled`), errores (`AGENT_OFFLINE`, `AGENT_TIMEOUT`, …).
- [ ] Registro en memoria `agentId → connectionId` **más** `last_seen_at`; online = dentro de TTL (D16).
- [ ] `POST /internal/jobs/send`, `GET /internal/agents/{agentId}/status`, API key.
- [ ] Autenticación de agentes contra Laravel (cache corta de token).
- [ ] Timeouts, correlación `jobId` + `traceId`, logs sin secretos.
- [ ] Al shutdown/restart: jobs en vuelo → `cancelled` (sin reentrega silenciosa).
- [ ] `launchSettings`: HTTP local `127.0.0.1:5100` para dev. No Tailscale.
- [ ] Test: job a agente mock / test host; rechazo sin API key; online/offline por TTL.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-003 — Deploy Gateway en AWS

**Repo:** este (`docs` + artefactos de publish) + cuenta AWS  
**HU:** HU-002

### Tareas

- [ ] Publicar `dotnet publish` Release.
- [ ] EC2 (o equivalente) misma VPC que Laravel. Security Group según SPEC.
- [ ] systemd + Nginx/ALB + certificado + DNS `gateway.paqsuite.com`.
- [ ] Env: `Gateway__InternalApiKey`, `LaravelApi__BaseUrl` (URL **privada** de Laravel), `LaravelApi__InternalApiKey`.
- [ ] Verificar WSS desde una máquina **fuera** de Tailscale (salida 443 a Internet).
- [ ] Documentar el procedimiento en el mismo PR que deja el servicio up (cierra con TR-009, borrador aquí).

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-004 — Auto-instalador

**Repo:** este  
**HU:** HU-003

### Tareas

- [ ] WinForms .NET 8: campos del SPEC sección 5, **AgentToken obligatorio**.
- [ ] Default Gateway URL de producción, editable.
- [ ] Probar SQL; bloquear instalación si falla o si falta identidad (sin crear servicio).
- [ ] Probar salida al Gateway; si falla, abortar sin servicio (D14). Checkbox override “Instalar de todos modos”, default off.
- [ ] Escribir `appsettings.local.json`; crear servicio `PaqAgent` auto-start **solo** tras SQL OK y (gateway OK o override).
- [ ] Empaquetar zip de release (exe + SNI nativo si aplica). Sin token de GitHub embebido.
- [ ] Publicar **SHA256** del asset en notas de release / `SHA256SUMS` (D9).
- [ ] Test manual documentado en la TR (máquinas Windows); automatizar lo automatizable (validación de campos).

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-005 — PaqAgent (servicio)

**Repo:** este  
**HU:** HU-004

### Tareas

- [ ] Worker Service .NET 8 instalable como Windows Service.
- [ ] SignalR client, Bearer, `RegisterAgent`, heartbeat (actualiza last_seen), reconexión Polly.
- [ ] Lee **solo** `appsettings.local.json` + appsettings base sin secretos de producción.
- [ ] Lab (D10): documentar plantilla mínima de `appsettings.local.json` para prueba sin instalador.
- [ ] Readiness interno: network_ok → gateway_authenticated → sql_connection_ok → schema_ready → operational.
- [ ] Logs archivo (conexión, jobs, errores, readiness); no loguear token/password.
- [ ] Identidad: machineName, sqlServerName, version.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-006 — diagnostics.run e2e

**Repos:** este + TANGO (`AgentGatewayClient`)  
**HU:** HU-005

### Tareas

- [ ] Operación interna `diagnostics.run` en el agente (SQL ping + versión + readiness).
- [ ] Laravel: método `sendJob` / `runDiagnostics` contra el Gateway interno (con `traceId`).
- [ ] Prueba real: AWS Laravel → AWS Gateway → agente en un Windows con SQL, **sin Tailscale**.
- [ ] Logs de duración (tramos Laravel→GW→agente→SQL→retorno) suficientes para no adivinar.
- [ ] Caso degraded documentado (SQL down con agente autenticado).

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-007 — Operación piloto

**Repos:** este + TANGO  
**HU:** HU-006

### Tareas

- [ ] Handler específico `auth.login` (multi result set) + registro en lista blanca.
- [ ] Migración/script SQL del SP piloto `PAQ_Auth_Login` (reutilizar archivo existente si ya está bien). No migraciones masivas.
- [ ] Laravel llama `auth.login` vía Gateway; normaliza JSON camelCase; propaga `traceId`.
- [ ] Lista blanca: todo lo demás `OPERATION_NOT_ALLOWED` (incl. `clientes.buscar` hasta fase 2).
- [ ] Test: operación no listada rechazada; login feliz con SQL de laboratorio.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-008 — Corte duro modo agente (legacy SQL directo permanece)

**Repo:** TANGO  
**HU:** HU-007

### Tareas

- [ ] Selector: si hay `agent_id` → **solo** Gateway. Offline → error 503 `AGENT_OFFLINE`. Nunca SQL por `host`.
- [ ] Si **no** hay `agent_id` → camino SQL directo legacy **sigue permitido** en MVP (transición hasta transformación total).
- [ ] Quitar o no usar `host` en resolución de consultas live **cuando** hay `agent_id`.
- [ ] Test que falle si se reintroduce fallback SQL para un tenant con `agent_id`.
- [ ] Test (o caso documentado) de tenant sin `agent_id` que aún usa SQL directo.
- [ ] Grep de control: ningún servicio de negocio nuevo mezcla “agente offline → SQL directo”.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## TR-009 — Documentación de instalación

**Repo:** este  
**HU:** HU-008

### Tareas

- [ ] `docs/instalacion.md` (o actualizar instructivo) con descarga, prerrequisitos, campos, AWS, “qué no hacer”.
- [ ] README del repo alineado al SPEC (nada de “Gateway pendiente de implementar” si ya está).
- [ ] Checklist de alta de cliente: 10 pasos, copiable.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |

---

## Definición de hecho de cada TR

Una TR no se cierra si falta alguno:

1. Criterios de la HU cubiertos.
2. Tests acordados en la TR (o prueba e2e documentada cuando es infra).
3. Sin Tailscale en el camino feliz.
4. Sin secretos en logs ni en git.
5. Sección Traza completada.
