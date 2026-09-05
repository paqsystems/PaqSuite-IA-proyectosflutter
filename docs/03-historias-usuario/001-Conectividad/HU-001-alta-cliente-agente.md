# HU-001 — Alta de cliente agente sin IP

| Campo | Valor |
|-------|--------|
| Identificador | HU-001 |
| Estado | Finalizado |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Operador PaqSystems |
| Dependencias | Ninguna de producto; contrato Laravel |
| Clasificación | HU SIMPLE (Laravel) |
| Repo de implementación | **PaqSuite-IA-TANGO** |
| TR | [TR-001](../../04-tareas/001-Conectividad/TR-001-alta-empresas-conexion.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) |

Origen: SPEC-AGW-001. Una HU = una capacidad observable. No se implementa la siguiente si la anterior no está aceptada.

### Narrativa

Como **operador de PaqSystems** quiero **dar de alta un cliente en `empresas_conexion` con `agent_id`, `client_id` y token, sin cargar IP ni puerto SQL**, para que Laravel sepa a qué agente pedir datos cuando el agente esté online.

### Criterios de aceptación

1. Formulario o comando de alta pide: `cliente`, `nombre`, genera `agent_id`, `client_id`, `agentToken`.
2. No exige `host` ni `port` ni password SQL.
3. Muestra el token **una vez** (para pasárselo al instalador).
4. El registro queda `activo=true` con `agent_id` poblado y `host` null.
5. Un tenant sin `agent_id` **no** entra al camino agente; durante el MVP ese tenant puede seguir en SQL directo legacy (D5) hasta la transformación total.

Token MVP: **columnas en `empresas_conexion`** (sin tabla `agents` aún). No Tailscale. No fallback modo agente.

```gherkin
Feature: Alta de cliente modo agente
  Scenario: Alta sin host
    Given un operador autenticado en PaqSuite
    When da de alta el cliente "Tecmetal" en modo agente
    Then empresas_conexion tiene agent_id y client_id
    And host es null
    And se muestra un AgentToken una sola vez
```
