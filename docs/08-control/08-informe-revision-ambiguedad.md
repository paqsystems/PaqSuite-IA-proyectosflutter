# 08 — Informe de revisión de ambigüedad (SPEC → HU)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-03 |
| Origen | `07-prompt-revision-ambiguedad.md` |
| Fuentes | `00`, `01` SPEC v1.1, `02`, `03` |
| Veredicto | **Se puede implementar el MVP** tras corregir 1 contradicción de redacción (aplicada) y actualizar kickoff. Resto = decidir en TR. |

---

## Resumen

No hay ambigüedad de **producto** que reabra D1–D6. Hay huecos de **implementación** normales y una contradicción residual en HU-001 §5 vs D5 (SQL directo en tenants sin `agent_id`), ya corregida en este mismo ciclo.

---

## Hallazgos

| ID | Tipo | Ítem | Clasificación | Nota |
|----|------|------|---------------|------|
| H1 | Contradicción | HU-001 criterio 5 decía que sin `agent_id` “no se define SQL directo”; D5/SPEC permiten SQL directo legacy en transición | ~~bloquea~~ **corregido** | Alineado a D5 |
| H2 | Contradicción | `05-prompt-kickoff.md` aún ordenaba HU-001…008 lineal y prohibía fallback en absoluto | ~~bloquea~~ **corregido** | Alineado a D10/D5 |
| H3 | Ambigüedad | Token: ¿columna en `empresas_conexion` o tabla `agents` 1:1? (D4 dice “o”) | no bloquea | Decidir en TR-001; default sugerido: columnas en `empresas_conexion` MVP |
| H4 | Ambigüedad | `last_seen_at`: ¿persistido en Laravel o solo memoria Gateway? SPEC lo lista en tabla pero “runtime” | no bloquea | TR-002/001: Gateway autoridad del online; sync a Laravel opcional vía status API |
| H5 | Ambigüedad | `status` persistido vs derivado | no bloquea | Derivado de TTL + readiness; no columna obligatoria MVP |
| H6 | Ambigüedad | Auth del Gateway: ¿callback Laravel o lectura de catálogo? | no bloquea | TR-002: callback Laravel con cache corta (D4) |
| H7 | Ambigüedad | `schema_ready`: ¿qué prueba exactamente en MVP? | no bloquea | TR-006: existencia/ejecutable de SP piloto o ping trivial documentado |
| H8 | Hueco | Valor numérico de TTL (solo “2–3× heartbeat”) | no bloquea | TR-002: p. ej. heartbeat 30s, TTL 90s |
| H9 | Hueco | Nombres exactos de parámetros de `PAQ_Auth_Login` (ejemplo usa `codigo`/`clave`) | no bloquea | TR-007: leer SP existente / contrato Laravel actual |
| H10 | Hueco | EC2 vs “equivalente”; Nginx vs ALB | no bloquea | TR-003 |
| H11 | Hueco | ¿Quién escribe `last_seen_ip` en AWS? | no bloquea | TR-002: Gateway lo observa; exposición a Laravel vía status |
| H12 | Supuesto | DNS `gateway.paqsuite.com` y VPC compartida con Laravel existen o se pueden crear | no bloquea* | *Bloquea deploy real (TR-003), no el código de Gateway/Agente en lab |
| H13 | Supuesto | Permisos SQL del usuario del agente alcanzan para ejecutar `PAQ_Auth_Login` | no bloquea | Verificar en lab del piloto |
| H14 | Supuesto | Un solo agente por tenant alcanza el piloto (D13) | cerrado | OK |
| H15 | Supuesto | Paralelismo: rama `sdd-reformulacion` vs carpeta limpia (00 §4) | no bloquea | Cerrar en scaffold (recomendado: rama + solución nueva bajo `src/`) |

---

## ¿Se puede implementar el MVP?

**Sí.** El SPEC está cerrado para producto. No devolver al humano por dudas de arquitectura.

Antes de la primera línea de código de producto:

1. ~~Corregir HU-001 / kickoff~~ (hecho).
2. Armar scaffold SDD de carpetas (siguiente paso).
3. Empezar por TR-001 / HU-001 cuando autoricen.

Preguntas al humano **solo si** quiere fijar ahora (opcionales): H3 (tabla `agents` vs columnas) y H15 (rama vs carpeta). Si no, el scaffold propone default.

---

## Defaults aplicados en el scaffold (2026-09-04)

El humano no fijó H3/H4/H8/H15 antes del scaffold. Quedan cerrados así (también en D18):

| Hueco | Default aplicado |
|-------|------------------|
| H3 Token | Columnas en `empresas_conexion` (sin tabla `agents` aún) |
| H4 `last_seen_at` | Autoridad en Gateway; Laravel consulta status API |
| H15 Paralelo | Rama `sdd-reformulacion` + código nuevo en `src/` |
| H8 TTL | Heartbeat 30 s, TTL 90 s (`PaqContracts.AgentDefaults`) |

El árbol SDD canónico es `docs/` (no `prompts/00–04`). Prompts de IA: `prompts/05–07` + scaffold. Solution vacía en `src/` + `tests/`. **Sin implementación de HU.** Trabajo Laravel: repo `PaqSuite-IA-TANGO`.
