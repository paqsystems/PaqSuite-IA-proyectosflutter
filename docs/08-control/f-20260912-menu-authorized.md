# Paso F — menu.authorized (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-12 |
| Repo | PaqSuite-IA-AgenteCliente-PAQ |
| Alcance | Op `menu.authorized` + SP `PAQ_User_Menu_Authorized` + whitelist |
| Plan | F3 plan-20260908 · adopción GEN-33 |
| F1 | [f1-20260912-menu-authorized.md](./f1-20260912-menu-authorized.md) |

## Resumen

El agente implementa el tramo SQL del menú autorizado exigido por F3 (Framework no tiene op Must; el host dispara el job). Código, contrato y SP coherentes con el smoke lab.

## Completitud

| Ítem | Veredicto |
|------|-----------|
| `JobOperations.MenuAuthorized` | ✓ |
| Runner + executor SP + DI | ✓ |
| Whitelist en `AgentGatewayConnector` | ✓ |
| Script SQL dictionary + README | ✓ |
| Tests `MenuAuthorizedRunnerTests` | ✓ |
| Ops dominio / F4+ | ✗ fuera de alcance |

## Corrección

| Comportamiento | Veredicto |
|----------------|-----------|
| Params `user_id` / `empresa_id` → SP | ✓ |
| Payload `items` + `procedimientos` + flags | ✓ (smoke 166 ítems) |
| Otras ops → `OPERATION_NOT_ALLOWED` | ✓ mensaje whitelist actualizado |
| Sin SQL libre / sin Tailscale | ✓ |

## Coherencia

| Cadena | Veredicto |
|--------|-----------|
| Plan F3 Agente “ops menú” ↔ código | ✓ (`menu.authorized` ≈ menú efectivo) |
| Host Tango `GatewayMenuAuthzService::OPERATION` ↔ contrato | ✓ mismo string |
| HU-008/TR-009 Finalizado (docs) | ✓ aparte del menú |

## Pruebas

- F1: 4 unitarios OK + smoke HTTP.
- No re-ejecutado en este F.

## Próximos pasos

- F4: ops/SP de params/prefs cuando Framework defina contratos.
- Dominio D1: una op por TR (no ampliar whitelist masiva).

## Puede el humano marcar Finalizado

**Sí** para el tramo F3 agente (cerrado con F1+este F). No hay HU formal `menu.authorized` en épica 001; el cierre vive en plan F3 + informe F1/F.
