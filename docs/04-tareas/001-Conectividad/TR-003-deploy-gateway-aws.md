# TR-003 — Deploy Gateway en AWS

| Campo | Valor |
|-------|--------|
| TR | TR-003 |
| Estado | Pendiente de Revisión |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) (CA 9–13) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6.1 |
| **Repo** | **este** (`docs/06-operacion/` + plantillas) + **cuenta AWS** (ops humano) |
| Orden D10 | 2 |
| Dependencia | [TR-002](TR-002-paqgateway-app.md) **Finalizado** |
| C1 | [c1-20260905-TR-003.md](../../08-control/c1-20260905-TR-003.md) — Apto; N1–N6 |
| D1 | [d1-20260905-TR-003.md](../../08-control/d1-20260905-TR-003.md) — confirmado |
| Runbook | [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) · [deploy/](../../06-operacion/deploy/) |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| N1 | Compute | EC2 |
| N2 | TLS | Nginx en la EC2 |
| N3 | DNS | `gateway.paqsystems.com` → EIP (ops; SPEC decía paqsuite.com) |
| N4 | Kestrel | `0.0.0.0:5100` + SG Laravel; `/internal` no en Nginx público |
| N5 | Stub | `UseDevAuthStub=false` en prod |
| N6 | Publish | `artifacts/paqgateway` → `/opt/paqgateway` + systemd |

### Tareas

- [x] `dotnet publish` Release documentado; smoke local OK (`artifacts/paqgateway`).
- [x] Plantillas SG/red/systemd/Nginx/env en runbook (aplicación en EC2 = ops).
- [x] systemd `paqgateway.service` + Nginx TLS/Upgrade + DNS documentados.
- [x] Env prod documentado (N5); sin `change-me-in-production` en plantillas.
- [ ] Verificar WSS público fuera de Tailscale (**humano / AWS**).
- [ ] Internal desde Laravel en VPC (**humano / AWS**).
- [x] Runbook [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) con pasos §10 reales.
- [x] Sin Tailscale / sin SQL a Internet (documentado).

### Traza

| | |
|--|--|
| Archivos | `docs/06-operacion/deploy-gateway-aws.md`; `docs/06-operacion/urls-deploy.md`; `docs/06-operacion/deploy/paqgateway.service`; `nginx-gateway.conf`; `env.example`; `README.md` |
| Comandos | `dotnet publish src/PaqGateway -c Release -o artifacts/paqgateway` (OK 2026-09-05) |
| Notas | CA 9–13: runbook+plantillas listos. Cierre e2e AWS pendiente de ficha §9 + checklist §8 en la EC2 real. Auth Laravel authenticate puede bloquear WSS con token real hasta TANGO. |
| Pendientes | Aplicar en EC2 (humano). Completar §8/§9. F1/F. H12 si faltan VPC/DNS. |

Siguiente: ops aplica runbook en AWS → **F1** / **F**. Luego se puede Finalizar HU-002.
