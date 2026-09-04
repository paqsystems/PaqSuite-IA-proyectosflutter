# Scaffold SDD — PaqAgent + PaqGateway (adaptado, no fullstack)

> Base de inspiración: `PaqSuite-IA-BASE/.cursor/docs/02-producto/agente-gateway/scaffold-fullstack-inicio-proyecto.md`  
> **No aplica** MONO/MULTI, Laravel host, React, DevExtreme, Forge/Vercel de producto web, ni Framework SDK GEN.  
> Este repo especifica y construye: **agente Windows + gateway .NET + instalador + contrato Laravel (en TANGO)**.

---

## 1) Invocación (primer mensaje)

```md
Plataforma: PaqSuite-IA-AgenteCliente-PAQ
Modo: AGENTE-GATEWAY (no MONO/MULTI)
Slug: agentegateway
Repos de código .NET: este (rama sdd-reformulacion)
Repo contrato Laravel: PaqSuite-IA-TANGO (rama equivalente)

Quiero el scaffold SDD de documentación + solución .NET mínima según
docs/02-producto/agente-gateway/scaffold-agente-gateway-sdd.md — sin commit/push.
```

Si falta el modo `AGENTE-GATEWAY` o el repo TANGO, preguntar antes de scaffoldear.

---

## 2) Qué se toma del scaffold BASE (espíritu)

| Idea BASE | Adaptación aquí |
|-----------|-----------------|
| Guía normativa + checklist | Este archivo + SPEC `docs/02-producto/` |
| Orden: docs → código → tests | Igual |
| Trazabilidad SPEC → HU → TR | Carpetas `docs/05-open-spec`, `03-historias-usuario`, `04-tareas` |
| Una capacidad E2E primero | Vertical: alta → gateway → agente → diagnostics → auth.login |
| No inventar fuera del SPEC | `01-SPEC-producto.md` manda |
| Symlinks / rules BASE | **Opcional**; solo rules útiles (testing, HU, no frontend DevExtreme) |
| `docs/06-operacion/` | Runbooks Gateway AWS + instalación agente |
| `VERSION` en raíz | Sí |

## 3) Qué NO se copia del scaffold fullstack

- `backend/` Laravel + `frontend/` React en este repo  
- `PAQSUITE_TENANCY` / Dictionary-Company como eje del producto  
- OpenAPI L5-Swagger del host web (Laravel vive en TANGO)  
- Capacitor, DevExtreme, `@paqsuite/react-core`  
- URLs Vercel `*paqsystems.vercel.app` como entregable del agente  

---

## 4) Árbol objetivo de documentación (SDD)

Migrar el contenido actual de `prompts/00–04` hacia `docs/` (dejar `prompts/` para **prompts de agente IA** solamente).

```text
docs/
  00-contexto/
    00-contexto-reformulacion.md          ← desde prompts/00
    README.md                             ← circuito SDD + DoD MVP
  01-arquitectura/
    01-arquitectura-agente-gateway.md     ← diagrama + responsabilidades (extraer de SPEC §2–§6)
  02-producto/
    SPEC-AGW-001-producto.md              ← desde docs/02-producto/agente-gateway/01-SPEC-producto.md
    decisiones-tecnicas.md                ← desde prompts/02
  03-historias-usuario/
    001-Conectividad/
      HU-001-alta-cliente-agente.md
      HU-002-gateway-aws.md
      HU-003-auto-instalador.md
      HU-004-agente-heartbeat.md
      HU-005-diagnostics-run.md
      HU-006-auth-login.md
      HU-007-corte-duro-modo-agente.md
      HU-008-documentacion-instalacion.md
      README.md                           ← orden efectivo D10
  04-tareas/
    001-Conectividad/
      TR-001-alta-empresas-conexion.md      (repo TANGO)
      TR-002-paqgateway-app.md
      TR-003-deploy-gateway-aws.md
      TR-004-auto-instalador.md
      TR-005-paqagent-servicio.md
      TR-006-diagnostics-e2e.md
      TR-007-auth-login-piloto.md
      TR-008-corte-duro-modo-agente.md      (repo TANGO)
      TR-009-docs-instalacion.md
      README.md                           ← mapa HU→TR
  05-open-spec/
    001-Conectividad/
      SPEC-AGW-001-producto.md            ← link o copia canónica
  06-operacion/
    instalacion-agente.md                 ← se escribe con HU-008
    deploy-gateway-aws.md                 ← se escribe con HU-002/TR-003
    urls-deploy.md                        ← gateway.paqsuite.com + notas lab
  08-control/
    08-informe-revision-ambiguedad.md     ← desde prompts/08
```

Prompts de IA (quedan en `prompts/`):

```text
prompts/
  README.md                               ← índice + apunta a docs/
  scaffold-agente-gateway-sdd.md          ← este archivo
  05-prompt-kickoff.md
  06-prompt-ejecutar-hu.md
  07-prompt-revision-ambiguedad.md
  historico/
```

---

## 5) Árbol objetivo de código (.NET) — solución nueva

No pisar el `main` histórico. En rama `sdd-reformulacion`:

```text
src/
  PaqContracts/          # DTOs job/result/heartbeat (traceId, estados)
  PaqGateway/            # ASP.NET Core + SignalR
  PaqAgent/              # Worker Service Windows
  PaqAgentInstaller/     # WinForms
tests/
  PaqGateway.Tests/
  PaqAgent.Tests/
VERSION
README.md
docs/                    # SDD arriba
```

El código viejo (si se trae como referencia) vive fuera de `src/` o en tag/rama `main` sin mezclarse en el camino feliz.

**Contrato Laravel:** no se scaffoldea aquí; las TR-001/007/008 se ejecutan en `PaqSuite-IA-TANGO` con los mismos IDs de HU.

---

## 6) Orden de ejecución del scaffold

1. Crear/actualizar árbol `docs/` y **mover o copiar** contenidos desde `prompts/00–04` + `08` (sin perder historial git si se usa `git mv`).
2. Actualizar `docs/02-producto/agente-gateway/README.md` para que la fuente de verdad apunte a `docs/`.
3. Crear `docs/06-operacion/urls-deploy.md` (placeholder DNS gateway).
4. Crear solution .NET 8 vacía bajo `src/` con los 4 proyectos + test stubs (sin lógica de negocio).
5. Archivo `VERSION` = `0.1.0-mvp`.
6. Checklist §7 abajo en verde / pendiente.

**No** implementar HU-001 en el scaffold: solo estructura.

---

## 7) Checklist de cierre del scaffold

- [ ] Modo `AGENTE-GATEWAY` declarado
- [ ] `docs/00–06` creados con SPEC/HU/TR trazables
- [ ] Orden D10 documentado en `docs/03-historias-usuario/001-Conectividad/README.md`
- [ ] Informe ambigüedad en `docs/08-control/`
- [ ] Solution `src/` con PaqContracts, PaqGateway, PaqAgent, PaqAgentInstaller
- [ ] `VERSION` presente
- [ ] `docs/06-operacion/urls-deploy.md` con `https://gateway.paqsuite.com/agent-hub`
- [ ] Prompts 05/06/07 actualizados a paths `docs/`
- [ ] Nota explícita: trabajo Laravel en repo TANGO
- [ ] Sin Tailscale / sin fallback modo agente en plantillas

---

## 8) Defaults para huecos del informe 08 (si el humano no fija antes)

| Hueco | Default scaffold |
|-------|------------------|
| H3 Token | Columnas en `empresas_conexion` (sin tabla `agents` aún) |
| H4 `last_seen_at` | Autoridad en Gateway; Laravel consulta status API |
| H15 Paralelo | Rama `sdd-reformulacion` + código nuevo en `src/` |
| H8 TTL | Heartbeat 30s, TTL 90s (constantes en PaqContracts/config) |

---

## 9) Prompt literal de uso

```md
Usá docs/02-producto/agente-gateway/scaffold-agente-gateway-sdd.md como fuente normativa del scaffold.

Plataforma: PaqSuite-IA-AgenteCliente-PAQ
Modo: AGENTE-GATEWAY
Slug: agentegateway
Repo Laravel contrato: PaqSuite-IA-TANGO

Ejecutá el scaffold:
1. Armá el árbol docs/ SDD migrando desde prompts/00–04 y 08.
2. Dejá prompts/ solo para prompts de IA (05–07 + este scaffold).
3. Creá la solution .NET 8 vacía bajo src/ (4 proyectos + tests).
4. Completá el checklist §7.
5. No implementes HU todavía. No commit/push.
```
