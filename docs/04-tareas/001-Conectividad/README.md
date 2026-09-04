# 04 — Tareas técnicas — 001-Conectividad

Origen: [HU 001-Conectividad](../../03-historias-usuario/001-Conectividad/README.md).  
Cada TR sigue **C1 → D1 (si COMPLEJA) → D → E → F1 → F**. Comando: `Hacé el paso C1`, etc.  
No se inventan features. Si falta algo, se vuelve al SPEC.

Orden de ejecución = columna “Orden D10” (no el número de TR).

## Mapa HU → TR (orden de ejecución)

| Orden | HU | TR | Repo |
|------:|----|----|------|
| 1 | HU-001 | [TR-001 Alta `empresas_conexion` modo agente](TR-001-alta-empresas-conexion.md) | **TANGO** |
| 2 | HU-002 | [TR-002 PaqGateway (código)](TR-002-paqgateway-app.md) + [TR-003 Deploy AWS](TR-003-deploy-gateway-aws.md) | Este / infra |
| 3 | HU-004 | [TR-005 PaqAgent conexión + heartbeat](TR-005-paqagent-servicio.md) (config manual OK) | Este |
| 4 | HU-005 | [TR-006 Job `diagnostics.run` e2e](TR-006-diagnostics-e2e.md) | Este + TANGO |
| 5 | HU-006 | [TR-007 Operación piloto live](TR-007-auth-login-piloto.md) | Este + TANGO |
| 6 | HU-007 | [TR-008 Corte duro modo agente](TR-008-corte-duro-modo-agente.md) | **TANGO** |
| 7 | HU-003 | [TR-004 Auto-instalador](TR-004-auto-instalador.md) | Este |
| 8 | HU-008 | [TR-009 Documentación de instalación](TR-009-docs-instalacion.md) | Este |

**Nota explícita:** el trabajo Laravel (TR-001, TR-007, TR-008 y la parte Laravel de TR-006) ocurre en **`PaqSuite-IA-TANGO`** con los mismos IDs de HU. Este repo no scaffoldea PHP.

Contratos compartidos (`PaqContracts`) se crean en TR-002 y los consume TR-005.  
Lab: TR-005 puede usar `appsettings.local.json` manual (D10). El instalador (TR-004) viene **después** de la vertical.

Plantillas: **sin Tailscale** y **sin fallback SQL** para modo agente.

## Definición de hecho de cada TR

Una TR no se cierra si falta alguno:

1. Criterios de la HU cubiertos.
2. Tests acordados en la TR (o prueba e2e documentada cuando es infra).
3. Sin Tailscale en el camino feliz.
4. Sin secretos en logs ni en git.
5. Sección Traza completada.
6. Informe **F1** + **F** en `docs/08-control/` (no cierre falso).
