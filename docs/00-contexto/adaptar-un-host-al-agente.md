# Cómo adaptar un host (Tango u otro) al agente-gateway

| Campo | Valor |
|-------|--------|
| Para | Quien tenga que entender el caño sin leer TR |
| No sustituye | [SPEC-AGW-001](../02-producto/SPEC-AGW-001-producto.md), GEN-18 del Framework |
| Fecha | 2026-09-08 |

Este texto traduce el SPEC a lenguaje de trabajo. Si choca con el SPEC, **manda el SPEC**.

---

## Tres capas (no las mezcles)

| Capa | Repo | Qué hace | Qué no hace |
|------|------|----------|-------------|
| **Framework** (`PaqSuite-IA-FRAMEWORK`, paquete `laravel-core`) | Canal común: “¿este tenant es modo agente?”, cliente HTTP al Gateway, no abrir SQL por `host` si hay agente | Operaciones de un producto (login Tango, clientes GVA, informes) |
| **Host** (hoy TANGO; mañana cualquier app PaqSuite) | Dominio: login, menús, pedidos, etc. Decide *qué* job mandar y cómo mapear el JSON | Credenciales SQL del cliente; hablar SignalR |
| **Agente** (`PaqSuite-IA-AgenteCliente-PAQ`) | En el Windows del cliente: recibe el job, ejecuta un SP de lista blanca, devuelve JSON | Elegir el tenant; abrir SQL desde AWS |

El **Gateway** (AWS) solo rutea. No conoce Tango ni GVA14.

```text
Usuario → Vercel → Host Laravel
                      │
                      ├─ fila empresas_conexion SIN agent_id
                      │     → SQL directo (instalación en el cliente)
                      │
                      └─ fila CON agent_id + client_id
                            → Gateway → Agente → SQL Tango en la LAN
```

La fila de `empresas_conexion` es la única llave. El agente **no** decide el modo.

---

## 1. ¿Cada módulo del host obliga a tocar el agente?

**Sí, si ese módulo necesita datos del SQL del cliente.** El agente no acepta SQL libre. Solo nombres de operación en lista blanca. Hoy: `diagnostics.run` y `auth.login`. Cualquier otro nombre vuelve `OPERATION_NOT_ALLOWED`.

**No**, si el módulo es solo UI, validación Laravel, o lee el catálogo AWS (`empresas_conexion`).

Ejemplo: TANGO ya pide `clientes.buscar`. El host está adelantado; el agente todavía no. Por eso el menú/dashboard fallan aunque el login del agente funcione.

---

## 2. ¿Se actualiza “todo” el agente? ¿Y las credenciales?

Hay **tres paquetes distintos**. No viajan juntos.

| Qué cambió | Dónde | Cómo llega al cliente |
|------------|--------|------------------------|
| Nueva operación C# (handler) | Código del agente + constante en `PaqContracts` | Nuevo build del servicio Windows (y del instalador si se redistribuye el .exe) |
| Nuevo SP / script SQL | `src/PaqAgent/Sql/dictionary/` | SSMS (hoy). No hace falta reinstalar el .exe. El instalador no pisa esto. |
| AgentId, token, URL Gateway, usuario SQL | `appsettings.local.json` en el PC | Lo escribe el instalador **una vez**. Un update del binario **no lo pisa**. |

No se reescribe el hub ni el heartbeat por cada módulo. Se agrega: constante + runner + tests + (si aplica) script SQL.

Prohibido meter el token o la clave Axoft en el binario.

---

## 3. Un mismo host, con o sin agente

Sí. Dos filas distintas en `empresas_conexion`:

- **Modo agente:** `agent_id` y `client_id` (y token) cargados. Laravel **no** usa `host`/`port`/password SQL. Si el agente está caído → error claro (`AGENT_OFFLINE`), no “probar por IP”.
- **Sin agente (legacy / instalación directa):** esos campos vacíos. Laravel habla al SQL del cliente como siempre (GEN-18).

El login de TANGO ya ramifica así (`shouldUseGateway`). Los services de negocio del host deben ser **dual-path**: SQL si no hay agente; solo Gateway si hay `agent_id`. Inventario y estado: repo TANGO `docs/08-control/nota-20260908-dual-path-services.md` y `docs/06-operacion/modo-agente-gateway.md`.

---

## 4. Checklist: un módulo nuevo (por ejemplo “buscar clientes”)

Orden fijo. No invertir.

1. **Host, dual-path:** si modo agente → `sendJob('clientes.buscar', …)`; si no → el SQL/SP que ya usaba el producto.
2. **Contrato JSON** del job (parámetros y `data` de success/failed). Lo define el host; el agente lo cumple.
3. **Agente:** constante en `JobOperations`, handler, tests. Rebuild del servicio.
4. **SQL del cliente:** script `PAQ_*` versionado, aplicado en el diccionario (SSMS hoy).
5. **No tocar** `appsettings.local.json` en el update.
6. Prohibido: SQL libre desde el agente; fallback a `host` cuando hay `agent_id`.

---

## 5. Login (caño piloto) vs el resto del producto

El piloto de conectividad es **entrar con usuario Tango** (`auth.login`). Eso no incluye menú, dashboard ni GVA.

Después del job OK, el host **no puede** hacer `SELECT` a `USERS` ni guardar tokens Sanctum en el diccionario remoto: esa base no está en AWS. La sesión en modo agente sale del **payload del job** + caché Bearer (`agw.…`). Lab 2026-09-08 (`lenovo`): login UI OK. Es provisorio hasta hidratación completa en Framework; el prefijo ya está en `GatewaySessionToken` (laravel-core 1.3.7).

Si el cache de sesión se pierde, hay que volver a loguear. `auth.session` en el agente **no** está en la lista blanca todavía.

---

## 6. Framework y Tango (hacia dónde)

**Framework (`laravel-core`)** es dueño del canal:

- `InstalacionRecord::isGatewayMode()` / `AgentGatewayMode::shouldUseGateway()`
- no reapuntar SQL en modo agente
- `AgentGatewayClient` + `GatewaySessionToken` (`agw.`)
- Doc: `docs/10-overrides-framework/07-agente-gateway-canal.md`

**Tango** debe:

- consumir el paquete (`^1.3.7`; thin wrappers `App\Services\Agents\*`)
- quedarse con operaciones **Tango** + SPs en el agente
- dual-path en cada service

Publicar `laravel-core` en Satis cierra el path-repo local (por corte deployable; lab usa path-repo). Plan de integración **100%** (F0–F9 plataforma + D1–D6 dominio): [plan-20260908-integracion-tango-framework-agente.md](../08-control/plan-20260908-integracion-tango-framework-agente.md).

---

## 7. Post-login: ops y plataforma pendientes

Tras login OK, el siguiente muro de lab es **company/sesión** (F2 del plan: `ValidateCompanyId` → 403 sin dictionary). Después: menú (F3) y ops de dominio (D1…). El agente MVP solo tiene `diagnostics.run` y `auth.login` → `OPERATION_NOT_ALLOWED` hasta implementar cada op + SP.

---

## Relacionado

- [MANUAL-DEL-PROGRAMADOR.md](MANUAL-DEL-PROGRAMADOR.md) — cómo está hecho el caño y el alta de cliente
- [Plan integración 100%](../08-control/plan-20260908-integracion-tango-framework-agente.md) — Framework → Tango → Agente por fases
- [Arranque SDD este repo](../08-control/sdd-arranque-integracion-agente-plan-100.md) — qué procesar acá (ops D1–D6 / plataforma SQL)
- Framework arranque SDD: `PaqSuite-IA-FRAMEWORK/docs/08-control/sdd-arranque-integracion-agente-plan-100.md`
- Tango arranque SDD: `PaqSuite-IA-TANGO/docs/08-control/sdd-arranque-integracion-agente-plan-100.md`
- [SPEC-AGW-001](../02-producto/SPEC-AGW-001-producto.md) §4 `empresas_conexion`
- Framework: `docs/10-overrides-framework/07-agente-gateway-canal.md`
- TANGO: `docs/06-operacion/modo-agente-gateway.md`
