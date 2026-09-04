# Paso F1 — Verificación del agente (evidencia)

Equivalente BASE `/agent-verification-guide`. Comando: `Hacé el paso F1` / `Hacer paso F1`.

Después de D (y E si hubo tests). **Antes** del paso F. No implementar la HU siguiente. No marcar `Finalizado`.

## Pregunta

¿Hay evidencia real de que la TR está hecha, o es un cierre falso?

## Revisar

1. Alcance: pedido cubierto, nada extra
2. Repo correcto (este vs TANGO)
3. Archivos = plan/Traza
4. Tests corridos **o** declaración explícita de por qué no
5. Traza (archivos, comandos, pendientes)
6. Sin Tailscale, fallback modo agente ni secretos en git/logs

## Salida

`docs/08-control/f1-YYYYMMDD-<id-tr>.md`

```md
# Verificación del agente - [TR]

## Resultado
- Aprobado / Aprobado con observaciones / No aprobado

## Evidencia revisada
## Hallazgos críticos
## Advertencias
## Tests
## Pendientes
## Recomendación final
```

Prohibido “debería funcionar”. Si no se corrió un test, decirlo.
