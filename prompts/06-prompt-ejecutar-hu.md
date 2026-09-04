# 06 — Paso D — Ejecutar una TR

Comando: `Hacé el paso D` / `Hacer paso D` / `Ejecutá la TR [ruta]`.

Usar **después** de C1 apto (y D1 si la HU es COMPLEJA). Una TR por conversación.

Antes de codear: si no hubo **paso C1** en esta conversación, ejecutarlo o confirmar `docs/08-control/c1-*` apto para esa TR.

---

Actuá como ingeniero senior. Implementá **solo** esta historia y su TR.

## Historia

- HU: `HU-00X — [título]`
- TR: `TR-00X`
- Repo: `[PaqSuite-IA-AgenteCliente-PAQ | PaqSuite-IA-TANGO]`
- Documentos:
  - SPEC: `docs/02-producto/SPEC-AGW-001-producto.md`
  - Decisiones: `docs/02-producto/decisiones-tecnicas.md`
  - HU: `docs/03-historias-usuario/001-Conectividad/HU-00X-*.md`
  - TR: `docs/04-tareas/001-Conectividad/TR-00X-*.md`
  - Orden D10: `docs/03-historias-usuario/001-Conectividad/README.md`

## Reglas

1. Usá **exclusivamente** el SPEC y la HU como alcance. Si falta un dato, declaralo supuesto o preguntá; no inventes features.
2. Lenguaje: C# / .NET 8 en este repo; PHP Laravel en TANGO. Miembros y JSON en camelCase.
3. No Tailscale. No fallback SQL en modo agente. No token default. No SQL libre.
4. TR-001, TR-007 y TR-008 (Laravel) se ejecutan en **`PaqSuite-IA-TANGO`**, no en este repo.
5. Al terminar:
   - Marcá los checkboxes de la TR que cumpliste.
   - Completá la tabla Traza (archivos, comandos, notas, pendientes).
   - Poné **Estado: Pendiente de Revisión** en la TR (no `Finalizado`).
   - Listá cómo verificaste cada criterio de aceptación de la HU.
6. No implementes la HU siguiente. Siguen **paso F1** y **paso F**.
7. No commit/push sin pedido explícito.

## Entrega

- Código necesario para cerrar la HU.
- Tests o prueba e2e descrita (si es infra: comandos reales).
- Lo que quedó fuera, en Pendientes de la TR, no en código “por las dudas”.

Respondé en español.
