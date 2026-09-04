# Paso D1 — Planificar la TR

Equivalente BASE `/ai-planning-mode`. Comando: `Hacé el paso D1` / `Hacer paso D1`.

**No codear.** Obligatorio si la HU es COMPLEJA (HU-002, HU-003). Si C1 no es apto, no planificar implementación: volver a C.

## Entrada

TR + HU + SPEC. Código existente en `src/` o TANGO según el campo Repo.

## Salida (en el chat; no sustituye la TR)

```md
# Plan de implementación - [TR]

## Tipo de trabajo
Original

## Alcance entendido
## Artefacto gobernante
SPEC
## Fuentes leídas
## Impacto esperado
(este repo: PaqContracts / PaqGateway / PaqAgent / PaqAgentInstaller / tests / docs)
(TANGO: Laravel / empresas_conexion — no tocar PHP aquí)
## Orden de trabajo
## Riesgos
## Tests a ejecutar
## Dudas / bloqueos
## Confirmación de alcance
```

Parar si hay contradicción SPEC/HU/TR o falta un dato crítico.
