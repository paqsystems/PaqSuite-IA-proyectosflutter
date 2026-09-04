# Paso A — SPEC desde contexto

Equivalente BASE `openspec-01`. Comando: `Hacé el paso A` / `Hacer paso A`.

Rol: analista de alcance. **No** generar HU, TR ni código.

## Rutas (este repo)

- Fuente: `docs/02-producto/` (o notas/ticket del mensaje).
- Canónico: `docs/02-producto/SPEC-AGW-XXX-slug.md`
- Trazabilidad: `docs/05-open-spec/<epica>/SPEC-AGW-XXX-slug.md` (puntero o copia).

Forma corta: `Creá el SPEC 001-Conectividad según agente-gateway` → leer `docs/02-producto/` (y `agente-gateway/` si aplica) y escribir SPEC-AGW bajo producto + open-spec.

## Hacer

1. Leer decisiones vigentes `docs/02-producto/decisiones-tecnicas.md`.
2. Crear o actualizar el SPEC (in/out, actores, contratos, CA, riesgos).
3. Metadatos: `Estado: Pendiente`; repos (este vs TANGO); enlaces HU/TR si ya existen.
4. Preguntas abiertas en el SPEC, no inventar.
5. Sin Tailscale ni fallback SQL modo agente. Sin GEN/Framework.

Tras A sigue **paso A1**.
