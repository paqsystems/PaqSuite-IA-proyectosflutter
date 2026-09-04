# Paso B — HU desde SPEC

Equivalente BASE `openspec-02`. Comando: `Hacé el paso B` / `Hacer paso B`.

Requiere SPEC (paso A) y A1 no bloqueante. **No** TR ni código.

## Entrada

SPEC: default `docs/02-producto/SPEC-AGW-001-producto.md` (o la ruta que indiquen).

## Salida

`docs/03-historias-usuario/<epica>/HU-XXX-slug.md`

Mínimo: metadatos (`Estado: Pendiente`, SPEC, repo este|TANGO), narrativa, CA, Gherkin si el SPEC lo implica. Foco funcional, sin detalle de TR.

Una capacidad observable por HU. Actualizar el SPEC con enlace a la HU.

Si el SPEC contradice una HU vieja, gana el SPEC; no ampliar alcance.
