# TR-009 — Documentación de instalación

| Campo | Valor |
|-------|--------|
| TR | TR-009 |
| Estado | Pendiente de Revisión |
| HU | [HU-008](../../03-historias-usuario/001-Conectividad/HU-008-documentacion-instalacion.md) |
| Repo | este (`docs/06-operacion/`) |
| Orden D10 | 8 |
| C1 | [c1-20260906-TR-009.md](../../08-control/c1-20260906-TR-009.md) — Apto; Q1–Q6 |
| F1 | [f1-20260906-TR-009.md](../../08-control/f1-20260906-TR-009.md) — Aprobado con observaciones |
| F | [f-20260906-TR-009.md](../../08-control/f-20260906-TR-009.md) — apto Finalizado (salvedad release) |

Los documentos `instalacion-agente.md` y `deploy-gateway-aws.md` quedan como instructivos operativos (HU-003 instalador verde + Gateway AWS ya desplegado).

### Tareas

- [x] Completar `docs/06-operacion/instalacion-agente.md` como instructivo cliente: URL **pública** de descarga, SHA256, prerrequisitos, campos, verificación, troubleshooting.
- [x] Completar `docs/06-operacion/deploy-gateway-aws.md` con publish/systemd (§10) + prueba funcional + “qué no” (§11–12).
- [x] Asegurar que [urls-deploy.md](../../06-operacion/urls-deploy.md) apunte al canal público vigente (`PaqSuite-IA-proyectosflutter` / `releases/latest`).
- [x] README del repo alineado (ops + tests; sin “placeholders” de instalador).
- [x] Checklist de alta de cliente: 10 pasos, copiable (en `instalacion-agente.md`).
- [x] Qué **no** configurar: Tailscale, IP pública, puerto 1433 a Internet, fallback SQL modo agente.

### Traza

| | |
|--|--|
| Archivos | `instalacion-agente.md`; `urls-deploy.md`; `deploy-gateway-aws.md` (§11–13); `README.md`; `c1-20260906-TR-009.md` |
| Comandos | N/A (docs) |
| Notas | C1 Apto 2026-09-06. Asset zip puede aún no existir en `latest` — instructivo describe el canal. |
| Pendientes | Publicar release GitHub con zip+SHA256; Finalizado cuando lo autoricen |

Siguiente: humano **Finalizado** si acepta salvedad release.
