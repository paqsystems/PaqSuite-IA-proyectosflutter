# URLs de deploy — agente-gateway

| Rol | URL |
|-----|-----|
| Gateway producción (hub SignalR) | https://gateway.paqsystems.com/agent-hub |
| Gateway lab | http://127.0.0.1:5100/agent-hub |
| Gateway interno (Laravel → jobs/status) | `http://10.0.1.224:5100` (instancia *Paq-Gateway-IA*; ver ficha) |
| Descarga instalador (MVP, **público**) | https://github.com/paqsystems/paqsuite-IA-AgenteCliente/releases/latest |

Notas:

- Hostname ops real (*2026-09-05*): **gateway.paqsystems.com** (zona Route 53 `paqsystems.com`). El SPEC histórico decía `gateway.paqsuite.com` (dominio no presente en la cuenta).
- El instalador del agente se descarga desde esa URL (o la que la reemplace) en **cada servidor nuevo**; no hace falta clonar el repo. Verificar SHA256 de la release (D9).
- Instructivo cliente: [instalacion-agente.md](instalacion-agente.md) (texto final en HU-008).
- Laravel habla al Gateway por URL **interna** VPC (no Tailscale ni el hostname público para `/internal/*`).
- El hub público solo recibe WSS saliente de agentes (443); Nginx no publica `/internal`.
- Runbook AWS: [deploy-gateway-aws.md](deploy-gateway-aws.md). **Instalación exhaustiva (referencia):** [deploy/instalacion-exhaustiva-paq-gateway-ia.md](deploy/instalacion-exhaustiva-paq-gateway-ia.md). Plantillas: [deploy/](deploy/).
- Lab por tramos: [lab-local.md](lab-local.md).
- **No** hay URL Tailscale ni fallback modo agente en este archivo.
