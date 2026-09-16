# Insumo para SPEC — actualización del agente (Fase 2 / frente C)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-13 |
| Estado | Insumo — **no** es SPEC. No generar HU/TR ni código desde este archivo sin Paso A |
| Destino | Formalizar como parte de **SPEC-AGW-002** (frente C: binario). Handshake/oleadas pueden ser slice interno; **mismo** SPEC que el frente A |
| Relato funcional | [../circuito-actualizacion-agente-funcional.md](../circuito-actualizacion-agente-funcional.md) |
| Insumo SQL (hermano) | [insumo-spec-agw-002-objetos-sql.md](insumo-spec-agw-002-objetos-sql.md) |
| Dependencia | [SPEC-AGW-001](../SPEC-AGW-001-producto.md) (Fase 1 / caño) |
| Placeholder actual | [SPEC-AGW-002-ciclo-sql-y-updates.md](SPEC-AGW-002-ciclo-sql-y-updates.md) |
| Análisis previo | [plan-ciclo-sql-y-updates.md](plan-ciclo-sql-y-updates.md) (tres frentes; este insumo = frente C + handshake; SQL = insumo hermano) |
| Mapa | [../fases-roadmap.md](../fases-roadmap.md) |

Uso: fuente de `prompts/a-spec-desde-contexto.md` (Paso A). El SPEC resultante nace **Pendiente**, con preguntas abiertas sin inventar. Contrato Laravel se especifica en el SPEC y se implementa en `PaqSuite-IA-TANGO`. Este repo: agente, gateway, instalador.

**Especificar junto** al frente A (objetos SQL): [insumo-spec-agw-002-objetos-sql.md](insumo-spec-agw-002-objetos-sql.md). Mismo SPEC-AGW-002. El corte de **código** puede ir por slices; el contrato de oleada/instalador **reserva** el paquete SQL (S2/S3 del insumo SQL). No dejar el placeholder “SQL después” como fuente de verdad.

---

## 1. Problema que el SPEC debe cubrir

En modo agente, Laravel no tiene connection string al SQL del tenant. Las operaciones de producto son nombres de lista blanca ejecutados por PaqAgent en el Windows del cliente.

Hoy, agregar o cambiar una API que el agente debe ejecutar implica un binario nuevo en **cada** servidor. El MVP no tiene canal de producto para:

- saber qué versión y qué capacidades tiene cada agente;
- impedir que el host mande una op que ese agente no soporta;
- publicar un instalador versionado en el árbol D9;
- disparar, con confirmación humana, la actualización de todos los tenants con `agent_id`;
- actualizar sin pisar `appsettings.local.json`.

El procedimiento manual de soporte no escala.

---

## 2. Solución (dirección acordada 2026-09-13)

1. Las APIs entran por **oleadas** (drop versionado), no de a una pantalla.
2. El agente declara **capacidades** (handshake). El host no envía jobs de ops no declaradas; error de producto, no solo `OPERATION_NOT_ALLOWED` opaco.
3. Un único artefacto `PaqAgentSetup.exe` y el **mismo árbol D9** para instalación nueva y para update.
4. El instalador detecta instalación existente → modo **upgrade** (silencioso cuando lo lanza el servicio).
5. Consola **staff en TANGO**, fuera del menú de usuarios, con confirmación. Regla de producto: **broadcast total** a filas `empresas_conexion` con `agent_id`. En pantalla interactiva: **filtro por cliente**.
6. Disparador alternativo: **comando / recipe Forge** (broadcast total; no es cada Deploy de TANGO).
7. Catálogo en AWS: `desiredAgentVersion`. Online: aviso inmediato. Offline: al reconectar, sin re-confirmar ese cliente.
8. **Oleada 0**: procedimiento de soporte (el binario actual no entiende self-update). No es botón de producto.
9. Self-update es **runtime** del agente, no una op de negocio de la whitelist de SP.

---

## 3. Decisiones ya cerradas (el SPEC no las reabre)

| Id | Decisión |
|----|----------|
| C1 | Staff en TANGO, no app Laravel aparte, no ítem de menú de usuario |
| C2 | Broadcast total = regla de producto |
| C3 | Filtro por cliente solo en la pantalla interactiva |
| C4 | Forge Command/Recipe dispara el mismo motor; Forge = siempre total |
| C5 | El Deploy Script cotidiano de TANGO **no** actualiza agentes, salvo opt-in explícito de ese release (`AGENT_OLEADA` o equivalente) |
| C6 | Árbol de descarga = D9 (landing pública, sin login, sin token en la URL) |
| C7 | Mismo exe para alta y update; el exe reconoce si ya está instalado |
| C8 | Upgrade **no** pisa `appsettings.local.json`; no vuelve a pedir token ni SQL |
| C9 | Upgrade disparado por el servicio es **silencioso** (sesión 0: no wizard) |
| C10 | Oleada 0 = soporte, una vez por instalación vieja |
| C11 | Agentes offline no abortan el lote; quedan pending hasta heartbeat |
| C12 | Frente A (objetos SQL) **entra al mismo SPEC**; ver insumo SQL. Implementación por slices; oleada = binario + esquema |
| C13 | Sin Tailscale; sin fallback SQL en modo agente; sin `dev-agent-token` |
| C14 | GitHub Releases no es canal de cliente (espejo CI/ops interno OK) |
| C15 | Authenticode / firma de código: no exigir en este corte (igual que D9 MVP) |

Vigentes y reutilizables de Fase 1: D9 (artefacto y canal), heartbeat `agentVersion` ya existe, un agente activo por tenant (D13), `appsettings.local.json` no se commitea.

---

## 4. Actores

| Actor | Rol en este circuito |
|-------|----------------------|
| Operador PaqSystems | Publica artefacto D9; confirma oleada (pantalla o Forge); lee tablero |
| Administrador del servidor cliente | Solo alta inicial y oleada 0 |
| Usuario PaqSuite | No opera el update |
| TANGO (Laravel) | Catálogo desired + status; pantalla staff; artisan; handshake antes de `sendJob` |
| PaqGateway | Transporta job/hint de update; no interpreta versiones de producto |
| PaqAgent | Declara versión + capacidades; aplica self-update |
| PaqAgentSetup | Install vs upgrade; payload embebido (D9) |

---

## 5. Alcance

### Entra (MUST en el SPEC a redactar)

- Modelo de **oleada** (identificador + `agentVersion` del artefacto + conjunto de ops/capacidades).
- Handshake: agente informa capacidades; host consulta antes de `sendJob`.
- Manifest de descarga (`version`, `sha256`, `url`) + landing D9 + servir exe (TANGO).
- Catálogo AWS: versión deseada (global de oleada) y estado **por** `agent_id`.
- Pantalla staff TANGO: listado, filtro cliente, confirmación, tablero (applied / pending-offline / failed).
- Artisan invocable desde Forge: misma operación, broadcast total, confirmación por flags.
- Job o método de runtime `agent.selfUpdate` (nombre final en SPEC) + catch-up por heartbeat si desired ≠ actual.
- Instalador: detección install vs upgrade; flags silenciosos de upgrade; no borrar JSON local.
- Oleada 0 documentada como runbook, no como HU de UI.
- Criterios de fallo parcial, timeout, hash mismatch, agente que aún no entiende self-update.

### No entra (explícito en el SPEC)

- Detalle MULTI/MONO, seeds PQ y `seedEmpresaNueva` (viven en el [insumo SQL](insumo-spec-agw-002-objetos-sql.md); este archivo no los duplica).
- Que Forge `migrate` contra el SQL del tenant modo agente.
- Auto-update sin confirmación humana de oleada (el agente no “mira GitHub solo”).
- Usuarios tenant disparando update.
- N agentes por tenant (sigue D13).
- Redis, escalado Gateway, Authenticode como MUST.
- Dispatcher genérico operación→SP (idea de diseño aparte; no acordada como parte de este circuito).
- Reabrir el caño de SPEC-AGW-001.

### Should / Could (el SPEC marca prioridad; no inventar si no está acá)

- SHOULD: pin versionado en URL `…/agente/{version}/PaqAgentSetup.exe` además de “vigente”.
- SHOULD: allowlist del host de descarga en el agente (no bajar de una URL arbitraria del payload sin coincidir con catálogo/D9).
- SHOULD: filtro cliente en pantalla incluye piloto lab (p. ej. lenovo) antes del primer total.
- COULD: opt-in `AGENT_OLEADA` al final de un Deploy TANGO que **exija** esa oleada.
- COULD: semáforo visible para soporte (no usuario final) “agente desactualizado”.

---

## 6. Repos (el SPEC debe etiquetar cada recorte)

| Recorte | Repo |
|---------|------|
| `PaqAgent`: heartbeat capacidades, self-update, catch-up | este |
| `PaqAgentInstaller`: detección + `/upgrade` `/quiet` | este |
| `PaqContracts`: DTO heartbeat, job update, constantes | este |
| `PaqGateway`: ruteo del job/hint; no lógica de oleada | este (mínimo) |
| Empaquetado `pack-installer.ps1` + SHA256 | este |
| Landing D9, manifest, storage exe, pantalla staff, artisan, catálogo, handshake pre-`sendJob` | **TANGO** |
| Dual-path / ops de negocio nuevas | TANGO + Framework según oleada; **fuera** salvo que el handshake las liste |

---

## 7. Flujos que el SPEC debe cortar en CA

### F1 — Instalación nueva (sin cambio de canal D9)

Administrador descarga landing pública → verifica SHA256 → exe modo **install** → JSON local + servicio. No usa pantalla staff ni artisan de oleada.

### F2 — Publicar oleada (antes de avisar)

Operador publica exe + `.sha256` + `manifest.json` alineados a versión X. El SPEC define el contrato del manifest (campos mínimos, camelCase en JSON de agente). Prohibido broadcast si el manifest no coincide con X.

### F3 — Disparo pantalla staff

Operador autenticado PaqSystems abre ruta staff (path exacto = pregunta abierta) → ve flota → opcional filtro `cliente` → confirma versión X → sistema marca desired y recorre `agent_id` (filtrados o todos).

### F4 — Disparo Forge

`php artisan … --version=X --confirm` (nombre final en SPEC). Sin `--confirm` o sin version: no-op. Siempre total. No vive en el Deploy Script por defecto.

### F5 — Agente online

Recibe aviso con `{ version, url, sha256 }` → baja manifest → valida version y hash → upgrade silencioso → heartbeat nuevo (`agentVersion` + capacidades). Resultado del job: success/failed.

### F6 — Agente offline

No falla el lote. Status `pending-offline`. Al heartbeat: si desired ≠ actual y el binario ya entiende self-update, aplica F5. Si no entiende: sigue oleada 0 (failed/unsupported, no loop infinito de jobs de negocio).

### F7 — Handshake en runtime de producto

Host, tenant con `agent_id`: si la op no está en capacidades del agente (o oleada mínima no cumplida) → error de producto estable (código a nombrar en SPEC). No fallback SQL.

### F8 — Oleada 0

Runbook: stop servicio, reemplazar binarios o correr exe upgrade interactivo, no borrar JSON, start, verificar online + handshake. Fuera de la pantalla staff como acción automática.

---

## 8. Contratos (borrador para el SPEC; nombres finales en Paso A)

JSON / propiedades en **camelCase**.

### 8.1 Manifest de descarga (público)

Mínimo:

- `version` (semver del artefacto)
- `sha256`
- `url` (HTTPS del exe; mismo árbol D9)
- `releasedAt` (opcional)

Landing humana: versión, fecha, SHA256, instructivo, botón. Q-D9-1 (host exacto) sigue abierto: el SPEC cita placeholder y no inventa host.

### 8.2 Catálogo AWS (no SQL del cliente)

- Versión deseada de la oleada vigente (global).
- Por `agent_id` (o por fila `empresas_conexion`): `agentVersion` reportada, capacidades/oleada reportada, `updateStatus`, `lastSeenAt` (ya hay presencia vía Gateway).

El SPEC decide persistencia (columnas vs tabla de oleadas). No inventar esquema aquí.

### 8.3 Heartbeat (extender `AgentHeartbeat` existente)

Ya: `agentId`, `clientId`, `agentVersion`, `schemaVersionApplied`, `readiness`, `timestampUtc`.

Agregar (nombre exacto en SPEC):

- capacidades: **lista de ops** y/o **id de oleada** (pregunta abierta Q2);
- opcional: ack de desired aplicado.

Hoy el heartbeat es fire-and-forget. El SPEC elige:

- **A.** Hub method / job `agent.selfUpdate` para online + comparación desired en el **agente** tras leer hint; o
- **B.** Heartbeat con respuesta (`desiredAgentVersion` + url + sha256) más job para “ahora”.

Acordado de producto: las dos capas (desired persistido + aviso inmediato). Detalle de wire protocol = Paso A / A1, no inventar si choca con SignalR actual.

### 8.4 Aviso de update (runtime, no whitelist SP)

Payload mínimo: `version`, `url`, `sha256`, `traceId`. El agente rechaza si `url` no pertenece al host D9 configurado (SHOULD). Timeout propio (update > timeout de un SP de negocio; el SPEC fija techo).

Agentes sin handler: `OPERATION_NOT_ALLOWED` o equivalente **unsupported**; status failed; no reintentar en caliente como si fuera op de clientes.

### 8.5 Handshake host

Antes de `sendJob(operation)`: operación ∈ capacidades del agente **o** oleada del agente ≥ oleada mínima de esa op. Si no: no llamar Gateway para esa op; respuesta de API de producto con código estable.

---

## 9. Criterios de aceptación (para desglosar en HU)

El SPEC debe poder marcar CA verificables. Borrador:

| CA | Verificable |
|----|-------------|
| CA-01 | Landing D9 sirve exe + sha256 + manifest; URL sin token |
| CA-02 | Alta nueva: exe en modo install; JSON creado; servicio Running |
| CA-03 | Upgrade: JSON intacto; servicio Running; `agentVersion` = X |
| CA-04 | Hash mismatch → no reemplaza binarios; status failed; JSON intacto |
| CA-05 | Staff: usuario tenant no accede; operador PaqSystems sí |
| CA-06 | Staff sin confirmar no marca desired ni avisa agentes |
| CA-07 | Staff con filtro un `cliente` no toca otros `agent_id` |
| CA-08 | Staff sin filtro (o “todos”) recorre todos los `agent_id` activos |
| CA-09 | Artisan sin `--confirm` no-op; con confirm + version = total |
| CA-10 | Deploy TANGO sin opt-in de oleada no avisa agentes |
| CA-11 | Offline queda pending; al reconectar (binario capaz) aplica X |
| CA-12 | Un failed no detiene el resto del lote |
| CA-13 | Host no envía op ausente en capacidades; sin SQL fallback |
| CA-14 | Wizard visible solo en install / upgrade interactivo; servicio usa quiet |
| CA-15 | Oleada 0 documentada; no aparece como acción automática en staff |

---

## 10. Riesgos (el SPEC los nombra; no los “resuelve” inventando)

| Riesgo | Nota |
|--------|------|
| Huevo y gallina | Self-update requiere oleada 0 |
| Sesión 0 | Upgrade con UI = fallo; MUST quiet |
| Acoplar Deploy PHP → Windows | Prohibido por defecto (C5) |
| Exe publicado ≠ version confirmada | MUST validar manifest vs X |
| Job de update en whitelist de SP | Prohibido; es runtime |
| URL arbitraria en el job | Allowlist D9 |
| Timeout corto de jobs de negocio | Update necesita techo propio |
| Capacidades stale | Heartbeat 30 s; host usa último reportado |

---

## 11. Preguntas abiertas (llevar al SPEC; **no inventar** en Paso A)

| Id | Pregunta | Por qué bloquea o no |
|----|----------|----------------------|
| Q-D9-1 | Host y path exactos de la landing | No bloquea el diseño; bloquea docs ops al publicar. Placeholder vigente `https://<host-tango>/descargas/agente` |
| Q1 | Path y guard exactos de la pantalla staff (rol/ability PaqSystems) | TANGO; no inventar nombre de rol |
| Q2 | Capacidades = lista de `supportedOperations` vs id de oleada vs ambos | Ambos cubren el relato; el SPEC elige. Lista es más precisa; oleada es más chica en heartbeat |
| Q3 | Persistencia del catálogo (columnas `empresas_conexion` vs tabla de oleadas/status) | Default H3 del MVP fue columnas; no asumir tabla nueva sin decidirlo |
| Q4 | Wire protocol: job dedicado vs heartbeat con respuesta vs ambos | Producto pidió ambos roles; A1 cierra el contrato SignalR |
| Q5 | Nombre de artisan y de operation runtime | camelCase en código; slug en docs |
| Q6 | Storage del exe (disco Forge del site TANGO vs S3 vs redirect) | D9 ya permite Forge storage / S3; elegir en TANGO |
| Q7 | ¿`activo=0` se incluye en el broadcast? | No acordado; SPEC debe decir solo agentes habilitados |
| Q8 | Semver y etiqueta de oleada (`1.4.0` vs `D1`) — una o dos dimensiones | Relato usa ambas; el SPEC unifica |
| Q9 | Techo de timeout y de tamaño del exe | Operativo; no inventar MB ni segundos en Paso A sin dato |

No reabrir: Tailscale, fallback SQL, token en la URL, zip de cara al cliente, segunda app Laravel, SQL masivo en este corte.

---

## 12. Slices sugeridos para Paso A (orden de SPEC/HU, no de código aún)

Paso A puede unificar en SPEC-AGW-002 frente C o partir épica `002-UpdateAgente`. Orden de valor:

1. **Handshake + oleadas en contrato** (heartbeat + host no manda ops de más). Entrega valor en lab **antes** de la pantalla Forge. Compatible con rebuild manual.
2. **Árbol D9 + manifest** (cierra publicación; Q-D9-1 puede seguir placeholder).
3. **Instalador dual** (install vs upgrade quiet). Habilita oleada 0 más segura y el self-update.
4. **Catálogo desired + staff TANGO + artisan Forge** (disparadores).
5. **Self-update en PaqAgent** (F5/F6) — dispara también el update SQL del paquete embebido.

SQL (frente A) = [insumo hermano](insumo-spec-agw-002-objetos-sql.md); **mismo** Paso A. Slices de código del runner pueden ir después del handshake; no reabrir “SQL fuera del SPEC”.

---

## 13. Trazabilidad que el SPEC debe citar

- SPEC-AGW-001 §5.1 / D9 — artefacto y canal público.
- `docs/06-operacion/urls-deploy.md` — placeholder URL.
- `docs/06-operacion/empaquetado-instalador.md` — pack.
- `docs/00-contexto/MANUAL-DEL-PROGRAMADOR.md` §5 (futuro update) y § D9.
- `AgentHeartbeat.agentVersion` ya en `PaqContracts`.
- Plan híbrido Fx/Dx: las oleadas de **producto** (D1, F5, …) son el contenido de cada drop; este SPEC es el **canal**, no el listado de ops.

---

## 14. Qué debe hacer Paso A (checklist)

- Redactar SPEC canónico `docs/02-producto/SPEC-AGW-00X-….md` con Estado **Pendiente**.
- Copiar/puntero en `docs/05-open-spec/<epica>/`.
- In/out, actores, contratos, CA, riesgos, Q abiertas (tabla §11).
- Marcar repos este vs TANGO por recorte (§6).
- Actualizar el placeholder SPEC-AGW-002: **un** SPEC con frentes C (binario) y A (SQL). No dejar dos fuentes de verdad ni “SQL después” como hueco.
- No generar HU (Paso B) ni código.
- No inventar host, rol Laravel, ni esquema SQL de catálogo si Q1/Q3 siguen abiertas: dejarlas en el SPEC.

Tras A → A1. No implementar hasta C1 apto del recorte correspondiente.
