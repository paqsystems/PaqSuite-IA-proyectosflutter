# URLs de deploy — agente-gateway

| Rol | URL |
|-----|-----|
| Gateway producción (hub SignalR) | https://gateway.paqsuite.com/agent-hub |
| Gateway desarrollo (lab) | http://127.0.0.1:5100/agent-hub |
| Descarga instalador (MVP) | https://github.com/paqsystems/paqsuite-IA-AgenteCliente/releases/latest |

Notas:

- Laravel habla al Gateway por URL **interna** VPC (no Tailscale).
- El hub público solo recibe WSS saliente de agentes (443).
- Completar DNS/TLS reales en TR-003 / HU-002.
