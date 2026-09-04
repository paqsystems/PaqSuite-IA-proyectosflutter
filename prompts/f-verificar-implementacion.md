# Paso F — Verificar implementación vs documentos

Equivalente BASE `openspec-05`. Comando: `Hacé el paso F` / `Hacer paso F`.

Después de D, E y F1. Contrastar código vs SPEC, HU y TR. **No** modificar archivos salvo que lo pidan. **No** marcar `Finalizado`.

## Entrada

TR (obligatoria). HU y SPEC desde enlaces del TR.

## Dimensiones

**CRÍTICO** / **ADVERTENCIA** / **SUGERENCIA** sobre:

- Completitud (CA y tareas de la TR)
- Corrección (comportamiento = SPEC)
- Coherencia (SPEC ↔ HU ↔ TR ↔ código)

No reemplaza el paso E.

## Salida

`docs/08-control/f-YYYYMMDD-<id-tr>.md`

1. Contexto (rutas)
2. Resumen
3. Completitud / Corrección / Coherencia (✓ ⚠ ✗)
4. Pruebas (cobertura; no re-ejecutar salvo pedido)
5. Próximos pasos
6. Puede el humano marcar Finalizado: Sí / No
