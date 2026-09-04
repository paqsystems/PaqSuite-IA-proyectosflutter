# 01 — Arquitectura PaqAgent + PaqGateway

| Campo | Valor |
|-------|--------|
| Origen | SPEC-AGW-001 §2–§6 |
| Canónico | [../02-producto/SPEC-AGW-001-producto.md](../02-producto/SPEC-AGW-001-producto.md) |
| Estado | Extraído para el scaffold SDD; el SPEC manda si hay divergencia |

---

## 1. Sentido de la conexión

Invertir el sentido: el cliente **sale** por 443; AWS **nunca** abre SQL hacia el cliente en modo agente.

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

No hay Tailscale en este diagrama. No hay fallback SQL para un tenant con `agent_id`.

---

## 2. Responsabilidades por componente

| Componente | Repo | Responsabilidad |
|------------|------|-----------------|
| Laravel | `PaqSuite-IA-TANGO` | Valida usuario/permisos, resuelve tenant (`cliente` / `X-Paq-Cliente`), manda job al Gateway por `agentId`. Contrato `empresas_conexion` + `AgentGatewayClient`. |
| PaqGateway | este (`src/PaqGateway`) | Mantiene SignalR, rutea jobs, timeouts, online por heartbeat+TTL. API interna `/internal/*`. |
| PaqAgent | este (`src/PaqAgent`) | Autentica, heartbeat, ejecuta operación de lista blanca contra SQL local, reporta readiness, devuelve JSON. |
| PaqAgentInstaller | este (`src/PaqAgentInstaller`) | UI WinForms: credenciales, prueba SQL + gateway, escribe `appsettings.local.json`, registra el servicio. |
| PaqContracts | este (`src/PaqContracts`) | DTOs job/result/heartbeat (`traceId`, estados). Constantes heartbeat 30 s / TTL 90 s. |

---

## 3. Actores

| Actor | Rol |
|-------|-----|
| Operador PaqSystems | Da de alta el cliente en Laravel, genera `agentId` / `clientId` / `agentToken`, entrega el instalador |
| Administrador del servidor del cliente | Ejecuta el instalador, carga credenciales, deja el servicio corriendo |
| Usuario de PaqSuite | Usa la app con normalidad; no ve el agente |
| Laravel | Ruteo por `agent_id`; no usa `host` en modo agente |
| PaqGateway | Hub WSS + jobs + status |
| PaqAgent | Salida 443 + SQL local |

---

## 4. `empresas_conexion` — llave de ruteo (no connection string)

MVP: **un agente activo por tenant**. Token: **columnas en `empresas_conexion`** (sin tabla `agents` aún; default scaffold H3).

Campos esenciales modo agente: `cliente`, `nombre`, `agent_id`, `client_id`, `activo`, token (hash), `last_seen_at` (runtime; **autoridad en Gateway**, Laravel consulta status API — default H4).

Modo agente **no usa** `host` / `port` / usuario SQL. Esos campos pueden quedar nullable por compatibilidad histórica.

Frase técnica: Laravel usa `agent_id` → POST interno al Gateway → conexión **ya abierta por el agente** → el agente usa SQL **local**.

---

## 5. Credenciales del auto-instalador (SPEC §5)

Identidad (PaqSystems): AgentId, ClientId, AgentToken (sin default), Gateway URL (`https://gateway.paqsuite.com/agent-hub`).

SQL local (administrador Tango): servidor, puerto opcional, base diccionario, usuario, contraseña.

No pide IP pública. No pide Tailscale. No pide nada de AWS.

---

## 6. Gateway en Amazon (SPEC §6)

- Una instancia (MVP) en la **misma VPC** que Laravel.
- HTTPS/WSS en 443 (`gateway.paqsuite.com`); hub SignalR `/agent-hub`.
- Kestrel interno; Nginx o ALB termina TLS.
- `/internal/jobs/send` y `/internal/agents/{agentId}/status` con API key.
- Laravel habla por URL **interna** (no Tailscale).
- **Online** = último heartbeat dentro del TTL (30 s / 90 s), no solo socket en memoria.

URLs: [../06-operacion/urls-deploy.md](../06-operacion/urls-deploy.md).
