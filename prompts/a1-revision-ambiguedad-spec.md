# A1 — Revisión de ambigüedad del SPEC

Usar **después** de crear o cambiar un SPEC (paso A) y **antes** de HU (paso B). No implementa. No inventa requisitos.

Comando: `Hacé el paso A1` / `Hacer paso A1` / `Revisá la ambigüedad del SPEC [ruta]`

Pasada de **todo** el MVP (ya hecha): `prompts/07-prompt-revision-ambiguedad.md`. Esta A1 es **incremental** (un SPEC o un delta).

---

Actuá como revisor de SPEC (no como implementador).

## Entrada

- SPEC: por defecto `docs/02-producto/SPEC-AGW-001-producto.md`
- Decisiones: `docs/02-producto/decisiones-tecnicas.md`
- Arquitectura: `docs/01-arquitectura/01-arquitectura-agente-gateway.md`
- Informe previo: `docs/08-control/08-informe-revision-ambiguedad.md` (no reabrir ítems cerrados)

## Tarea

1. Ambigüedades (dos lecturas del mismo párrafo).
2. Huecos (el implementador tendría que inventar).
3. Contradicciones SPEC ↔ decisiones ↔ HU existentes.
4. Supuestos que podrían ser falsos.
5. Clasificar cada ítem: `bloquea` | `no bloquea (TR)`.
6. No proponer features nuevas. No codear. Si hace falta redacción, sugerir el párrafo exacto.

## Cerrado (no reabrir)

Laravel no usa IP en modo agente. Tailscale no es producto. C# .NET 8 + SignalR. Sin fallback SQL con `agent_id`. D10/D12–D17. H3/H4/H8/H15 (defaults scaffold). Laravel en TANGO.

## Salida

Archivo `docs/08-control/a1-YYYYMMDD-<id-spec>.md` (además del resumen en el chat):

```md
# Revisión de ambigüedad - [SPEC]

## Resultado general
- Estado: Apto / Apto con observaciones / No apto

## Ambigüedades críticas
## Ambigüedades menores
## Supuestos detectados
## Preguntas para decisión humana
## Recomendaciones de ajuste del SPEC

## Veredicto
- Puede pasar a HU / D: Sí / No
```

Respondé en español.
