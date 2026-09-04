# Deploy Gateway AWS — qué definir y configurar

| Campo | Valor |
|-------|--------|
| Origen | SPEC-AGW-001 §6, HU-002, TR-003, D2/D8 |
| Estado | Checklist de **definición / configuración** (pre-piloto). El paso a paso de `dotnet publish` + systemd se completa al ejecutar TR-003 |
| Hub público | `https://gateway.paqsuite.com/agent-hub` |
| Lab previo | [lab-local.md](lab-local.md) (verdear caño en localhost antes de exigir AWS) |

**Prohibido:** Tailscale como camino de producción; abrir SQL 1433 a Internet; dejar `/internal/*` público sin API key; secretos `change-me-in-production` en el servidor.

Ya tenés una **instancia EC2** dedicada. Completá las tablas de abajo con los valores reales de esa cuenta (no hace falta esperar a codear TR-002).

---

## 1. Decisiones de arquitectura (cerrar o anotar default)

El SPEC permite **Nginx en la EC2** o **ALB** delante. Para el MVP alcanza **una** de las dos.

| Decisión | Opciones | Default sugerido MVP | Tu elección |
|----------|----------|----------------------|-------------|
| Terminación TLS | Nginx en la misma EC2 / ALB ACM | Nginx + cert en la EC2 (menos piezas) | |
| DNS | Route 53 u otro DNS de `paqsuite.com` | Registro A o CNAME → IP/ALB pública | |
| SO de la EC2 | Amazon Linux 2023 / Ubuntu LTS | El que ya usan en Laravel Forge/VPC | |
| Cómo llega Laravel al Gateway | IP privada EC2 / DNS interno VPC | `http://<ip-privada>:5xxx` o DNS interno | |

Lo que **no** se decide aquí: Redis backplane, N instancias de Gateway, multi-AZ (fuera del MVP).

---

## 2. Red (obligatorio)

| Ítem | Requisito SPEC | Completar |
|------|----------------|-----------|
| VPC | **Misma VPC** que Laravel (Forge / host PaqSuite) | VPC id: |
| Subnet | Preferible privada para Kestrel; pública solo si Nginx/ALB necesita IP elástica | Subnet: |
| IP privada de la EC2 Gateway | Laravel habla por red **interna**, no por Tailscale | |
| IP pública / Elastic IP | Solo para agentes (WSS 443). Si usás ALB, la EIP es del ALB | |
| Conectividad Laravel → Gateway | Desde la instancia Laravel, TCP al puerto interno del Gateway | Probar con `curl` interno |

Si la EC2 nueva **no** está en la misma VPC que Laravel, hay que corregir eso antes del piloto (peering o recrear en la VPC correcta). No improvisar Tailscale.

---

## 3. Security Group de la instancia Gateway

| Dirección | Puerto | Origen | Destino | Motivo |
|-----------|--------|--------|---------|--------|
| Inbound | **443** | `0.0.0.0/0` (o Cloudflare si aplica) | Nginx/ALB | Agentes: HTTPS/WSS saliente desde clientes |
| Inbound | **22** (o SSM) | Solo IPs de soporte PaqSystems | SSH/SSM | Admin humano — **no** 22 abierto al mundo si se puede evitar |
| Inbound | Puerto Kestrel (ej. 5100) | **Solo** CIDR de la VPC / SG de Laravel | Kestrel | API `/internal/*` — **no** a Internet |
| Inbound | **1433** | — | — | **Prohibido** abrir a Internet |
| Outbound | 443/80 | Según | Laravel privado / HTTPS | Auth de agentes contra Laravel; updates OS |

SG id (Gateway): _______________  
SG Laravel (para referenciar como origen interno): _______________

Regla de oro: lo que ve Internet es **solo** el hub SignalR en 443. Jobs y status van por red privada + API key.

---

## 4. DNS y certificado TLS

| Ítem | Valor esperado | Completar |
|------|----------------|-----------|
| Nombre público | `gateway.paqsuite.com` | ¿Dominio ya en Route 53 / registrador? |
| Registro | A → EIP de la EC2, o CNAME/A → ALB | |
| Certificado | ACM (ALB) o Let's Encrypt / cert en Nginx | Quién emite: |
| Path hub | `/agent-hub` | Fijo (SPEC) |
| URL agentes | `https://gateway.paqsuite.com/agent-hub` | |

Sin DNS + TLS válidos no se cierra HU-002 (handshake WSS real desde fuera de la VPC).

WebSocket: el reverse proxy debe permitir **Upgrade** (Nginx: `proxy_http_version 1.1`, headers `Upgrade`/`Connection`; ALB: target group con stickiness si aplica). Detalle de config en TR-003.

---

## 5. Softare base en la EC2 (antes o al instalar el publish)

| Ítem | Notas | Hecho |
|------|-------|-------|
| .NET 8 **ASP.NET Core Runtime** (Linux x64) | No hace falta SDK en producción | [ ] |
| Nginx (si no hay ALB) o solo Kestrel detrás de ALB | Termina TLS / proxy a Kestrel | [ ] |
| systemd unit `paqgateway.service` | `Restart=always`, WorkingDirectory del publish | [ ] |
| Usuario de servicio sin shell | No correr Kestrel como root | [ ] |
| Disco / logs | Carpeta de logs Serilog; rotación básica | [ ] |
| Zona horaria / NTP | UTC recomendado para `last_seen_at` | [ ] |

---

## 6. Variables de entorno / secretos (producción)

Nombres canónicos (TR-003). Generar valores fuertes; **no** commitear en git.

| Variable | Quién la usa | Dónde vive | Completar / generar |
|----------|--------------|------------|---------------------|
| `Gateway__InternalApiKey` | Laravel → Gateway (`/internal/*`) | Env systemd / Parameter Store | |
| `LaravelApi__BaseUrl` | Gateway → Laravel (auth token agente) | URL **privada** de Laravel en la VPC | ej. `http://10.x.x.x` o DNS interno |
| `LaravelApi__InternalApiKey` | Gateway → Laravel | Mismo secreto que Laravel espera en API interna | |
| (opcional) `ASPNETCORE_URLS` | Kestrel escucha solo localhost o red privada | ej. `http://127.0.0.1:5100` | |
| (opcional) `Gateway__OnlineTtlSeconds` | Default scaffold 90 | Solo si no usás constante de PaqContracts | |

En Laravel (TANGO, al cablear): URL del Gateway = **IP/DNS privado** de esta EC2 (o ALB interno), **no** `https://gateway.paqsuite.com` para jobs internos.

Rotación: documentar quién guarda las keys (1Password / Secrets Manager). Nunca en `appsettings.json` versionado.

---

## 7. Qué debe existir en Laravel (contrato, no en esta EC2)

Sin esto el Gateway no puede autenticar agentes en producción:

| Ítem | Repo | Notas |
|------|------|--------|
| Alta modo agente / token | TANGO — TR-001 | `agent_id`, token hash; sin `host` obligatorio |
| Endpoint interno de auth (o catálogo) | TANGO | Gateway llama `LaravelApi__BaseUrl` |
| Cliente HTTP al Gateway | TANGO — TR-006+ | Base URL **interna** + `Gateway__InternalApiKey` |

Orden D10: podés **preparar** la EC2 ya; el cableado Laravel puede ir en paralelo (TR-001) y el handshake agente↔gateway público cuando TR-002/005 estén verdes en lab.

---

## 8. Pruebas de aceptación infra (checklist)

Antes de dar por cerrado el deploy (HU-002):

| # | Prueba | Cómo | OK |
|---|--------|------|-----|
| 1 | Proceso up | `systemctl status paqgateway` | [ ] |
| 2 | TLS público | Navegador/`curl` a `https://gateway.paqsuite.com` (cert válido) | [ ] |
| 3 | WSS hub | Agente o cliente SignalR a `/agent-hub` **desde fuera** de Tailscale | [ ] |
| 4 | Internal no público | `POST https://gateway…/internal/jobs/send` sin key → 401/403 | [ ] |
| 5 | Internal desde Laravel | Desde host Laravel: `GET http://<privado>/internal/agents/{id}/status` con API key | [ ] |
| 6 | 1433 cerrado | SG: sin regla 1433 a Internet | [ ] |
| 7 | Sin secretos en disco claros | Revisar unit + env; no `change-me` | [ ] |

Detalle de lab previo en máquina de desarrollo: [lab-local.md](lab-local.md). URLs: [urls-deploy.md](urls-deploy.md).

---

## 9. Ficha de la instancia (completar vos)

Copiá y rellená; sirve para TR-003 y soporte.

```text
Cuenta AWS / región:
VPC id:
Subnet:
EC2 instance id:
Nombre / tag:
AMI / SO:
Tipo de instancia:
IP privada:
IP pública / EIP:
Security Group id(s):
DNS gateway.paqsuite.com → :
TLS: Nginx | ALB (+ ARN certificado):
Puerto Kestrel interno:
URL interna Laravel (LaravelApi__BaseUrl):
URL interna que usará Laravel hacia este Gateway:
Fecha de alta:
Responsable:
```

---

## 10. Qué falta para “instalar” el binario (TR-003)

Cuando el código de `PaqGateway` esté listo:

1. `dotnet publish` Release en CI o en la máquina de build  
2. Copiar artefactos a la EC2 (scp/SSM)  
3. Unit systemd + env con las variables de §6  
4. Nginx/ALB según §1 y §4  
5. Pruebas §8  
6. Actualizar este archivo con comandos reales (paths, unit file) y cerrar Traza en TR-003  

Hasta entonces, este documento es la **lista de lo que tenés que tener definido en AWS** para no inventar infra el día del deploy.
