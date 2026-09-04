# Paso C — TR desde SPEC + HU

Equivalente BASE `openspec-03`. Comando: `Hacé el paso C` / `Hacer paso C`.

Requiere SPEC + HU (B/B1). **No** código.

Si SPEC y HU discrepan: manda el SPEC para alcance; anotar **Discrepancias** en la TR.

## Salida

`docs/04-tareas/<epica>/TR-XXX-slug.md`

Metadatos: HU, SPEC, `Estado: Pendiente`, **Repo** (este / TANGO / ambos), orden D10 si es MVP conectividad.

Tareas checklist verificables. Traza vacía para completar en el paso D.

Contratos de este producto: `traceId`, estados D12, hub `/agent-hub`, TTL 90 s, ruteo por `agent_id`. Sin Tailscale ni fallback modo agente.

Enlazar TR en metadatos de HU y SPEC.
