# 01 — SPEC de producto: PaqAgent + PaqGateway

| Campo | Valor |
|-------|--------|
| Identificador | SPEC-AGW-001 |
| Producto | PAQSuite IA — Agente local Tango |
| Versión | 1.1 |
| Fecha | 2026-09-03 |
| Estado | Cerrado para MVP (debates D1–D6 + aportes Codex incorporados) |
| Repos | `paqsuite-IA-AgenteCliente`, contrato en `PaqSuite-IA-TANGO` |

Este documento es la **fuente de verdad**. Si una HU, un TR o un agente de IA contradicen este SPEC, manda el SPEC. Si falta algo, se actualiza el SPEC **antes** de codear.

Vocabulario unificado (vs Codex / docs viejos):

| Concepto | Término canónico |
|----------|------------------|
| Tenant | Campo `cliente` + header `X-Paq-Cliente` |
| Habilitación | `activo` (MVP). `revoked_at` opcional en fase 2 |
| Protocolo Agent↔Gateway | **SignalR** sobre WSS/HTTPS 443 |
| IP observada | `last_seen_ip` solo auditoría; nunca ruteo |
| Piloto de negocio | `auth.login` (SP `PAQ_Auth_Login`) |
| SQL en MVP | Un SP piloto embebido OK; migraciones masivas fuera |

---

## 1. Problema

PaqSuite (Laravel en AWS) necesita datos vivos de Tango Gestión, que vive en SQL Server **dentro de la red de cada cliente**.

Abrir SQL Server a Internet es inaceptable. Pedir VPN/Tailscale por cliente no escala: IPs no fijas, fricción de soporte, latencia, dependencia de un overlay que no es el producto.

---

## 2. Solución

Invertir el sentido de la conexión.

```text
Usuario  →  Laravel (AWS)  --HTTP interno-->  PaqGateway (AWS)
                                              ▲
                                              │ WSS 443 saliente (SignalR)
                                              │
                                    PaqAgent (Windows Service
                                    en el servidor SQL del cliente)
                                              │
                                              ▼ LAN
                                    SQL Server Tango
```

- El **agente** se instala en el servidor donde está SQL Server (o en un Windows de la misma LAN que alcance SQL).
- El **gateway** se instala en Amazon, junto a Laravel (misma VPC).
- Laravel **nunca** abre un socket SQL hacia el cliente en **modo agente**.
- El cliente **nunca** abre puertos entrantes ni expone 1433.

---

## 3. Actores

| Actor | Rol |
|-------|-----|
| Operador PaqSystems | Da de alta el cliente en Laravel, genera `agentId` / `clientId` / `agentToken`, entrega el instalador |
| Administrador del servidor del cliente | Ejecuta el instalador, carga credenciales, deja el servicio corriendo |
| Usuario de PaqSuite | Usa la app con normalidad; no ve el agente |
| Laravel | Valida usuario/permisos, resuelve tenant (`cliente` / `X-Paq-Cliente`), manda job al Gateway por `agentId` |
| PaqGateway | Mantiene SignalR, rutea jobs, timeouts, online por heartbeat+TTL |
| PaqAgent | Autentica, heartbeat, ejecuta operación de lista blanca contra SQL local, reporta readiness, devuelve JSON |

---

## 4. `empresas_conexion` — qué guarda y qué no

Esta tabla (catálogo de tenants en AWS) es la **llave de ruteo**, no un connection string de SQL remoto.

MVP: **un agente activo por tenant** (ver D13).

### 4.1 Campos esenciales del modo agente (MVP)

| Campo | Obligatorio | Para qué |
|-------|-------------|----------|
| `cliente` | Sí | Identificador del tenant (subdominio / `X-Paq-Cliente`) |
| `nombre` | Sí | Nombre visible |
| `agent_id` | Sí (modo agente) | A qué conexión SignalR mandar el job |
| `client_id` | Sí (modo agente) | Identidad que presenta el agente |
| `activo` | Sí | Habilitar / suspender el tenant |
| token del agente | Sí | Hash o secreto en AWS; valor en claro una vez al instalador. Columna aquí o tabla `agents` 1:1 (D4/D13) |
| `last_seen_at` | Sí (runtime) | Último heartbeat; base del cálculo online + TTL |

### 4.2 Opcionales (no de ruteo)

| Campo | Obligatorio | Para qué |
|-------|-------------|----------|
| `last_seen_ip` | No | IP de salida observada del agente; **solo auditoría/soporte**. Jamás condición de conexión ni clave de resolución |
| `status` | No / derivado | Puede persistirse o calcularse: `online` \| `degraded` \| `offline` según heartbeat+TTL y readiness |
| `dictionary_database` | No | Nombre de base diccionario, informativo / diagnóstico |
| empresa activa | No en esta tabla | La elige la sesión; Laravel la manda como `_database` en el job |
| `revoked_at` | No en MVP | Fase 2; en MVP basta `activo=false` |

### 4.3 Campos que el modo agente **no usa**

| Campo | Por qué no |
|-------|------------|
| `host` (IP o hostname del SQL) | Laravel no se conecta al SQL. El agente ya está en esa red. |
| `port` (1433) | Idem. |
| `username` / `password` SQL | Solo en el servidor del cliente (`appsettings.local.json`). |

`host` y `port` pueden quedar en el esquema por compatibilidad histórica, **nullable**. En modo agente **no se consultan**. Tenants **sin** `agent_id` pueden seguir usando SQL directo legacy durante la transición (D5).

### 4.4 Cómo “se conecta” AWS al SQL (modo agente)

Frase de negocio: la app en AWS consulta el SQL del cliente.

Frase técnica: Laravel usa `agent_id` de `empresas_conexion` → POST interno al Gateway → el Gateway usa la conexión **ya abierta por el agente** → el agente usa las credenciales SQL **locales**.

No hay un tercer significado. Si alguien pone una IP en `host` “para que ande” un tenant en modo agente, está violando este SPEC.

---

## 5. Credenciales que pide el auto-instalador

El instalador es un .exe Windows, se ejecuta como Administrador, y **debe pedir todo lo esencial en la UI**. Prohibido dejar tokens por defecto o editar JSON a mano como paso de producción (lab: config manual permitida según D10).

### Identidad (la da PaqSystems al dar de alta el cliente)

| Campo | Notas |
|-------|--------|
| AgentId | Obligatorio |
| ClientId | Obligatorio |
| AgentToken | Obligatorio, password-char, **sin valor por defecto** |
| Gateway URL | Obligatorio; default de fábrica `https://gateway.paqsuite.com/agent-hub` |

### SQL local (las conoce el administrador del servidor Tango)

| Campo | Notas |
|-------|--------|
| Servidor SQL | Instancia local o LAN, ej. `SERVIDORTM\AXSQLEXPRESS` o `localhost` |
| Puerto SQL | Opcional; vacío = 1433 |
| Base diccionario | Nombre exacto, ej. `Diccionario_000205_012` |
| Usuario SQL | Con permisos de lectura/ejecución/CREATE PROCEDURE en diccionario y empresas |
| Contraseña SQL | Password-char |

### Acciones del instalador

1. Validar que ningún campo obligatorio esté vacío.
2. **Probar conexión SQL** antes de instalar. Si falla, no instala (no crea servicio).
3. **Probar salida al Gateway** (HTTPS/WSS al hub). Si falla, **aborta sin crear el servicio Windows** ni dejar instalación a medias. Mensaje claro (DNS/TLS/443 saliente).
4. Override opcional (checkbox avanzado, default desmarcado): “Instalar de todos modos; el agente reintentará”. Solo entonces se permite instalar con gateway fallido.
5. Copiar binarios (desde el ZIP embebido o release).
6. Escribir `appsettings.local.json` (no se pisa en updates).
7. Registrar e iniciar el servicio Windows (`start= auto`) **solo** tras SQL OK y (gateway OK o override explícito).
8. Mostrar resultado: servicio running + “esperando aparecer online en PaqSuite”.

No pide IP pública. No pide Tailscale. No pide nada de AWS.

Cada release publica **SHA256** del zip/exe en las notas (firma criptográfica = fase 2).

---

## 6. Gateway en Amazon

- Una instancia (MVP) en la **misma VPC** que Laravel.
- HTTPS/WSS en 443 (`gateway.paqsuite.com`); hub SignalR `/agent-hub`.
- Kestrel interno; Nginx o ALB termina TLS y hace upgrade WebSocket.
- Endpoints internos `/internal/jobs/send` y `/internal/agents/{agentId}/status` protegidos con API key.
- El Gateway autentica agentes contra Laravel (o contra el catálogo de tokens). No hardcodea la lista de clientes en `appsettings` de producción.
- Laravel habla al Gateway por URL **interna** (IP privada / DNS interno), no por Tailscale.
- **Online** = último heartbeat dentro del TTL configurado, **no** solo “existe un socket en memoria”.
- Al reiniciar: agentes reconectan solos; jobs en vuelo → `cancelled` (auditados), sin duplicar.

---

## 7. Contrato de un job

Laravel → Gateway:

```json
{
  "traceId": "01J8…",
  "agentId": "tecmetal-agent-01",
  "clientId": "Tec-Metal001",
  "operation": "auth.login",
  "parameters": { "codigo": "01", "clave": "***", "_database": "TEC_METAL" },
  "timeoutSeconds": 30
}
```

- `traceId`: obligatorio; correlaciona Laravel → Gateway → Agente → logs.
- `jobId`: lo asigna el Gateway (único); viaja en la respuesta.

Respuestas de estado del job:

| Estado | Significado |
|--------|-------------|
| `success` | Operación OK; respuesta usable |
| `failed` | Error controlado de operación o SQL |
| `timeout` | Se superó el límite de tiempo |
| `offline` | No hay agente elegible / heartbeat fuera de TTL |
| `degraded` | Hay red/sesión con el gateway, pero el agente no está listo para SQL |
| `cancelled` | Abortado antes de completar; no equivale a `failed` |

Si el tenant está en **modo agente** (`agent_id` presente) y el agente está `offline`, Laravel responde `AGENT_OFFLINE`. **No hay fallback a SQL directo** para ese tenant.

Los tenants **aún no migrados** (sin `agent_id`) pueden seguir usando SQL directo durante el MVP, hasta la transformación total (D5).

El agente **no ejecuta SQL libre**. Solo operaciones de lista blanca → stored procedure parametrizado.

---

## 8. MVP — qué entra y qué no

### Entra

- Agente como Windows Service, reconexión automática, heartbeat + TTL, logs, readiness.
- Gateway en AWS con un agente piloto conectado por Internet (salida 443).
- Demostración de la vertical con **config manual** (`appsettings.local.json`) antes del instalador GUI.
- Instalador con las credenciales de la sección 5 (MUST del MVP, **después** de verdear el caño); prueba SQL + gateway (D14).
- Alta en `empresas_conexion` sin `host` obligatorio para modo agente; 1 agente por tenant.
- Laravel: enviar job + consultar status por Gateway cuando hay `agent_id`.
- Operaciones: `diagnostics.run` y piloto **`auth.login`** (SP piloto embebido OK; no migraciones masivas).
- Documentación de instalación + SHA256 en releases.
- Convivencia temporal: tenants **sin** `agent_id` siguen pudiendo usar SQL directo hasta la transformación total.

### No entra (fase 2)

- El resto de operaciones (incl. `clientes.buscar` como porte posterior).
- Auto-update del agente; firma de instalador (SHA256 sí en MVP).
- Múltiples instancias de Gateway / Redis backplane; N agentes por tenant.
- Cache de resultados; Tailscale como feature.
- Fallback SQL de un tenant ya en modo agente.
- Eliminar por completo el camino SQL directo de todos los tenants (corte final post-transformación).

---

## 9. Criterios de aceptación del SPEC (MVP)

1. Un servidor de cliente **sin Tailscale** deja el agente `Running` (lab: config manual; cierre MVP: instalador .exe).
2. El agente aparece **online** (heartbeat dentro de TTL) en PaqSuite.
3. `diagnostics.run` round-trip con readiness (`network_ok` … `operational` / `degraded`).
4. `auth.login` live devuelve datos reales de Tango vía agente.
5. `empresas_conexion` del piloto tiene `agent_id` y **no requiere** `host`.
6. Con `agent_id` y servicio detenido → `AGENT_OFFLINE`; **no** SQL por IP.
7. Gateway en AWS con HTTPS; no en PC de desarrollo.
8. Instalador: sin AgentToken o SQL fail → no crea servicio; gateway fail → aborta salvo override (D14).
9. Tenant sin `agent_id` puede seguir en SQL directo (transición); no invalida el piloto agente.

### Anexo — Matriz de aceptación (Codex A-01…A-10)

| ID | Caso | Resultado esperado |
|----|------|--------------------|
| A-01 | Instalación limpia (o lab con appsettings) | Servicio iniciado; config persistida |
| A-02 | Solo salida TCP 443 | Agente conectado al gateway público |
| A-03 | `X-Paq-Cliente` válido | Laravel selecciona el `agent_id` esperado |
| A-04 | agent/client incompatibles | Rechazo auditado |
| A-05 | Cambio de IP/NAT de salida | Reconecta; `last_seen_ip` puede cambiar; ruteo por `agent_id` intacto |
| A-06 | `auth.login` | Resultado correcto desde SQL local |
| A-07 | Servicio detenido | `offline` / `AGENT_OFFLINE` sin espera indefinida |
| A-08 | Reinicio gateway | Agente reconecta; jobs en vuelo `cancelled`; sin duplicados |
| A-09 | Operación lenta controlada | `timeout` único; sin job duplicado |
| A-10 | SQL libre / SP no permitido | Rechazo; logs sin secretos |

---

## 10. Seguridad (mínimo del SPEC)

- TLS en todo el tráfico AWS ↔ agente.
- Token de agente único, rotables a futuro (MVP: generar en el alta).
- No loguear tokens, passwords ni connection strings.
- Lista blanca de operaciones. Cero SQL concatenado.
- SQL Server no expuesto a Internet.
- API interna Gateway ↔ Laravel con API key; no pública.
- SHA256 del instalador publicado en cada release.

---

## 11. Observabilidad mínima

- Agente: log de conexión, jobs, errores, readiness (archivos locales).
- Gateway: conexiones, jobs, timeouts, online/degraded/offline (TTL), `traceId` / `jobId`.
- Laravel: `traceId`, `jobId`, duración, status, `agent_id` (sin payloads sensibles).

### Readiness del agente (diagnostics / status)

Orden lógico (una falla de esquema no debe ocultar una falla de red):

```text
network_ok → gateway_authenticated → sql_connection_ok → schema_ready → operational
```

Si hay red/auth pero SQL o esquema fallan → agente/`job` pueden reportar `degraded`.

### Latencia (mínimo en logs de TR-006; no bloquea el SPEC)

Separar cuando sea práctico: Laravel→gateway, resolución agente, gateway→agente, SQL open, SP exec, serialización, retorno.

Sin observabilidad mínima no se soporta un cliente. Con lo de arriba alcanza el MVP.

---

## 12. Riesgos (MVP)

| Riesgo | Mitigación |
|--------|------------|
| Token o password expuestos | Secretos protegidos; sin defaults productivos; logs saneados |
| Dependencia accidental de IP | Prueba A-05; `last_seen_ip` no rutea; modo agente no lee `host` |
| Gateway reiniciado con jobs en vuelo | `cancelled` auditado; sin reentrega silenciosa |
| Tango con esquemas distintos | Un SP piloto versionado; migraciones masivas fuera del runtime |
| Instalador “exitoso” sin caño | Prueba gateway + abort sin servicio (D14) |
| Expansión de operaciones antes del caño | Prohibido; solo `diagnostics.run` + `auth.login` |
