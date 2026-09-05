# Lab local — verificar el caño sin túnel ni instalación completa

| Campo | Valor |
|-------|--------|
| Origen | D8, D10, SPEC-AGW-001 §11 |
| Objetivo | Probar Agente ↔ Gateway (y luego Laravel) **por tramos**, sin Tailscale, sin AWS y sin instalador GUI |
| Cuándo | Durante TR-002 / TR-005 / TR-006; no espera HU-003 ni deploy productivo |

**Prohibido en lab:** Tailscale como requisito, `dev-agent-token` en configs que se presenten como piloto, fallback SQL por `host` si el tenant ya tiene `agent_id`.

---

## 1. Principio

El camino oficial de laboratorio es:

1. Gateway en `http://127.0.0.1:5100`
2. Agente con `appsettings.local.json` **escrito a mano**
3. SQL local o de lab (no SQL de cliente real vía overlay)
4. Jobs internos (`/internal/*`) **antes** de Laravel en AWS
5. Instalador GUI y Gateway AWS **después** de que el caño local esté verde (D10)

No hace falta “instalar todo el túnel” para saber si el diseño se cumple. Se verifica **un tramo por vez**. Si algo falla, el síntoma apunta al tramo, no a “el sistema no anda”.

---

## 2. Readiness (dónde está la falla)

Orden lógico (SPEC §11). Una falla de esquema **no** debe ocultar una falla de red:

```text
network_ok → gateway_authenticated → sql_connection_ok → schema_ready → operational
```

| Estado / síntoma | Significado | Tramo |
|------------------|-------------|--------|
| No handshake WSS / no llega al hub | URL, puerto, proceso Gateway | Red / PaqGateway |
| Conecta pero no queda `online` | Token, query M8, heartbeat / TTL (30 s / 90 s) | Auth + heartbeat |
| Job → `offline` | Heartbeat fuera de TTL o agente caído; **no** se intenta SQL remoto | Presencia |
| Job → `degraded` | Red/auth OK; SQL o esquema no | SQL local |
| Job → `timeout` | El job llegó; la espera se venció (SQL/SP o ida-vuelta) | Mirar tramos de duración |
| Job → `failed` + `OPERATION_NOT_ALLOWED` | Lista blanca | Operación, no el canal |
| Laravel `AGENT_OFFLINE` | Corte duro correcto; el hueco está en agente/gateway | No usar `host` |

Correlación: el mismo **`traceId`** en log de Laravel (cuando exista), Gateway y archivo del agente.

---

## 3. Tramos de verificación (orden)

| # | Qué levantás | Qué comprobás | Repo / pieza |
|---|--------------|---------------|--------------|
| 1 | Solo `PaqGateway` en `:5100` | Hub `/agent-hub`; `GET /internal/agents/{id}/status` con API key; **401/403** sin key | `src/PaqGateway` (TR-002) |

**Dev (TR-002):** en `appsettings.Development.json`, `Gateway:UseDevAuthStub=true` + `Gateway:InternalApiKey` (header `X-Paq-Internal-Api-Key`). El stub **no** vale en Production. Conexión hub: `.../agent-hub?agentId=&clientId=&agentToken=`.

Mock de agente (lab, no es TR-005):

```powershell
# Terminal A
dotnet run --project src/PaqGateway

# Terminal B
dotnet run --project tools/LabAgentMock
# opcional: hubUrl agentId clientId agentToken
# dotnet run --project tools/LabAgentMock -- http://127.0.0.1:5100/agent-hub lab-agent-01 lab lab-token-manual

# Terminal C
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }
Invoke-RestMethod http://127.0.0.1:5100/internal/agents/lab-agent-01/status -Headers $h
$body = '{"traceId":"01MANUAL","agentId":"lab-agent-01","clientId":"lab","operation":"diagnostics.run","timeoutSeconds":15,"parameters":{}}'
Invoke-RestMethod http://127.0.0.1:5100/internal/jobs/send -Method Post -Headers $h -ContentType "application/json" -Body $body
```
**Tramo 2 (TR-005) — PaqAgent real:**

```powershell
# Una vez: copiar plantilla (no commitear secretos)
Copy-Item src/PaqAgent/appsettings.local.json.example src/PaqAgent/appsettings.local.json

# Terminal A — Gateway (Dev stub)
dotnet run --project src/PaqGateway

# Terminal B — Agente
dotnet run --project src/PaqAgent

# Terminal C — status
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }
Invoke-RestMethod http://127.0.0.1:5100/internal/agents/lab-agent-01/status -Headers $h
# Esperado: status online mientras B corre
```

Logs del agente: `src/PaqAgent/logs/paqagent-*.log` (o bajo el output de `dotnet run`). **Sin** token en claro en logs (URL redactada).

| # | Qué levantás | Qué comprobás | Repo / pieza |
|---|--------------|---------------|--------------|
| 2 | Gateway + `PaqAgent` (config manual) | Conecta (query M8), heartbeat, status `online` | `src/PaqAgent` (TR-005) |
| 3 | + SQL local de lab | Readiness hasta `sql_connection_ok` | Agente + SQL |
| 4 | `POST /internal/jobs/send` (`diagnostics.run`) **sin Laravel** | Round-trip, estados D12, `traceId`, `sqlConnectionOk` / `degraded` | Gateway + Agente (TR-006 este repo) |

**Tramo 4 (TR-006) — diagnostics sin TANGO:**

**Dónde:** terminal de **Cursor**, raíz del repo. **Abrí tres terminales nuevas** (no asumas procesos previos).

1. Terminal 1: `dotnet run --project src/PaqGateway` (dejar corriendo)  
2. Terminal 2: `dotnet run --project src/PaqAgent` (dejar corriendo; hace falta `appsettings.local.json`)  
3. Terminal 3: el `Invoke-RestMethod` de abajo  

Con Gateway + PaqAgent ya arriba. SQL en `appsettings.local.json` opcional.

```powershell
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }
$body = @{
  traceId = "01DIAGLAB"
  agentId = "lab-agent-01"
  clientId = "lab"
  operation = "diagnostics.run"
  timeoutSeconds = 30
  parameters = @{}
} | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5100/internal/jobs/send -Method Post -Headers $h -ContentType "application/json" -Body $body
```

| SQL en local.json | Esperado `status` | `data.sqlConnectionOk` |
|-------------------|-------------------|-------------------------|
| Vacío / ausente | `degraded` | `false` |
| Válido y alcanzable | `success` | `true` (`readiness` ≈ `operational`) |
| Server incorrecto | `degraded` | `false` |
| Agente detenido | `offline` | (sin data de agente) |

Sin Tailscale.

**Tramo 5 (TR-006) — TANGO → Gateway lab:** con Terminales 1–2 arriba y en TANGO `.env` `AGENT_GATEWAY_URL=http://127.0.0.1:5100` + `AGENT_GATEWAY_INTERNAL_KEY=lab-internal-api-key`:

```php
// php artisan tinker — una sola línea
app(\App\Services\Agents\AgentGatewayClient::class)->runDiagnostics('lab-agent-01', 'lab', 30, '01TANGODIAG');
```

Evidencia 2026-09-05: `degraded` / `SQL_NOT_CONFIGURED` / jobId `61e6e318…` (detalle en TR-006).

**Tramo 4b (TR-007 slice este) — `auth.login` sin TANGO:**

### Dos bases / dos “usuarios” (no confundir)

| Pieza | Significado |
|-------|-------------|
| `src/PaqAgent/appsettings.local.json` → `sql.*` | Conexión al **diccionario SQL** del cliente (`server`, `database`, login SQL tipo `Axoft`). **No** es `empresas_conexion`. |
| Job `parameters.codigo` | Usuario Tango (`USERS.codigo`, ej. `PQ`). Va en el body del REST, no en `sql.user`. |
| `empresas_conexion` | Solo Laravel (alta agente). Fuera de este tramo REST. |
| `"encrypt": false` | Alinear a SSMS con cifrado **Opcional**; si no, suele aparecer `SQL_UNREACHABLE` (timeout SSL). |
| `AGENT_OFFLINE` | PaqAgent no está conectado (o quedó un `.exe` zombie). Reiniciar agente; si el build falla por archivo bloqueado, matar `PaqAgent.exe`. |

Con Terminales 1–2 arriba (`Conectado al Gateway`). Scripts SP: `src/PaqAgent/Sql/`.

```powershell
$h = @{ "X-Paq-Internal-Api-Key" = "lab-internal-api-key" }

# Whitelist
$bodyBad = @{
  traceId = "01AUTHBAD"; agentId = "lab-agent-01"; clientId = "lab"
  operation = "clientes.buscar"; timeoutSeconds = 30; parameters = @{}
} | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5100/internal/jobs/send -Method Post -Headers $h -ContentType "application/json" -Body $bodyBad

# auth.login — codigo = USERS.codigo (ej. PQ), no el login SQL
$bodyAuth = @{
  traceId = "01AUTHOK"; agentId = "lab-agent-01"; clientId = "lab"
  operation = "auth.login"; timeoutSeconds = 30
  parameters = @{ codigo = "PQ" }
} | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5100/internal/jobs/send -Method Post -Headers $h -ContentType "application/json" -Body $bodyAuth

# diagnostics
$bodyDiag = @{
  traceId = "01DIAGOK"; agentId = "lab-agent-01"; clientId = "lab"
  operation = "diagnostics.run"; timeoutSeconds = 30; parameters = @{}
} | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:5100/internal/jobs/send -Method Post -Headers $h -ContentType "application/json" -Body $bodyDiag
```

| Condición | Esperado |
|-----------|----------|
| `clientes.buscar` | `failed` / `OPERATION_NOT_ALLOWED` |
| `auth.login` sin SQL en local | `degraded` / `SQL_NOT_CONFIGURED` |
| `auth.login` sin `codigo` | `failed` / `INVALID_PARAMETERS` |
| SQL + SP + codigo válido (ej. `PQ`) | `success` + `data.status=OK` |
| diagnostics con SQL OK | `success` + `sqlConnectionOk=true` |

**Evidencia 2026-09-05 (SQL lab + `encrypt:false` + codigo `PQ`):** whitelist OK; `auth.login` → `success`/`OK`/`es_admin=True`; diagnostics → `sqlConnectionOk=True` (detalle en TR-007).

| # | Qué levantás | Qué comprobás | Repo / pieza |
|---|--------------|---------------|--------------|
| 5 | Laravel (TANGO) → Gateway de lab o VPC | `runDiagnostics` / ruteo por `agent_id`; sin exigir `host` | `PaqSuite-IA-TANGO` (TR-006) |
| 6 | Gateway AWS + agente con salida 443 | Internet real, sin Tailscale | TR-003 / HU-002 |
| 7 | Instalador GUI | UI, prueba SQL, prueba gateway | HU-003 / TR-004 (**después** del caño) |

Tests automáticos en este repo (`dotnet test PaqAgentGateway.sln`): stubs/unitarios de Gateway y Agente cubren parte de los tramos 1–2 **sin** UI ni SQL de cliente. No sustituyen el lab manual del tramo 4.

---

## 4. Plantilla mínima `appsettings.local.json` (lab)

Archivo **junto al binario** del agente (no commitear secretos). Valores de ejemplo; reemplazar por los del alta Laravel (HU-001) cuando existan.

```json
{
  "agentId": "lab-agent-01",
  "clientId": "lab",
  "agentToken": "<pegar-token-real-sin-default>",
  "gatewayUrl": "http://127.0.0.1:5100/agent-hub",
  "sql": {
    "server": "192.168.x.x",
    "database": "diccionario_del_piloto",
    "user": "<login-SQL-Server>",
    "password": "<password-SQL>",
    "encrypt": false,
    "trustServerCertificate": true
  }
}
```

Notas:

- Ruta del archivo: **`src/PaqAgent/appsettings.local.json`** (no la raíz del repo).
- `sql.*` = diccionario Tango en SQL Server. **No** es `empresas_conexion` (Laravel).
- `sql.user` = login SQL (ej. Axoft). El usuario Tango (`PQ`) va en el job como `parameters.codigo`.
- `encrypt: false` si SSMS usa cifrado Opcional (evita `SQL_UNREACHABLE` por SSL).
- `gatewayUrl` en lab = localhost (D8). En prod ops = `https://gateway.paqsystems.com/agent-hub`.
- **Sin** `dev-agent-token`.
- Plantilla: `src/PaqAgent/appsettings.local.json.example`.
- SQL puede omitirse solo para verdear conexión al hub; auth.login/diagnostics con SQL exigen diccionario + SP.

---

## 5. Comandos típicos (lab en esta máquina)

Desde la raíz del repo (ajustar cuando el código de TR-002/005 esté listo):

```bash
dotnet build PaqAgentGateway.sln
dotnet run --project src/PaqGateway
```

En otra terminal:

```bash
dotnet run --project src/PaqAgent
```

Tests:

```bash
dotnet test PaqAgentGateway.sln
```

API interna (ejemplos; paths definitivos según TR-002):

```http
GET  http://127.0.0.1:5100/internal/agents/{agentId}/status
POST http://127.0.0.1:5100/internal/jobs/send
```

Headers: API key interna. Body del job: incluir `traceId`, `agentId`, `operation` (`diagnostics.run`), `timeoutSeconds`.

URLs: [urls-deploy.md](urls-deploy.md).

---

## 6. Tramos de duración (cuando exista diagnostics)

En logs de TR-006, separar cuando sea práctico:

1. Laravel → Gateway  
2. Resolución del agente (online / TTL)  
3. Gateway → Agente  
4. Apertura SQL  
5. Ejecución SP / diagnostics  
6. Serialización y retorno  

Así, un `timeout` o latencia alta se localiza sin adivinar.

---

## 7. Qué no es este documento

| Documento | Alcance |
|-----------|---------|
| [instalacion-agente.md](instalacion-agente.md) | Cliente final (HU-008 / TR-009); instalador .exe |
| [deploy-gateway-aws.md](deploy-gateway-aws.md) | Gateway productivo (HU-002 / TR-003) |
| Este archivo | Lab de desarrollo / verificación por tramos |

Si el lab local no está verde, **no** se declara listo el piloto ni se avanza a “solo falta el instalador”.
