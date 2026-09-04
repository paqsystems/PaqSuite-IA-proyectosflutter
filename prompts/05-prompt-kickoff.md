# 05 — Prompt de kickoff (desarrollo paralelo)

Copiar este bloque **entero** en una conversación nueva (rama `sdd-reformulacion` o worktree limpio). No implementar en el mismo turno en que se hace la revisión de ambigüedad.

---

Sos un ingeniero senior. Vas a **reformular** PaqAgent + PaqGateway en paralelo al código existente, con SDD.

## Fuente de verdad (leer en este orden, sin saltearse)

1. `docs/00-contexto/README.md`
2. `docs/00-contexto/00-contexto-reformulacion.md`
3. `docs/01-arquitectura/01-arquitectura-agente-gateway.md`
4. `docs/02-producto/SPEC-AGW-001-producto.md`
5. `docs/02-producto/decisiones-tecnicas.md`
6. `docs/03-historias-usuario/001-Conectividad/README.md`
7. `docs/04-tareas/001-Conectividad/README.md`
8. `docs/08-control/08-informe-revision-ambiguedad.md`

El código en `src/` es **scaffold vacío**. El código histórico (si existe fuera de `src/` o en `main`) es **referencia**, no la especificación. Si el código contradice el SPEC, gana el SPEC.

## Objetivo del producto (no lo negociés)

Agente Windows en el servidor SQL del cliente + Gateway en Amazon. En **modo agente**, Laravel en AWS **no** se conecta a SQL remoto. `empresas_conexion` guarda `agent_id` / `client_id` / token, **no** la IP del cliente como llave. Tailscale no es parte del producto. Tenants **sin** `agent_id` pueden seguir en SQL directo hasta la transformación total (D5).

Modo: **AGENTE-GATEWAY**. Slug: `agentegateway`. Contrato Laravel: repo **`PaqSuite-IA-TANGO`**.

## Lenguaje

- Agente, Gateway, instalador: **C# / .NET 8** + SignalR
- App: Laravel (repo `PaqSuite-IA-TANGO`)
- Datos Tango: T-SQL (SP parametrizados)
- Piloto: `diagnostics.run` + `auth.login`

## Circuito (obligatorio)

1. Dispatcher: `.cursor/rules/00-dispatcher-agente-gateway.mdc`. Hablá por pasos (`Hacé el paso C1`, `Hacé el paso D`, …).
2. A1 del MVP ya está en `docs/08-control/08-informe-revision-ambiguedad.md`.
3. Una TR por conversación. Orden D10: HU-001 → 002 → 004 → 005 → 006 → 007 → 003 → 008.
4. HU-001 / HU-007 (y Laravel de HU-005/006) en `PaqSuite-IA-TANGO`.
5. No commit ni push salvo pedido.

## Prohibido

- Fallback SQL cuando el tenant **tiene** `agent_id` / usar `host` para consultar en modo agente.
- Tailscale en config, runbooks de producción o `GatewayUrl`.
- `dev-agent-token` como default del instalador.
- SQL libre enviado desde AWS.
- Una clase C# por cada stored procedure (usar handler genérico; excepción: `auth.login`, `diagnostics.run`).
- Ampliar el MVP (auto-update, Redis, N agentes, porte masivo de operaciones).

## Permitido reutilizar del repo actual

Contratos JSON, idea de SignalR, `SqlExecutor` parametrizado, SP `PAQ_Auth_Login` para HU-006. El instalador se rehace con token obligatorio y prueba de gateway (D14).

## Primera respuesta esperada

No escribas código. Confirmá:

1. Que leíste el SPEC v1.1 y el informe `08`.
2. Que no hay bloqueantes de producto abiertos.
3. Cuál es la primera HU (HU-001, en TANGO) cuando te autoricen.

Respondé en español.
