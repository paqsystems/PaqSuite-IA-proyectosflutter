# 07 — Prompt de revisión de ambigüedad (SPEC → HU)

Correr **antes** de la primera línea de código del desarrollo paralelo. No genera HU nuevas: audita el SPEC.

---

Actuá como revisor de SPEC (no como implementador).

Leé en este orden:

- `docs/02-producto/agente-gateway/00-contexto-reformulacion.md`
- `docs/02-producto/agente-gateway/01-SPEC-producto.md`
- `docs/02-producto/agente-gateway/02-decisiones-tecnicas.md`
- `docs/02-producto/agente-gateway/03-historias-usuario.md`

## Tarea

1. Listá **ambigüedades** (dos lecturas posibles del mismo párrafo).
2. Listá **huecos** (el implementador tendría que inventar).
3. Listá **contradicciones** entre SPEC, decisiones y HU.
4. Listá **supuestos** que el SPEC da por cerrados y podrían ser falsos (DNS, VPC, permisos SQL, un solo agente por tenant, etc.).
5. Clasificá cada ítem: `bloquea MVP` | `no bloquea (se puede decidir en la TR)`.
6. **No** propongas features nuevas (auto-update, más operaciones, Redis, app móvil…).
7. **No** escribas código ni reescribas el SPEC entero. Si hace falta un cambio de redacción, sugerí el párrafo exacto.

## Preguntas que ya están cerradas (no las reabras)

- Laravel no usa la IP del cliente para consultar SQL **en modo agente**.
- Tailscale no es requisito de producto.
- Lenguaje del agente/gateway/instalador: C# .NET 8 + SignalR.
- El instalador pide AgentToken; no hay default de desarrollo en producción.
- Modo agente: sin fallback SQL directo. Tenants sin `agent_id`: SQL directo permitido en MVP hasta transformación total (D5).
- Orden: vertical con `appsettings` manual primero; instalador GUI después (D10).
- Estados de job: success | failed | timeout | offline | degraded | cancelled (D12).
- Instalador: prueba gateway; fallo aborta sin servicio; override opcional (D14).
- Un agente activo por tenant (D13).
- Piloto de negocio: `auth.login` (D15).
- Online = heartbeat + TTL; `last_seen_ip` solo auditoría (D16).
- Vocabulario: `cliente` / `X-Paq-Cliente` / `activo` / SignalR (D17).
- Contrato job incluye `traceId`; jobs en vuelo al restart → `cancelled`.
- SHA256 del instalador en cada release.

## Salida

Un informe corto en español: tabla de hallazgos + “¿se puede implementar el MVP o hay que devolver el SPEC al humano?”.
