# HU-006 — Operación piloto live (`auth.login`)

| Campo | Valor |
|-------|--------|
| Identificador | HU-006 |
| Estado | Especificado |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Usuario de PaqSuite |
| Dependencias | HU-005 **Finalizado**; SP `PAQ_Auth_Login` en diccionario/empresa del piloto |
| Clasificación | HU SIMPLE |
| Repo de implementación | este + `PaqSuite-IA-TANGO` |
| TR | [TR-007](../../04-tareas/001-Conectividad/TR-007-auth-login-piloto.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §7; D15 |
| C1 | [c1-20260905-TR-007.md](../../08-control/c1-20260905-TR-007.md) — Apto; Q1–Q9 |

### Narrativa

Como **usuario de PaqSuite** quiero **completar un login con datos reales de Tango vía agente**, para comprobar que el producto sirve y no solo que “el socket está abierto”.

### Criterios de aceptación

1. Operación de negocio del MVP: **`auth.login`** (D15).
2. Lista blanca: cualquier otra operación → `OPERATION_NOT_ALLOWED`.
3. SQL parametrizado; cero SQL enviado desde AWS.
4. Resultado usable por Laravel (JSON camelCase); job lleva `traceId`.
5. El usuario no configura IP.
6. Un solo agente por tenant resuelve el job (D13).
7. Con `agent_id` presente: **sin** fallback a SQL directo si el agente está offline/timeout o Gateway falla (`AGENT_OFFLINE` / error de proveedor).

```gherkin
Feature: Operación piloto auth.login
  Scenario: Login vía agente
    Given tenant con agent_id y agente online
    When el usuario se loguea con un código Tango válido
    Then Laravel obtiene el resultado vía Gateway
    And no abre conexión SQL hacia el cliente

  Scenario: Agente offline
    Given tenant con agent_id y PaqAgent detenido
    When el usuario intenta login
    Then respuesta AGENT_OFFLINE (o error de proveedor)
    And no hay fallback SQL directo
```

Siguiente: **paso D1** de TR-007.
