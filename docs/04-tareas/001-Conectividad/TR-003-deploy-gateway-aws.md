# TR-003 — Deploy Gateway en AWS

| Campo | Valor |
|-------|--------|
| TR | TR-003 |
| Estado | Especificado |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) (CA 9–13) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6.1 |
| **Repo** | **este** (`docs/06-operacion/` + artefactos publish) + **cuenta AWS** |
| Orden D10 | 2 |
| Dependencia | [TR-002](TR-002-paqgateway-app.md) **Finalizado** |
| C1 | [c1-20260905-TR-003.md](../../08-control/c1-20260905-TR-003.md) — Apto; N1–N6 cerrados |
| Runbook | [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) · [urls-deploy.md](../../06-operacion/urls-deploy.md) |

Completa publish/systemd/TLS sobre el checklist AWS. **No** reescribe el producto Gateway (TR-002).

### Decisiones cerradas (post-C1, 2026-09-05)

| ID | Tema | Decisión |
|----|------|----------|
| N1 | Compute | **EC2** dedicada (ya referida en runbook). |
| N2 | TLS edge | **Nginx en la EC2** (+ cert). ALB solo si ya está en la cuenta; documentar en Traza. |
| N3 | DNS | `gateway.paqsuite.com` → EIP/IP pública (o ALB). |
| N4 | Kestrel | `ASPNETCORE_URLS=http://127.0.0.1:5100`; Nginx 443→5100 con WebSocket Upgrade. Puerto 5100 **solo** VPC/SG Laravel; no 1433 a Internet. |
| N5 | Stub Dev | `Gateway__UseDevAuthStub` ausente/`false` en producción. |
| N6 | Publish | `dotnet publish src/PaqGateway -c Release -o <dir>`; systemd `paqgateway.service`; ruta canónica documentada en runbook (default sugerido `/opt/paqgateway`). |

### Tareas

- [ ] `dotnet publish` Release; documentar ruta de artefactos en runbook.
- [ ] EC2 misma VPC que Laravel; SG: 443 público (Nginx); 5100 solo VPC/Laravel; **sin** 1433 a Internet.
- [ ] systemd `paqgateway.service` + Nginx TLS + Upgrade WebSocket + DNS `gateway.paqsuite.com`.
- [ ] Env: `Gateway__InternalApiKey`, `LaravelApi__BaseUrl` (URL **privada**), `LaravelApi__InternalApiKey`; N5; sin `change-me-in-production`.
- [ ] Verificar WSS `https://gateway.paqsuite.com/agent-hub` desde máquina **fuera** de Tailscale.
- [ ] Desde host Laravel (red privada): `GET http://<privado>:5100/internal/agents/{id}/status` con API key (o vía ruta interna documentada).
- [ ] Completar [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) con pasos publish/systemd/Nginx reales (narrativa amplia = TR-009).
- [ ] Sin Tailscale como producto. Sin SQL a Internet.

### Traza (completar en D)

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Laravel → Gateway solo URL interna VPC. |
| Pendientes | H12 si faltan VPC/DNS. |

Siguiente: **D1** → D.
