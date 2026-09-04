# C1 — Revisión de ambigüedad de la TR

Usar **después** del paso C y **antes** de D1/D. No implementa. No inventa requisitos.

Comando: `Hacé el paso C1` / `Hacer paso C1` / `Revisá la ambigüedad de la TR [ruta]`

---

Actuá como revisor de TR (no como implementador).

## Entrada obligatoria

- TR en `docs/04-tareas/001-Conectividad/`
- HU enlazada
- SPEC: `docs/02-producto/SPEC-AGW-001-producto.md`
- Decisiones: `docs/02-producto/decisiones-tecnicas.md`

## Tarea

Confirmar que la TR es **implementable sin interpretar**. Recorrer el checklist de `.cursor/rules/16-tr-ambiguity-review.mdc`.

En particular: **en qué repo se codea** (este vs `PaqSuite-IA-TANGO`); contratos job (`traceId`, estados D12); online = TTL 90 s; sin Tailscale ni fallback modo agente.

## Salida

Archivo `docs/08-control/c1-YYYYMMDD-<id-tr>.md` (además del resumen en el chat):

```md
# Revisión de ambigüedad - [TR]

## Resultado general
- Estado: Apto / Apto con observaciones / No apto

## Ambigüedades críticas
## Ambigüedades menores
## Contradicciones TR ↔ HU ↔ SPEC
## Supuestos detectados
## Preguntas para decisión humana
## Recomendaciones de ajuste de la TR
## Repo de implementación
- este | TANGO | ambos

## Veredicto
- Puede pasar a D1/D: Sí / No
```

Si el veredicto es Sí y el alcance está cerrado, podés proponer `Estado: Especificado` en HU y TR (no `Finalizado`).

Respondé en español.
