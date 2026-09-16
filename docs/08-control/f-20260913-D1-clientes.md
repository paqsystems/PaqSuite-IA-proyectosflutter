# Paso F — D1 piloto clientes (Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-13 |
| Repo | PaqSuite-IA-AgenteCliente-PAQ |
| Alcance | Whitelist + SP `PAQ_Clientes_*` |
| F1 | [f1-20260913-D1-clientes.md](./f1-20260913-D1-clientes.md) |
| Kickoff host | TANGO `docs/02-producto/_Integracion-Framework-Agente/Dominio-d1-01` |

## Resumen

Primera familia D1 en Agente PAQ: `clientes.buscar` / `clientes.obtener` con `_database` → InitialCatalog company, sin SQL libre.

## Completitud

| Ítem | Veredicto |
|------|-----------|
| JobOperations + connector | ✓ |
| Runners + Sql*SpExecutor | ✓ |
| Scripts company | ✓ |
| Tests unitarios | ✓ |
| Smoke lab | ✗ pendiente deploy |

## Próximos pasos

1. Aplicar SQL company en lab + reiniciar agente.
2. Smoke Tango `GET /api/v1/clientes/buscar` con `agw.` + `X-Company-Id`.
3. Siguiente familia D1 (p. ej. `articulos.*`).

## Puede el humano marcar Finalizado

**Sí** tras smoke lab (o dejar Pendiente de Revisión hasta deploy).
