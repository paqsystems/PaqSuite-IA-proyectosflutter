# HU-003 — Auto-instalador con credenciales esenciales

| Campo | Valor |
|-------|--------|
| Identificador | HU-003 |
| Estado | Finalizado |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Administrador del servidor del cliente |
| Dependencias | HU-001 (valores AgentId/ClientId/AgentToken); binarios PaqAgent; **después** de HU-004…HU-007 (caño lab verde) — D10 |
| Clasificación | HU COMPLEJA |
| Repo de implementación | este (`src/PaqAgentInstaller`) |
| TR | [TR-004](../../04-tareas/001-Conectividad/TR-004-auto-instalador.md) — Finalizado |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §5, §8–§10; D1, D9, D10, D14, D19 |
| B1 | 2026-09-05 — Lista para TR |
| C1 | [c1-20260905-TR-004.md](../../08-control/c1-20260905-TR-004.md) — Apto; Q1–Q8 |
| F1 | [f1-20260906-TR-004.md](../../08-control/f1-20260906-TR-004.md) — Aprobado con observaciones |
| F | [f-20260906-TR-004.md](../../08-control/f-20260906-TR-004.md) — apto Finalizado |

### Narrativa

Como **administrador del servidor SQL del cliente** quiero **un instalador Windows en asistente por pasos que verifique el runtime, pida credenciales, pruebe SQL y Gateway, y deje el agente como servicio**, para no editar JSON a mano ni pelearme con errores técnicos de .NET faltante.

### Nota de orden (D10)

Lab puede verdear el caño con `appsettings.local.json` manual (HU-004…HU-007). Esta HU **empaqueta** ese resultado para el cliente final y cierra el MUST de instalación del MVP; no bloqueaba la demo vertical.

### Estructura del asistente (MUST documentar / implementar en TR)

| Paso | Nombre | Qué hace |
|-----:|--------|----------|
| 0 | Runtime | Detectar .NET 8 Desktop x64; si falta → aviso claro + SHOULD ofrecer instalar (web o embebido); **avisar posible reinicio del servidor**; no continuar sin runtime OK (D19) |
| 1 | Credenciales | Identidad + SQL + Gateway URL (SPEC §5) |
| 2 | Pruebas | Probar SQL; probar Gateway (D14); override avanzado default off |
| 3 | Instalar | Copiar binarios; `appsettings.local.json`; servicio `PaqAgent` auto-start |
| 4 | Resultado | Running + mensaje “esperando online en PaqSuite” |

En un slice de D se puede entregar primero **detectar + avisar (+ reinicio)** y después la oferta de instalación; la **estructura de pasos** debe existir desde el diseño.

### Alcance in

- WinForms .NET 8 x64 (D1) + entrypoint compatible con D19 (bootstrapper o self-contained para el paso 0).
- Asistente por pasos 0→4 (tabla arriba).
- UI con campos SPEC §5: identidad + SQL local + Gateway URL.
- AgentToken obligatorio, password-char, **sin default** / sin `dev-agent-token`.
- Probar SQL; si falla → no instala (no crea servicio).
- Probar salida al Gateway (HTTPS/WSS); si falla → aborta sin servicio (D14), salvo override avanzado default off.
- Escribir `appsettings.local.json` junto al binario; servicio Windows `PaqAgent`, `start=auto`, Running tras éxito.
- Distribución: zip release GitHub + SHA256 publicado (D9).
- Sin IP pública, sin Tailscale, sin pedir nada de AWS.

### Alcance out

- Auto-update del agente; firma criptográfica del instalador (fase 2; SHA256 sí).
- Instructivo largo cliente (HU-008 / TR-009) — complementa, no reemplaza el paso 0.
- Botón de descarga dentro de PaqSuite web.
- Edición manual de JSON como paso de producción (prohibido; lab sí).
- Cambiar contrato Gateway/Agente (ya verde en HU-004…007).

### Reglas

1. Campos identidad: AgentId, ClientId, AgentToken, Gateway URL — todos obligatorios (SPEC §5).
2. Gateway URL default de fábrica: `https://gateway.paqsystems.com/agent-hub` (C1 Q1); editable.
3. SQL: Servidor, Base diccionario, Usuario, Contraseña obligatorios; Puerto opcional (vacío = 1433).
4. Orden: paso 0 runtime → validar vacíos → SQL → Gateway → (override?) → binarios → local.json → servicio (SPEC §5 + D14 + D19).
5. Override “Instalar de todos modos…”: checkbox avanzado, **default desmarcado** (D14).
6. `appsettings.local.json` no se pisa en updates.
7. Release: SHA256 del zip/exe (D9).
8. Runtime faltante: mensaje claro + aviso de **posible reinicio**; SHOULD ofrecer instalar (D19).

### Criterios de aceptación

1. UI pide exactamente los campos del SPEC sección 5 (incluye **AgentToken**, sin default).
2. Gateway URL prellenada con el default de fábrica del SPEC/D8, editable.
3. Botón “Probar conexión” contra SQL local; si falla, **no** instala (no crea servicio).
4. Prueba de salida al Gateway; si falla, **aborta sin crear servicio** (D14). Override avanzado opcional, default off.
5. Si AgentId, ClientId o AgentToken están vacíos, **no** instala.
6. Tras éxito: servicio Windows `PaqAgent` (nombre estable) `Running`, `start= auto`, `appsettings.local.json` escrito junto al binario.
7. No pide IP pública ni Tailscale.
8. Se distribuye como zip con el .exe (release GitHub). Documentar prerrequisito en release/HU-008.
9. Cada release publica SHA256 del asset (D9).
10. **Paso 0 runtime (D19):** si falta .NET 8 Desktop x64 → aviso claro (no error técnico crudo) + aviso de posible **reinicio del servidor**; no avanza a instalar el servicio sin runtime OK. SHOULD: ofrecer instalación (descarga o embebido).
11. La UI es un **asistente por pasos** (0 Runtime → 1 Credenciales → 2 Pruebas → 3 Instalar → 4 Resultado).

```gherkin
Feature: Instalador
  Scenario: Runtime faltante
    Given no hay .NET 8 Desktop Runtime x64
    When se abre el instalador
    Then muestra aviso claro de runtime faltante
    And advierte que puede ser necesario reiniciar el servidor
    And no crea el servicio PaqAgent

  Scenario: Rechaza sin token
    Given runtime OK y formulario con SQL ok y AgentToken vacío
    When pulsa Instalar
    Then no crea el servicio
    And muestra error de token obligatorio

  Scenario: SQL falla
    Given runtime OK, identidad completa y SQL inválido
    When pulsa Probar conexión o Instalar
    Then no crea el servicio

  Scenario: Gateway falla sin override
    Given runtime OK, SQL ok e identidad ok y Gateway inalcanzable
    And override desmarcado
    When pulsa Instalar
    Then no crea el servicio
    And mensaje claro de salida/DNS/TLS/443

  Scenario: Instalación feliz
    Given runtime OK, credenciales válidas y Gateway ok
    When completa el asistente e instala
    Then el servicio PaqAgent queda Running
    And appsettings.local.json contiene el token informado
```

### Supuestos

- Los valores AgentId / ClientId / AgentToken vienen del alta Laravel (HU-001), entregados al admin fuera de banda.
- El ZIP de release incluye (o referencia) los binarios de PaqAgent necesarios para el servicio.
- Scaffold `src/PaqAgentInstaller` es punto de partida; se implementa en TR-004.

### Dudas / decisiones pendientes (no inventar en D)

Cerradas en C1 (Q1–Q8): ver [c1-20260905-TR-004.md](../../08-control/c1-20260905-TR-004.md).

### Veredicto B1

**Lista para TR: Sí** (C1 Apto).

Siguiente D10: **HU-008** (documentación de instalación).
