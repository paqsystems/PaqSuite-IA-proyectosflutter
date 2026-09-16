# SPEC-AGW-002 — Ciclo de vida SQL + update agente (placeholder)

| Campo | Valor |
|-------|--------|
| Identificador | SPEC-AGW-002 |
| Estado | Placeholder — **no** implementar. Paso A pendiente |
| Dependencia | [SPEC-AGW-001](../SPEC-AGW-001-producto.md) (MVP conectividad / **Fase 1**) aceptado |
| Mapa de fases | [../fases-roadmap.md](../fases-roadmap.md) — Fase 2 = update binario; Fase 3 = objetos SQL; **un** SPEC, implementación por slices |

## Insumos listos para Paso A (ambos frentes)

Acuerdo 2026-09-13: oleadas, handshake, staff TANGO, D9, Forge, oleada 0 **y** paquete SQL en el mismo exe (MULTI/MONO, install vs update, alta empresa).

| Documento | Uso |
|-----------|-----|
| [circuito-actualizacion-agente-funcional.md](../circuito-actualizacion-agente-funcional.md) | Relato funcionales: binario |
| [circuito-objetos-sql-agente-funcional.md](../circuito-objetos-sql-agente-funcional.md) | Relato funcionales: tablas, seeds, SP |
| [insumo-spec-agw-002-update-agente.md](insumo-spec-agw-002-update-agente.md) | Fuente Paso A — frente C |
| [insumo-spec-agw-002-objetos-sql.md](insumo-spec-agw-002-objetos-sql.md) | Fuente Paso A — frente A |

Análisis previo de tres frentes (A SQL / B Laravel / C binario): [plan-ciclo-sql-y-updates.md](plan-ciclo-sql-y-updates.md). El Paso A **unifica A+C** en este SPEC. Frente B (deploy Laravel) sigue siendo Forge; no migra el SQL del tenant modo agente.

Este archivo se convierte en SPEC formal (o se reemplaza por `SPEC-AGW-002-….md` canónico en `docs/02-producto/`) cuando corran Paso A → A1. Hasta entonces no hay HU/TR de update ni de runner SQL.
