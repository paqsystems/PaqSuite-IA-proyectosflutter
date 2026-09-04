# Paquete SDD — Reformulación PaqAgent + PaqGateway

Este directorio es la **fuente de verdad** para rehacer el proyecto en paralelo, con la misma metodología SDD que el resto de PaqSuite (SPEC → HU → TR → implementación → verificación).

El código actual de `PaqAgent/`, `PaqGateway/` y `PaqAgentInstaller/` **no se borra**. Se toma como referencia de lo que ya se aprendió, no como base a parchar. El desarrollo paralelo arranca desde estos documentos, no desde el prompt histórico.

---

## Circuito de trabajo (obligatorio)

```text
Contexto humano (00)
        │
        ▼
SPEC de producto (01)          ← no se escribe código sin SPEC cerrado
        │
        ▼
Decisiones técnicas (02)       ← lenguaje, tabla, prohibiciones
        │
        ▼
Revisión de ambigüedad         ← prompt 07; dudas se cierran o se marcan
        │
        ▼
Historias de usuario (03)      ← una capacidad observable por vez
        │
        ▼
Tareas técnicas / TR (04)      ← checklist verificable, sin inventar features
        │
        ▼
Kickoff (05) + ejecución HU (06)
        │
        ▼
Implementación HU por HU
        │
        ▼
Verificación contra criterios de aceptación
```

**Prohibido:** implementar operaciones de negocio (acopios, informes, etc.) antes de que el MVP de conectividad esté aceptado.

---

## Índice

| Archivo | Para qué |
|---------|----------|
| [00-contexto-reformulacion.md](00-contexto-reformulacion.md) | Por qué se reformula, qué falló, objetivo real |
| [01-SPEC-producto.md](01-SPEC-producto.md) | SPEC cerrado v1.1 (fuente de verdad) |
| [02-decisiones-tecnicas.md](02-decisiones-tecnicas.md) | Lenguaje, `empresas_conexion`, Tailscale, repos |
| [03-historias-usuario.md](03-historias-usuario.md) | HU del MVP (HU-001 a HU-008) |
| [04-tareas-mvp.md](04-tareas-mvp.md) | TR por HU, orden de ejecución |
| [05-prompt-kickoff.md](05-prompt-kickoff.md) | Prompt para arrancar el agente de desarrollo paralelo |
| [06-prompt-ejecutar-hu.md](06-prompt-ejecutar-hu.md) | Prompt para implementar **una** HU |
| [07-prompt-revision-ambiguedad.md](07-prompt-revision-ambiguedad.md) | Prompt SDD antes de pasar SPEC → HU |
| [08-informe-revision-ambiguedad.md](08-informe-revision-ambiguedad.md) | Resultado de la revisión (3 sep 2026) |
| [scaffold-agente-gateway-sdd.md](scaffold-agente-gateway-sdd.md) | Scaffold SDD adaptado (no fullstack BASE) |
| [plan-ciclo-sql-y-updates.md](plan-ciclo-sql-y-updates.md) | Análisis fase 2 SQL/update (no altera MVP) |
| [SPEC-AGW-002-ciclo-sql-y-updates.md](SPEC-AGW-002-ciclo-sql-y-updates.md) | Placeholder SPEC fase 2 |
| [codex-definicion.md](codex-definicion.md) | Spec alternativa Codex (referencia) |
| [historico/01-prompt-inicial.md](historico/01-prompt-inicial.md) | Prompt original (junio 2026). No usar. |

---

## Repos involucrados

| Repo | Qué se construye aquí |
|------|------------------------|
| `PaqSuite-IA-AgenteCliente-PAQ` (este) | Agente Windows, Gateway .NET, instalador |
| `PaqSuite-IA-TANGO` | Contrato Laravel: `empresas_conexion`, `AgentGatewayClient`, **sin** SQL directo en modo agente |

El SPEC cubre ambos. El código de Laravel no vive en este repo; el contrato sí.

---

## Definición de terminado del MVP

Un cliente Tango en **modo agente**, **sin Tailscale y sin IP pública obligatoria en `empresas_conexion`**, puede:

1. Conectar el agente (lab: `appsettings` manual; cierre: auto-instalador).
2. Ver el servicio Windows corriendo.
3. Aparecer online en PaqSuite.
4. Ejecutar `diagnostics.run` y la operación piloto live **`auth.login`**.
5. Si el agente cae: Laravel responde `AGENT_OFFLINE` **sin** caer a SQL por IP.

Los tenants aún sin `agent_id` pueden seguir en SQL directo hasta la transformación total. Si el piloto agente no está verde, el MVP de conectividad no está cerrado.

## Archivo Canvas de Arquitectura Agente Anterior

C:\Users\PabloQ\.cursor\projects\c-Programacion-PaqSuite-IA-AgenteCliente-PAQ\canvases\informe-arquitectura-agente.canvas.tsx

## Comparativa definiciones Codex vs Cursor

C:\Users\PabloQ\.cursor\projects\c-Programacion-PaqSuite-IA-AgenteCliente-PAQ\canvases\comparacion-codex-vs-sdd 