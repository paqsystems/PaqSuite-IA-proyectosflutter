# URLs de deploy — agente-gateway

| Rol | URL |
|-----|-----|
| Gateway producción (hub SignalR) | https://gateway.paqsuite.com/agent-hub |
| Gateway desarrollo (lab) | http://127.0.0.1:5100/agent-hub |
| Descarga instalador (MVP, **público**) | https://github.com/paqsystems/paqsuite-IA-AgenteCliente/releases/latest |

Notas:

- El instalador del agente se descarga desde esa URL (o la que la reemplace) en **cada servidor nuevo**; no hace falta clonar el repo. Verificar SHA256 de la release (D9).
- Instructivo cliente: [instalacion-agente.md](instalacion-agente.md) (texto final en HU-008).
- Laravel habla al Gateway por URL **interna** VPC (no Tailscale).
- El hub público solo recibe WSS saliente de agentes (443).
- Completar DNS/TLS reales en TR-003 / HU-002. Checklist AWS: [deploy-gateway-aws.md](deploy-gateway-aws.md).
- Lab por tramos (sin instalación completa): [lab-local.md](lab-local.md).
- **No** hay URL Tailscale ni fallback modo agente en este archivo.
