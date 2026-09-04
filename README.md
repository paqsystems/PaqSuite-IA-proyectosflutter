# PaqSuite-IA-AgenteCliente-PAQ

Reformulación SDD de **PaqAgent + PaqGateway** (agente Windows saliente + gateway AWS).

## Fuente de verdad

**[docs/02-producto/agente-gateway/](docs/02-producto/agente-gateway/)** — SPEC, decisiones, HU, TR, prompts.

- MVP conectividad: SPEC-AGW-001
- Fase 2 SQL/updates (no implementada): [plan-ciclo-sql-y-updates.md](docs/02-producto/agente-gateway/plan-ciclo-sql-y-updates.md)

## Solución .NET 8

| Proyecto | Rol |
|----------|-----|
| `src/PaqContracts` | Contratos job/result/heartbeat |
| `src/PaqGateway` | ASP.NET Core + SignalR |
| `src/PaqAgent` | Windows Worker Service |
| `src/PaqAgentInstaller` | WinForms |
| `tests/*` | xUnit |

```bash
dotnet build PaqAgentGateway.sln
```

Versión: ver [VERSION](VERSION).

## Circuito SDD

Ver [README del producto](docs/02-producto/agente-gateway/README.md). Contrato Laravel en repo `PaqSuite-IA-TANGO`.

No commit/push de secretos. Tailscale no es parte del producto.
