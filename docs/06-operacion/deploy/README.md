# README — artefactos de deploy PaqGateway (TR-003)

Plantillas para la EC2. Orquestación completa: [../deploy-gateway-aws.md](../deploy-gateway-aws.md).  
**Instalación exhaustiva (instancia de referencia *Paq-Gateway-IA*):** [instalacion-exhaustiva-paq-gateway-ia.md](instalacion-exhaustiva-paq-gateway-ia.md).

| Archivo | Destino en servidor |
|---------|---------------------|
| `paqgateway.service` | `/etc/systemd/system/paqgateway.service` |
| `nginx-gateway.conf` | `/etc/nginx/conf.d/gateway.conf` (AL2023) |
| `env.example` | Copiar a `/etc/paqgateway/env` (secretos fuera de git) |
| `instalacion-exhaustiva-paq-gateway-ia.md` | Solo docs (procedimiento + constantes) |

Publish local (máquina de build):

```bash
dotnet publish src/PaqGateway -c Release -o artifacts/paqgateway
```

En la EC2 el contenido de `artifacts/paqgateway` va a `/opt/paqgateway`.
