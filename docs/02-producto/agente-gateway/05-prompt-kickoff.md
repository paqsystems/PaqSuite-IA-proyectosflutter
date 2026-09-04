# 05 — Prompt de kickoff (desarrollo paralelo)

Copiar este bloque **entero** en una conversación nueva (rama `sdd-reformulacion` o worktree limpio). No implementar en el mismo turno en que se hace la revisión de ambigüedad.

---

Sos un ingeniero senior. Vas a **reformular** PaqAgent + PaqGateway en paralelo al código existente, con SDD.

## Fuente de verdad (leer en este orden, sin saltearse)

1. `docs/02-producto/agente-gateway/README.md`
2. `docs/02-producto/agente-gateway/00-contexto-reformulacion.md`
3. `docs/02-producto/agente-gateway/01-SPEC-producto.md`
4. `docs/02-producto/agente-gateway/02-decisiones-tecnicas.md`
5. `docs/02-producto/agente-gateway/03-historias-usuario.md`
6. `docs/02-producto/agente-gateway/04-tareas-mvp.md`
7. `docs/02-producto/agente-gateway/08-informe-revision-ambiguedad.md` (si existe)

El código actual de `PaqAgent/`, `PaqGateway/` y `PaqAgentInstaller/` es **referencia**, no la especificación. Si el código contradice el SPEC, gana el SPEC.

## Objetivo del producto (no lo negociés)

Agente Windows en el servidor SQL del cliente + Gateway en Amazon. En **modo agente**, Laravel en AWS **no** se conecta a SQL remoto. `empresas_conexion` guarda `agent_id` / `client_id` / token, **no** la IP del cliente como llave. Tailscale no es parte del producto. Tenants **sin** `agent_id` pueden seguir en SQL directo hasta la transformación total (D5).

## Lenguaje

- Agente, Gateway, instalador: **C# / .NET 8** + SignalR
- App: Laravel (repo `PaqSuite-IA-TANGO`)
- Datos Tango: T-SQL (SP parametrizados)
- Piloto: `diagnostics.run` + `auth.login`

## Circuito (obligatorio)

1. La revisión de ambigüedad (`07` → `08`) ya debe estar hecha. Si aparecen dudas nuevas de producto, **pará** y listalas. No codees.
2. Implementá **una** HU por conversación usando `docs/02-producto/agente-gateway/06-prompt-ejecutar-hu.md`.
3. Orden efectivo (D10): HU-001 → HU-002 → HU-004 → HU-005 → HU-006 → HU-007 → HU-003 → HU-008. Lab con `appsettings` manual antes del instalador.
4. No hagas commit ni push salvo que te lo pidan.

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
3. Cuál es la primera HU (HU-001) cuando te autoricen.

Respondé en español.
