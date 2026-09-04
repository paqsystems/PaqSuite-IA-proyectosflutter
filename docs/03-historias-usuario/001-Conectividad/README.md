# 03 — Historias de usuario — 001-Conectividad

Épica MVP de conectividad. Origen: [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md).

Una HU = una capacidad observable. Circuito: **A → A1 → B → B1 → C → C1 → D1 → D → E → F1 → F**. Comando: `Hacé el paso X`.

## Orden efectivo D10 (construcción)

Los IDs de HU se mantienen; el número **no** es el orden estricto de implementación.

| Paso | HU | Título | Repo |
|-----:|----|--------|------|
| 1 | [HU-001](HU-001-alta-cliente-agente.md) | Alta modo agente | TANGO |
| 2 | [HU-002](HU-002-gateway-aws.md) | Gateway AWS | este |
| 3 | [HU-004](HU-004-agente-heartbeat.md) | Agente conectado (config **manual** en lab) | este |
| 4 | [HU-005](HU-005-diagnostics-run.md) | diagnostics.run | este + TANGO |
| 5 | [HU-006](HU-006-auth-login.md) | Operación piloto | este + TANGO |
| 6 | [HU-007](HU-007-corte-duro-modo-agente.md) | Corte duro modo agente (sin fallback SQL) | TANGO |
| 7 | [HU-003](HU-003-auto-instalador.md) | Auto-instalador | este |
| 8 | [HU-008](HU-008-documentacion-instalacion.md) | Documentación | este |

Decisión: D10 en [decisiones-tecnicas.md](../../02-producto/decisiones-tecnicas.md) (lab con `appsettings` manual antes del instalador).

**Nota Laravel:** HU-001, HU-007 (y parte de HU-005/006) se implementan en `PaqSuite-IA-TANGO` con los mismos IDs.

## Fuera del MVP

Portar operaciones existentes, auto-update, botón de descarga en PaqSuite, escalado horizontal del Gateway: no se estiman aquí.
