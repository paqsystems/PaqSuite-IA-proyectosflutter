# TR-003 — Deploy Gateway en AWS

| Campo | Valor |
|-------|--------|
| TR | TR-003 |
| Estado | Pendiente |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) (CA 9–13) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §6.1 |
| **Repo** | **este** (`docs/06-operacion/` + artefactos publish) + **cuenta AWS** |
| Orden D10 | 2 (después de tener binario TR-002 publicable; checklist AWS ya en runbook) |
| Dependencia | [TR-002](TR-002-paqgateway-app.md) en estado usable (lab verde) |
| Runbook | [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) · [urls-deploy.md](../../06-operacion/urls-deploy.md) |

Completa el publish/systemd/TLS sobre el checklist de definición AWS ya existente. **No** reescribe el producto Gateway (eso es TR-002).

### Defaults / supuestos (cerrar en C1 de TR-003)

| ID | Tema | Default / nota |
|----|------|----------------|
| N1 | Compute | EC2 (o equivalente ya usado por PaqSystems); C1 no inventa proveedor nuevo |
| N2 | TLS edge | Nginx **o** ALB — elegir el que ya use la VPC Laravel; documentar el elegido en Traza |
| N3 | DNS | `gateway.paqsuite.com` → edge TLS (H12: bloquea cierre real si DNS/VPC no existen) |

### Tareas

- [ ] `dotnet publish` Release de `PaqGateway`; artefactos versionados / ruta documentada en runbook.
- [ ] Instancia en **misma VPC** que Laravel; Security Group: 443 desde Internet (hub); Kestrel/`/internal/*` solo desde SG/CIDR Laravel; **1433 no** a Internet.
- [ ] systemd (o servicio equivalente) + reverse proxy TLS + certificado + DNS `gateway.paqsuite.com`.
- [ ] Env alineado a TR-002: `Gateway__InternalApiKey`, `LaravelApi__BaseUrl` (**URL privada** de Laravel), `LaravelApi__InternalApiKey`; sin `change-me-in-production` en el servidor.
- [ ] Verificar WSS a `https://gateway.paqsuite.com/agent-hub` desde máquina **fuera** de Tailscale (salida 443).
- [ ] Desde host Laravel (red privada): `GET /internal/agents/{id}/status` con API key (health del camino interno).
- [ ] Actualizar [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) con pasos de publish reales (cierre narrativo amplio = TR-009).
- [ ] Sin Tailscale como camino de producto. Sin abrir SQL a Internet.

### Traza (completar en D)

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Laravel → Gateway solo por URL interna VPC. |
| Pendientes | |

Siguiente tras TR-002 C1/D: **C1** de TR-003 cuando toque deploy (puede diferirse hasta lab app estable).
