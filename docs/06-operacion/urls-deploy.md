# URLs de deploy — agente-gateway

| Rol | URL |
|-----|-----|
| Gateway producción (hub SignalR) | https://gateway.paqsystems.com/agent-hub |
| Gateway lab | http://127.0.0.1:5100/agent-hub |
| Gateway interno (Laravel → jobs/status) | `http://10.0.1.224:5100` (instancia *Paq-Gateway-IA*; ver ficha) |
| Descarga instalador (MVP, **público**, D9) | `https://<host-tango>/descargas/agente` (**Q-D9-1** — host exacto pendiente) |
| Asset instalador | `PaqAgentSetup.exe` (+ `PaqAgentSetup.exe.sha256` en la misma landing) |

Notas:

- Hostname ops real (*2026-09-05*): **gateway.paqsystems.com** (zona Route 53 `paqsystems.com`). El SPEC histórico decía `gateway.paqsuite.com` (dominio no presente en la cuenta).
- El instalador del agente se descarga desde la **landing pública TANGO** en cada servidor nuevo; no hace falta clonar el código ni descomprimir zip. Verificar SHA256 (D9). Empaquetado: [empaquetado-instalador.md](empaquetado-instalador.md). Justificación: [MANUAL-DEL-PROGRAMADOR](../00-contexto/MANUAL-DEL-PROGRAMADOR.md) § D9.
- **Q-D9-1:** cuando el operador fije host y path, reemplazar el placeholder `<host-tango>` **solo** aquí, en D9 y en [instalacion-agente.md](instalacion-agente.md). No reabrir el formato del artefacto.
- GitHub Releases no es el canal de cara al cliente (puede usarse como espejo interno de CI).
- Instructivo cliente: [instalacion-agente.md](instalacion-agente.md).
- Laravel habla al Gateway por URL **interna** VPC (no Tailscale ni el hostname público para `/internal/*`).
- El hub público solo recibe WSS saliente de agentes (443); Nginx no publica `/internal`.
- Runbook AWS: [deploy-gateway-aws.md](deploy-gateway-aws.md). **Instalación exhaustiva (referencia):** [deploy/instalacion-exhaustiva-paq-gateway-ia.md](deploy/instalacion-exhaustiva-paq-gateway-ia.md). Plantillas: [deploy/](deploy/).
- Lab por tramos: [lab-local.md](lab-local.md).
- **No** hay URL Tailscale ni fallback modo agente en este archivo.
