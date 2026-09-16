# Insumo para SPEC — objetos SQL en modo agente (Fase 3 / frente A)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-13 |
| Estado | Insumo — **no** es SPEC. No generar HU/TR ni código desde este archivo sin Paso A |
| Destino | Formalizar como parte de **SPEC-AGW-002** (frente A: paquete SQL). Mismo identificador que el frente C (binario); el Paso A decide un SPEC o slices internos |
| Relato funcional | [../circuito-objetos-sql-agente-funcional.md](../circuito-objetos-sql-agente-funcional.md) |
| Circuito hermano | [../circuito-actualizacion-agente-funcional.md](../circuito-actualizacion-agente-funcional.md) · [insumo-spec-agw-002-update-agente.md](insumo-spec-agw-002-update-agente.md) |
| Dependencia | [SPEC-AGW-001](../SPEC-AGW-001-producto.md) (Fase 1 / caño) |
| Placeholder actual | [SPEC-AGW-002-ciclo-sql-y-updates.md](SPEC-AGW-002-ciclo-sql-y-updates.md) |
| Análisis previo | [plan-ciclo-sql-y-updates.md](plan-ciclo-sql-y-updates.md) (tres frentes; este insumo **cierra dirección** del frente A y lo **engancha** al C) |
| Norma alineada | Framework GEN-18 (`SPEC-001-18`, runbook install/update, `seedEmpresaNueva`) y GEN-24 (`24-asignacion-modulos-clientes`) |
| Mapa | [../fases-roadmap.md](../fases-roadmap.md) |

Uso: fuente de `prompts/a-spec-desde-contexto.md` (Paso A) **junto** al insumo de update del binario. El SPEC resultante nace **Pendiente**, con preguntas abiertas sin inventar. Contrato Laravel se especifica en el SPEC y se implementa en `PaqSuite-IA-TANGO`. Este repo: agente, instalador, runner SQL, scripts embebidos.

**Especificar juntos** con el frente C (oleadas, handshake, D9, staff, Forge). **Implementar por slices** (el Paso A ordena): no hace falta codear el runner completo en el mismo drop que el handshake, pero el contrato del instalador/oleada **debe** reservar el paquete SQL para no pintarse un rincón.

---

## 1. Problema que el SPEC debe cubrir

En modo agente (`agent_id` + `client_id`) Laravel **no tiene** connection string al SQL del tenant. GEN-18 asume install/update en el **host** cuando hay SQL directo; en gateway solo documenta “runtime = solo SP”. El hueco: **quién aplica DDL, seeds y SP** en diccionario y empresas.

Hoy:

- scripts `PAQ_*` viven en `src/PaqAgent/Sql/` y se aplican **a mano** (SSMS);
- tablas/seeds PQ de Framework se piensan como migrate Laravel (rompe en gateway puro);
- el circuito de oleadas del binario dejaba SQL “para después”.

Sin este frente, una oleada nueva deja el servicio en línea y las pantallas rotas (`schema_ready` eterno o `OPERATION_NOT_ALLOWED` / error de SP inexistente).

---

## 2. Solución (dirección acordada 2026-09-13)

1. **Mismo artefacto** `PaqAgentSetup.exe` y **misma oleada**: payload binario + **paquete SQL** versionado (`schemaVersion` alineado a `agentVersion`).
2. **Quién aplica en modo agente:** instalador y/o `SqlMigrationRunner` del agente **en el cliente**. Forge/Laravel **no** hacen `migrate` / `CREATE PROCEDURE` sobre el SQL del tenant modo agente.
3. **Install ≠ update** (GEN-18): comandos/modos separados; update **no** re-seed de users ni empresa inicial; params solo caption/tooltip (`valor_*` intactos).
4. **MULTI vs MONO** según la tabla del relato (§5 del texto funcional). MONO no tiene alta de empresas.
5. **Alta empresa MULTI:** no es oleada. Job runtime `schema.seedEmpresa` (nombre final en SPEC) = contrato `seedEmpresaNueva`. BD física preexistente. Idempotente. No toca users/rol globales.
6. Handshake: el host no envía jobs de negocio si el agente no declara esquema listo para esa oleada/op (además de capacidades de binario).
7. Clientes **sin** `agent_id`: GEN-18 en el host (Artisan) sigue vigente. Dual-path de **aplicación** de esquema; mismos contratos de SP.
8. GEN-24: matriz solo en PAQSYSTEMS **dev** → seeders. En modo agente esos artefactos **se empaquetan en el exe**. Sin ABM de matriz en el SQL del cliente. Para Tango modo agente: supuesto de trabajo = **producto completo**; overlay por contrato recortado = pregunta abierta.

Self-update del binario y apply SQL son **runtime**, no ops de la whitelist de SP de negocio. El apply SQL **sí** ejecuta T-SQL de paquete (excepción GEN-18: DDL / deploy de SP).

---

## 3. Decisiones ya cerradas (el SPEC no las reabre)

| Id | Decisión |
|----|----------|
| S1 | Modo agente: aplica el cliente (instalador/runner). Forge no migra SQL remoto del tenant |
| S2 | Mismo `PaqAgentSetup.exe` que el frente C; no segundo instalador “de BD” |
| S3 | Oleada confirma **binario + esquema**; no botón staff “solo SQL” en v1 |
| S4 | Install (fail-if-exists) ≠ update (idempotente, sin re-seed users) |
| S5 | MULTI install: diccionario + **una** empresa inicial en `PQ_EMPRESA` |
| S6 | MULTI update: diccionario + **todas las empresas habilitadas** en `PQ_EMPRESA`, secuencial |
| S7 | MULTI alta empresa: diccionario (solo lo de esa empresa) + esa BD; job al agente; no wizard |
| S8 | MONO install: insertar seguridad/config + negocio en la única BD; MONO update: insert/update; sin alta empresa |
| S9 | Diccionario fail-fast; empresas secuencial; abort + reportar la empresa fallida (GEN-18 D1-U1) |
| S10 | `pq_parametros_gral` en update: solo caption/tooltip; nunca `valor_*` |
| S11 | Framework GEN siempre completo en el paquete; Tango agente = producto completo (supuesto de trabajo) |
| S12 | SP pueden instalarse aunque el módulo no esté contratado; menús/params siguen contrato GEN-24 |
| S13 | Upgrade **no** pide de nuevo usuario SQL ni pisa `appsettings.local.json` |
| S14 | El runner de esquema **no** es un job de la whitelist de SP de negocio |
| S15 | Sin Tailscale; sin fallback SQL en modo agente; sin `dev-agent-token` |
| S16 | Oleada 0 de soporte incluye aplicar SQL pendiente al correr el exe nuevo |

Vigentes del frente C: C1–C11, C13–C15 (staff, broadcast, D9, quiet upgrade, etc.). S3 **reemplaza** el espíritu de C12 (“SQL fuera de este corte”): SQL entra al **mismo SPEC**; el corte de **código** puede seguir en slices.

---

## 4. Actores

| Actor | Rol en objetos SQL |
|-------|-------------------|
| Operador PaqSystems | Publica oleada (exe con paquete SQL); confirma envío; lee tablero (incluye schema status) |
| Administrador servidor cliente | Alta inicial: credenciales SQL en wizard. No aplica SSMS en el circuito objetivo |
| Usuario PaqSuite | No opera esquema. ABM Empresas (MULTI) dispara seed vía host → agente |
| TANGO (Laravel) | `desiredSchemaVersion`; handshake; job alta empresa; **no** migrate tenant agente |
| PaqGateway | Transporta job/hint; no interpreta T-SQL |
| PaqAgent | Runner install/update/seedEmpresa; heartbeat `schemaVersionApplied` + readiness |
| PaqAgentSetup | Tras conectividad: modo install aplica paquete install; modo upgrade llama runner update |
| Host **sin** agente | Artisan GEN-18 (fuera de este runner; dual-path) |

---

## 5. Alcance

### Entra (MUST en el SPEC a redactar)

- Formato del **paquete SQL** embebido: `dictionary/`, `company/`, seeds install vs update, orden, checksums, `schemaVersion`.
- Modo **install** (wizard, no quiet): diccionario + una empresa; fail-if-exists; users admin/PQ, Supervisor, permisos, menús, params, SP, tablas Framework.
- Modo **update** (quiet en upgrade de servicio): diccionario + empresas habilitadas; sin re-seed users; params solo meta.
- Job **alta empresa** MULTI (`schema.seedEmpresa`): contrato `seedEmpresaNueva`; BD preexistente; idempotente; rollback de fila a cargo del host.
- Heartbeat: `schemaVersionApplied` (gancho ya en `AgentHeartbeat`), readiness `schema_ready` / `degraded` / (nombre final en SPEC).
- Handshake host: no `sendJob` de negocio si esquema no listo para esa op/oleada.
- Manifest D9: además de `version`/`sha256`/`url`, `schemaVersion` (o un id de oleada que implique ambos).
- Catálogo AWS: desired de esquema por oleada + status por `agent_id` (applied / pending / failed / degraded).
- Política MULTI/MONO de la tabla §2/relato.
- Criterios de fallo: diccionario, empresa N de M, hash de script, permisos SQL insuficientes.
- Dual-path: documentar que sin `agent_id` el dueño sigue siendo GEN-18 en el host.
- Inventario de familias de objetos (no DDL fila a fila): seguridad, `pq_menus`, `pq_parametros_gral`, componentes Framework, users/rol/permisos, SP `PAQ_*` / `pq_sp_*`, tablas de trabajo de módulos.

### No entra (explícito en el SPEC)

- Que Forge `migrate` contra el SQL del tenant modo agente.
- Crear la BD física de una empresa nueva (ops/ERP).
- ABM de matriz `PQ_INSTALACION_*` en producción / en el cliente (GEN-24).
- Re-seed de passwords `admin`/`PQ` en cada oleada.
- SQL libre desde el agente en jobs de negocio (sigue lista blanca de ops → SP).
- Reabrir SPEC-AGW-001 / Tailscale / fallback.
- Pixel UI del ABM Empresas (GEN-06); este insumo solo el entrypoint de siembra vía agente.
- Vault de passwords SQL (sigue v1 en claro local como el JSON actual).

### Should / Could (el SPEC marca prioridad; no inventar si no está acá)

- SHOULD: usuario SQL de **runtime** con privilegio mínimo (`EXEC` de SP) distinto del usuario de **install/update** (DDL). Si no se cierra, el SPEC deja Q y no inventa cuentas.
- SHOULD: dry-run / listado de scripts pendientes en diagnostics.
- SHOULD: tablero staff muestra `schemaVersion` además de `agentVersion`.
- COULD: overlay GEN-24 por cliente (menús recortados) vía job `schema.applyContract` si Tango deja de ser producto completo.
- COULD: aplicar solo scripts cuyo checksum no esté en tabla de control local (journal de migraciones del agente).

---

## 6. Repos (el SPEC debe etiquetar cada recorte)

| Recorte | Repo |
|---------|------|
| Paquete SQL embebido, runner, journal, modos install/update/seedEmpresa | **este** (`PaqAgent`, `PaqAgentInstaller`) |
| Scripts `PAQ_*` ya en `src/PaqAgent/Sql/` | este (siguen; el runner los consume) |
| `PaqContracts`: heartbeat esquema, job seedEmpresa, constantes | este |
| Empaquetado `pack-installer.ps1` incluye carpeta SQL + checksums | este |
| `desiredSchemaVersion`, handshake pre-`sendJob`, job alta empresa, dual-path Artisan | **TANGO** |
| Familias PQ / SP Framework (origen de verdad GEN) | **FRAMEWORK** (`laravel-core` database/sp + runbook 18); el exe **embute** una copia versionada |
| Matriz / export seeders (GEN-24) | FRAMEWORK + ops PAQSYSTEMS **dev**; artefacto entra al pack de oleada |
| Dual-path / ops de negocio | TANGO + Framework según oleada; el runner no las interpreta |

---

## 7. Flujos que el SPEC debe cortar en CA

### F-S1 — Instalación nueva (wizard)

Administrador: landing D9 → exe modo **install** → identidad + prueba SQL + Gateway → **aplicar paquete SQL install** (diccionario + una empresa) → JSON local + servicio. Si SQL install falla: no dejar `operational`; error en wizard; no “online listo”.

### F-S2 — Oleada (upgrade quiet)

Mismo F5/F6 del insumo binario, **después** de reemplazar el programa y **antes** de declarar capacidades nuevas: runner modo **update**. Heartbeat con `agentVersion` + `schemaVersionApplied` + readiness. Hash mismatch del exe: no aplica SQL. Fallo SQL: status failed/degraded; JSON intacto; binario puede haber quedado nuevo (el SPEC nombra el estado).

### F-S3 — Catch-up offline

Al reconectar: desired oleada ≠ actual → self-update binario **y** SQL pendiente. Sin re-confirmar ese cliente.

### F-S4 — Alta empresa MULTI

Host ABM crea fila `pq_empresa` → job al agente con `empresaId` + datos de conexión de esa BD (o derivables de la config local + catálogo empresa) → `seedEmpresaNueva` → success/failed. BD ausente: abort sin escribir. Segunda ejecución: no duplica. Host hace rollback de fila si failed (GEN-18 AC-15..17).

### F-S5 — Handshake

Antes de `sendJob(operation)`: op ∈ capacidades **y** esquema listo (oleada/schema mínima). Si no: error de producto estable; sin fallback SQL.

### F-S6 — Tenant sin agente

No usa este runner. GEN-18 host. El SPEC lo cita para no mezclar dueños.

### F-S7 — Oleada 0

Runbook: exe nuevo (upgrade interactivo o quiet) aplica SQL pendiente. No botón staff “migrar SQL”.

---

## 8. Contratos (borrador para el SPEC; nombres finales en Paso A)

JSON / propiedades en **camelCase**.

### 8.1 Paquete SQL embebido

Mínimo conceptual:

- `schemaVersion`
- lista ordenada `dictionary[]` y `company[]` (path + sha256)
- seeds etiquetados `install` vs `update` (update no incluye users)
- opcional: mapa módulo → scripts (GEN-24)

Journal local (nombre de tabla/archivo = SPEC): scripts ya aplicados + checksum. Re-run update es seguro.

Scripts actuales en `src/PaqAgent/Sql/dictionary/` y `company/` son el **núcleo `PAQ_*`**; el SPEC exige **además** las familias PQ GEN (hoy no viajan en el exe). No inventar el dump DDL aquí: Paso A lista familias y apunta origen Framework.

### 8.2 Heartbeat (extender lo existente)

Ya: `agentId`, `clientId`, `agentVersion`, `schemaVersionApplied`, `readiness`, `timestampUtc`.

Agregar (nombre exacto en SPEC):

- detalle de esquema: applied vs desired (si el agente conoce desired);
- opcional: última empresa fallida / error code de runner;
- capacidades **no** se anuncian completas de la oleada nueva hasta `schema_ready` (dirección: S2 del relato).

### 8.3 Job `schema.seedEmpresa` (runtime, no whitelist SP de negocio)

Payload mínimo: `empresaId`, `traceId`, datos de destino de la BD empresa (el SPEC elige: connection derivados vs payload). Timeout propio (siembra > timeout de un SP de lectura).

Agentes sin handler: unsupported; ABM no deja empresa a medias.

### 8.4 Manifest D9

Campos del insumo binario **más** `schemaVersion` (o id de oleada único que el catálogo desglosa en ambas versiones). Prohibido broadcast si manifest no coincide con X.

### 8.5 Users seed (solo install)

Alinear GEN-18: `admin` / `Paqsystems`; `PQ` / `PaqSystems26*`; hash = hasher del host **no aplica** en SQL Tango clásico (tablas `users` / equivalente ERP). El SPEC **no inventa** el algoritmo de hash Tango: cita el que ya usa el producto / `PAQ_Auth_Login`. firstLogin **No**.

---

## 9. Criterios de aceptación (para desglosar en HU)

| CA | Verificable |
|----|-------------|
| CA-S01 | Install nuevo: diccionario + exactamente una `pq_empresa`; Supervisor; admin y PQ; permisos |
| CA-S02 | Re-ejecutar install sin `--force` → fail-if-exists; no duplica users/empresa |
| CA-S03 | Update no cambia passwords ni recrea empresa inicial |
| CA-S04 | Update MULTI recorre empresas habilitadas; no toca inhabilitadas |
| CA-S05 | Update: `valor_*` de un parámetro personalizado permanece; caption/tooltip pueden cambiar |
| CA-S06 | Fallo en diccionario: aborta; no recorre empresas; readiness ≠ schema_ready |
| CA-S07 | Fallo en empresa k: aborta el lote de empresas; reporta cuál; diccionario de esa pasada ya aplicado queda documentado |
| CA-S08 | Upgrade quiet no muestra wizard; no pide SQL de nuevo |
| CA-S09 | `seedEmpresaNueva` con BD inexistente: abort sin escribir |
| CA-S10 | Segunda `seedEmpresaNueva` no duplica objetos |
| CA-S11 | Host modo agente no llama migrate/SQL directo para estas familias |
| CA-S12 | Handshake: op de oleada nueva no se envía si schema no listo |
| CA-S13 | MONO: una sola BD recibe install/update; no existe flujo alta empresa |
| CA-S14 | Paquete de la oleada X incluye scripts `PAQ_*` de esa oleada (idempotentes) |
| CA-S15 | Tenant sin agent_id: este runner no es el camino (doc + no-op) |

---

## 10. Riesgos (el SPEC los nombra; no los “resuelve” inventando)

| Riesgo | Nota |
|--------|------|
| Permisos SQL del usuario del wizard | DDL vs runtime; Q-S1 |
| Binario nuevo + esquema viejo | No anunciar capacidades nuevas hasta schema_ready |
| Journal vs `CREATE OR ALTER` | Doble idempotencia; no aplicar a medias sin registro |
| Copiar ciego migrate Laravel | Tipos/Eloquent no corren en el runner; el paquete es T-SQL |
| Matriz distinta por cliente vs exe único | Tango = producto completo; overlay = Q-S3 |
| Passwords seed en docs | Ya públicos en GEN-18; rotación = fuera |
| Timeout siembra N empresas | Update de oleada ≠ timeout de job de clientes |
| Dual-path divergente | Mismos SP; dos aplicadores (host vs agente) |

---

## 11. Preguntas abiertas (llevar al SPEC; **no inventar** en Paso A)

| Id | Pregunta | Por qué bloquea o no |
|----|----------|----------------------|
| Q-S1 | ¿Mismo login SQL para wizard/DDL y para runtime `EXEC`? | Operativo; no inventar segunda cuenta |
| Q-S2 | Columna exacta de “habilitada” en `PQ_EMPRESA` | MULTI update; no inventar nombre |
| Q-S3 | ¿Tango agente siempre producto completo o hay overlay GEN-24 por `agent_id`? | Default de trabajo = completo; SPEC lo declara |
| Q-S4 | Origen embebido de DDL PQ GEN (export desde Framework vs scripts ya en este repo) | Pack; no inventar un dump |
| Q-S5 | Tabla/archivo journal de scripts aplicados | Implementación runner |
| Q-S6 | Payload de conexión de empresa nueva (local catalog vs campos en el job) | No filtrar secretos de más por Gateway |
| Q-S7 | Readiness strings canónicos (`schema_ready` / `degraded` / …) | Heartbeat ya tiene `readiness` libre |
| Q-S8 | ¿`--force` de install existe en el wizard o solo runbook soporte? | GEN-18 lo tiene en CLI host |
| Q-D9-1 | Host landing | Ya abierta en frente C |
| Q2 / Q8 | Capacidades vs oleada vs semver | Compartidas con insumo binario; el SPEC unifica **incluyendo** schemaVersion |

No reabrir: Tailscale, fallback SQL, migrate Forge al tenant agente, segundo exe de BD, re-seed users en update, ABM matriz en prod.

---

## 12. Slices sugeridos para Paso A (orden de SPEC/HU, no de código aún)

Unificar en SPEC-AGW-002 **ambos** frentes. Orden de valor (encaja con el insumo binario):

1. Handshake + oleadas en contrato (**versión de programa y de esquema**). Valor en lab con apply SQL todavía manual.
2. Árbol D9 + manifest (incluye `schemaVersion`).
3. Instalador dual (install vs upgrade quiet) **con fase SQL** (aunque el runner v1 solo aplique `PAQ_*` ya versionados).
4. Runner install/update completo (familias PQ GEN + seeds) + journal.
5. Catálogo desired + staff + artisan (tablero con esquema).
6. Self-update F5/F6 **que dispara update SQL**.
7. `schema.seedEmpresa` (MULTI).

El slice 1 puede ir a código **antes** que el 4; el SPEC igual describe el 4–7 para no contradecir S2/S3.

---

## 13. Trazabilidad que el SPEC debe citar

- SPEC-AGW-001 §4 / D5 — modo agente sin SQL desde AWS.
- Insumo frente C — oleadas, staff, D9, oleada 0.
- Framework: `docs/02-producto/18-instalacion-y-actualizacion.md`, `SPEC-001-18` §6.1–§9, `docs/06-operacion/runbook-install-update.md`, TR-GEN-18-seed (incl. TR-18-U1 `seedEmpresaNueva`), TR-GEN-18-actualizacion, `docs/02-producto/24-asignacion-modulos-clientes.md`.
- Override Framework `02-base-datos` — MUST SP; install/update despliegan SP.
- `AgentHeartbeat.schemaVersionApplied` ya en `PaqContracts`.
- Scripts actuales `src/PaqAgent/Sql/README.md`.
- Plan híbrido Fx/Dx: el **contenido** de cada oleada (D1, F5, …) alimenta el paquete SQL; este frente es el **aplicador**.

---

## 14. Qué debe hacer Paso A (checklist)

- Redactar SPEC canónico `docs/02-producto/SPEC-AGW-00X-….md` con Estado **Pendiente**, cubriendo **frente C + frente A** (o SPEC hijo con puntero inequívoco; **una** fuente de verdad).
- Copiar/puntero en `docs/05-open-spec/<epica>/`.
- In/out, actores, contratos, CA, riesgos, Q abiertas (tabla §11 de **ambos** insumos).
- Marcar repos este vs TANGO vs FRAMEWORK por recorte.
- Actualizar el placeholder SPEC-AGW-002.
- No generar HU (Paso B) ni código.
- No inventar host, columna `PQ_EMPRESA`, ni segunda cuenta SQL si Q-S1/Q-S2 siguen abiertas.

Tras A → A1. No implementar hasta C1 apto del recorte correspondiente.
