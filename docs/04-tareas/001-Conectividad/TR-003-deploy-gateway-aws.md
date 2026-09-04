# TR-003 — Deploy Gateway en AWS

| Campo | Valor |
|-------|--------|
| TR | TR-003 |
| Estado | Pendiente |
| HU | [HU-002](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md) |
| Repo | este (`docs/06-operacion/` + artefactos de publish) + cuenta AWS |
| Orden D10 | 2 (junto con TR-002) |

El runbook [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) tiene el **checklist de definición AWS** (VPC, SG, DNS, secretos). Al ejecutar esta TR se completa el paso a paso de publish/systemd.

### Tareas

- [ ] Publicar `dotnet publish` Release.
- [ ] EC2 (o equivalente) misma VPC que Laravel. Security Group según SPEC.
- [ ] systemd + Nginx/ALB + certificado + DNS `gateway.paqsuite.com`.
- [ ] Env: `Gateway__InternalApiKey`, `LaravelApi__BaseUrl` (URL **privada** de Laravel), `LaravelApi__InternalApiKey`.
- [ ] Verificar WSS desde una máquina **fuera** de Tailscale (salida 443 a Internet).
- [ ] Documentar el procedimiento (cierra con TR-009; borrador aquí).
- [ ] Hub público: `https://gateway.paqsuite.com/agent-hub` ([urls-deploy.md](../../06-operacion/urls-deploy.md)).

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Laravel habla al Gateway por URL interna VPC, no Tailscale. |
| Pendientes | |
