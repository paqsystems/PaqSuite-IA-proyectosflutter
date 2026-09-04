# 00 — Contexto y circuito SDD

| Campo | Valor |
|-------|--------|
| Plataforma | PaqSuite-IA-AgenteCliente-PAQ |
| Modo | **AGENTE-GATEWAY** (no MONO/MULTI) |
| Slug | `agentegateway` |
| Repo .NET | este (`sdd-reformulacion`, código en `src/`) |
| Repo Laravel (contrato) | **PaqSuite-IA-TANGO** — TR-001, TR-007 y TR-008 se ejecutan allí |

---

## Circuito de trabajo (obligatorio)

```text
A  SPEC desde contexto
        │
        ▼
A1 Ambigüedad del SPEC
        │
        ▼
B  HU desde SPEC  →  B1 Enriquecer HU
        │
        ▼
C  TR desde SPEC+HU  →  C1 Ambigüedad de la TR
        │
        ▼
D1 Planificar  →  D Ejecutar TR  →  E Tests  →  F1 Evidencia  →  F Docs↔código
```

Comando: `Hacé el paso A` … `Hacé el paso F`. Dispatcher: [`.cursor/rules/00-dispatcher-agente-gateway.mdc`](../../.cursor/rules/00-dispatcher-agente-gateway.mdc).

**Prohibido:** implementar operaciones de negocio (acopios, informes, etc.) antes de que el MVP de conectividad esté aceptado. **Prohibido:** Tailscale o fallback SQL en plantillas de modo agente.

---

## Definición de terminado del MVP

Un cliente Tango en **modo agente**, **sin Tailscale y sin IP pública obligatoria en `empresas_conexion`**, puede:

1. Conectar el agente (lab: `appsettings` manual; cierre: auto-instalador).
2. Ver el servicio Windows corriendo.
3. Aparecer online en PaqSuite.
4. Ejecutar `diagnostics.run` y la operación piloto live **`auth.login`**.
5. Si el agente cae: Laravel responde `AGENT_OFFLINE` **sin** caer a SQL por IP.

Los tenants aún sin `agent_id` pueden seguir en SQL directo hasta la transformación total. Si el piloto agente no está verde, el MVP de conectividad no está cerrado.

El trabajo Laravel **no vive en este repo**. Se especifica aquí (mismos IDs de HU/TR) y se implementa en `PaqSuite-IA-TANGO`.

---

## Índice de esta carpeta

| Archivo | Para qué |
|---------|----------|
| [00-contexto-reformulacion.md](00-contexto-reformulacion.md) | Por qué se reformula, qué falló, objetivo real |
| [../02-producto/fases-roadmap.md](../02-producto/fases-roadmap.md) | Fase 1 MVP caño · Fase 2 update agente · Fase 3 objetos SQL |
