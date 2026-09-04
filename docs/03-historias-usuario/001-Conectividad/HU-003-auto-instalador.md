# HU-003 — Auto-instalador con credenciales esenciales

| Campo | Valor |
|-------|--------|
| Identificador | HU-003 |
| Estado | Pendiente |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Administrador del servidor del cliente |
| Dependencias | HU-001 (valores para pegar), binarios del agente; **se ejecuta después** de HU-004…HU-007 (caño verde en lab) |
| Clasificación | HU COMPLEJA |
| Repo de implementación | este (`src/PaqAgentInstaller`) |
| TR | [TR-004](../../04-tareas/001-Conectividad/TR-004-auto-instalador.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §5 |

### Narrativa

Como **administrador del servidor SQL del cliente** quiero **un instalador Windows que me pida las credenciales esenciales, pruebe SQL y deje el agente como servicio**, para no editar archivos a mano ni instalar Visual Studio.

### Nota de orden (D1 / D10)

En laboratorio está permitido verdear HU-004…HU-006 editando `appsettings.local.json` a mano. Esta HU empaqueta ese mismo resultado para el cliente final y **cierra** el MVP de instalación; no bloquea la demo de la vertical.

### Criterios de aceptación

1. UI pide exactamente los campos del SPEC sección 5 (incluye **AgentToken**, sin default).
2. Gateway URL prellenada con `https://gateway.paqsuite.com/agent-hub`, editable.
3. Botón “Probar conexión” contra SQL local; si falla, **no** instala (no crea servicio).
4. Prueba de salida al Gateway; si falla, **aborta sin crear servicio** (D14). Override avanzado opcional, default off.
5. Si AgentId, ClientId o AgentToken están vacíos, **no** instala.
6. Tras éxito: servicio Windows `PaqAgent` (nombre estable) `Running`, `start= auto`, `appsettings.local.json` escrito junto al binario.
7. No pide IP pública ni Tailscale.
8. Se distribuye como zip con el .exe (release GitHub). Documentar prerrequisito: .NET 8 Desktop Runtime x64.

```gherkin
Feature: Instalador
  Scenario: Rechaza sin token
    Given el formulario con SQL ok y AgentToken vacío
    When pulsa Instalar
    Then no crea el servicio
    And muestra error de token obligatorio
  Scenario: Instalación feliz
    Given credenciales de identidad y SQL válidas
    When pulsa Instalar
    Then el servicio PaqAgent queda Running
    And appsettings.local.json contiene el token informado
```
