# PaqSuite-IA-AgenteCliente-PAQ

Reformulación SDD de **PaqAgent + PaqGateway** (agente Windows saliente + gateway AWS).

| Campo | Valor |
|-------|--------|
| Plataforma | PaqSuite-IA-AgenteCliente-PAQ |
| **Modo** | **AGENTE-GATEWAY** |
| Slug | `agentegateway` |
| Rama de trabajo | `sdd-reformulacion` |
| Contrato Laravel | repo **`PaqSuite-IA-TANGO`** (no se scaffoldea aquí) |

## Fuente de verdad

Árbol SDD en [`docs/`](docs/):

| Carpeta | Contenido |
|---------|-----------|
| [docs/00-contexto/](docs/00-contexto/) | Circuito SDD + DoD MVP |
| [docs/01-arquitectura/](docs/01-arquitectura/) | Diagrama y responsabilidades |
| [docs/02-producto/](docs/02-producto/) | SPEC-AGW-001 + decisiones |
| [docs/03-historias-usuario/001-Conectividad/](docs/03-historias-usuario/001-Conectividad/) | HU-001…HU-008 (orden D10) |
| [docs/04-tareas/001-Conectividad/](docs/04-tareas/001-Conectividad/) | TR-001…TR-009 |
| [docs/05-open-spec/](docs/05-open-spec/) | Trazabilidad SPEC |
| [docs/06-operacion/](docs/06-operacion/) | Lab local + runbooks (AWS/instalador placeholders) |
| [docs/08-control/](docs/08-control/) | Informes A1 / C1 / F |

Dispatcher: `Hacé el paso A` … `Hacé el paso F` — [`.cursor/rules/00-dispatcher-agente-gateway.mdc`](.cursor/rules/00-dispatcher-agente-gateway.mdc).

Prompts de IA: [`prompts/`](prompts/) (A1, C1, D/06, F, kickoff 05, A1-MVP 07, scaffold).

Fase 2 SQL/updates y update de agente (no implementadas): mapa en [fases-roadmap.md](docs/02-producto/fases-roadmap.md); análisis en [plan-ciclo-sql-y-updates.md](docs/02-producto/agente-gateway/plan-ciclo-sql-y-updates.md).

## Solución .NET 8

| Proyecto | Rol |
|----------|-----|
| `src/PaqContracts` | Contratos job/result/heartbeat |
| `src/PaqGateway` | ASP.NET Core + SignalR |
| `src/PaqAgent` | Windows Worker Service |
| `src/PaqAgentInstaller` | WinForms |
| `tests/*` | xUnit (stubs) |

```bash
dotnet build PaqAgentGateway.sln
```

Versión: ver [VERSION](VERSION) (`0.1.0-mvp`).

El código Laravel **no vive en este repo**. Las TR-001, TR-007 y TR-008 se ejecutan en `PaqSuite-IA-TANGO` con los mismos IDs.

No commit/push de secretos. **Tailscale no es parte del producto.** En modo agente no hay fallback SQL por IP.
