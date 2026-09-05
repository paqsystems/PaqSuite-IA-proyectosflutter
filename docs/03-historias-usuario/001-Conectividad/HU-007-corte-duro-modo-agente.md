# HU-007 — Modo agente: error claro, sin fallback SQL

| Campo | Valor |
|-------|--------|
| Identificador | HU-007 |
| Estado | Especificado |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Usuario de PaqSuite |
| Dependencias | HU-006 **Finalizado** |
| Clasificación | HU SIMPLE |
| Repo de implementación | **PaqSuite-IA-TANGO** |
| TR | [TR-008](../../04-tareas/001-Conectividad/TR-008-corte-duro-modo-agente.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §7; D5 |
| C1 | [c1-20260905-TR-008.md](../../08-control/c1-20260905-TR-008.md) — Apto; Q1–Q7 |
| F1 | [f1-20260905-TR-008.md](../../08-control/f1-20260905-TR-008.md) — Aprobado con observaciones |
| F | [f-20260905-TR-008.md](../../08-control/f-20260905-TR-008.md) |

### Narrativa

Como **usuario** quiero **un mensaje claro si el servidor del cliente (modo agente) no está conectado**, para no esperar timeouts de SQL ni “caídas misteriosas”. Como **producto**, si el tenant ya tiene `agent_id`, **no** queremos que AWS intente SQL por IP.

### Criterios de aceptación

1. Tenant con `agent_id` y agente detenido → API Laravel error `AGENT_OFFLINE` (HTTP **503**, no 401).
2. Con `agent_id` presente: no se lee `host` de `empresas_conexion` para reintentar.
3. Log Laravel: warning con agentId, sin secretos.
4. Test automatizado (unitario Laravel o de contrato) que falle si alguien reintroduce el fallback **para modo agente**.
5. Tenant **sin** `agent_id`: el camino SQL directo legacy **sigue permitido** durante el MVP (transición hasta transformación total). Eso no contradice el corte duro del modo agente.

**Prohibido** en plantillas y código de modo agente: Tailscale, fallback SQL por IP, `host` como llave de ruteo.

```gherkin
Feature: Corte duro modo agente
  Scenario: Offline con agent_id
    Given agent_id configurado y agente offline
    When el usuario dispara la operación piloto
    Then la API responde AGENT_OFFLINE
    And no hay intento de conexión TDS desde Laravel
  Scenario: Legacy sin agent_id
    Given un tenant sin agent_id con host legacy
    When el usuario dispara una consulta live
    Then Laravel puede usar SQL directo (transición MVP)
```

Siguiente: humano puede marcar **Finalizado** (salvedad HTTP e2e Tailscale). Luego D10 siguiente HU.
