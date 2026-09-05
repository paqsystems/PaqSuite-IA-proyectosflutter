# Nota — Tenancy modo agente sin SQL (TANGO)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-05 |
| Repo | PaqSuite-IA-TANGO (`FRAMEWORK`) |
| Origen | Salvedad F TR-008 + SPEC-AGW §4.3 / D5 / D11 |

## Cambio

- `ResolveTenant`: si `agent_id` + `client_id` (mismo criterio que Gateway / `InstalacionRecord::isGatewayMode`) → setea `tenant_cliente` / `tenant_config` y **no** abre PDO por `host`.
- `ResolveDictionaryConnection`: si modo agente → **no** reapunta ni conecta dictionary por `host`.
- Atributo request: `tenant_modo_agente`.

## Evidencia

| Prueba | Resultado |
|--------|-----------|
| Feature `ResolveTenantModoAgenteTest` | host `203.0.113.99` inalcanzable → login HTTP **503** / `5030` / `AGENT_OFFLINE` (no “No se pudo conectar…”) |
| Lab HTTP `X-Paq-Cliente: tecser` en `:8002` | **503** / `5030` / `AGENT_OFFLINE` (sin Tailscale) |

## Pendiente

Revisar el **mismo contrato** en **PaqSuite-IA-FRAMEWORK** cuando TANGO se asocie (hoy no).
