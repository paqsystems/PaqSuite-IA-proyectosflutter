# Deploy Gateway AWS — runbook (TR-003)

| Campo | Valor |
|-------|--------|
| Origen | SPEC-AGW-001 §6, HU-002 CA 9–13, TR-003 (N1–N6), D2/D8 |
| Estado | Runbook **ejecutable** (TR-003 + cierre docs TR-009 / HU-008) |
| Hub público | `https://gateway.paqsystems.com/agent-hub` |
| Lab previo | [lab-local.md](lab-local.md) (verdear caño local antes de exigir AWS) |
| C1 | [c1-20260905-TR-003.md](../08-control/c1-20260905-TR-003.md) |
| Instalación exhaustiva | [deploy/instalacion-exhaustiva-paq-gateway-ia.md](deploy/instalacion-exhaustiva-paq-gateway-ia.md) |

**Prohibido:** Tailscale como camino de producción; abrir SQL 1433 a Internet; publicar `/internal/*` en Nginx público; `change-me-in-production` / `UseDevAuthStub=true` en el servidor.

Defaults cerrados (C1 + lección ops): **EC2** + **Nginx en la EC2** + Kestrel `0.0.0.0:5100` (SG limita origen) + publish en `/opt/paqgateway`. Hostname DNS real: **gateway.paqsystems.com** (SPEC histórico `paqsuite.com` no existe en Route 53 de la cuenta).

---

## 1. Decisiones de arquitectura (cerradas MVP)

| Decisión | Elección MVP |
|----------|----------------|
| Terminación TLS | Nginx en la misma EC2 (+ cert Let's Encrypt o el de PaqSystems) |
| DNS | `gateway.paqsystems.com` → EIP / IP pública de la EC2 |
| SO | Amazon Linux 2023 o Ubuntu LTS (el de la VPC Laravel) |
| Laravel → Gateway | `http://<IP-privada-EC2>:5100` (red VPC; header API key) |
| Kestrel bind | `0.0.0.0:5100` + SG (solo SG Laravel); Nginx proxya `127.0.0.1:5100` |

Fuera del MVP: Redis backplane, N instancias, multi-AZ, ALB (salvo que la cuenta ya lo use; documentar en Traza).

---

## 2. Red (obligatorio)

| Ítem | Requisito SPEC | Completar |
|------|----------------|-----------|
| VPC | **Misma VPC** que Laravel | VPC id: |
| Subnet | Pública si necesita EIP; Kestrel solo localhost | Subnet: |
| IP privada EC2 | Laravel habla por red **interna** | |
| IP pública / EIP | Solo para agentes (WSS 443) | |
| Conectividad Laravel → Gateway | TCP 5100 desde SG Laravel | `curl` interno |

Sin misma VPC: corregir antes del piloto. No Tailscale.

---

## 3. Security Group

| Dirección | Puerto | Origen | Destino | Motivo |
|-----------|--------|--------|---------|--------|
| Inbound | **443** | `0.0.0.0/0` (o Cloudflare) | Nginx | Hub WSS agentes |
| Inbound | **80** | `0.0.0.0/0` | Nginx | ACME Let's Encrypt (+ redirect) |
| Inbound | **22** / SSM | Solo IPs soporte | SSH/SSM | Admin |
| Inbound | **5100** | Solo CIDR VPC / SG Laravel | Kestrel | `/internal/*` |
| Inbound | **1433** | — | — | **Prohibido** a Internet |
| Outbound | 443/80 | Según | Laravel privado | Auth agentes |

SG Gateway: _______________ · SG Laravel: _______________

Internet ve **solo** 443 → `/agent-hub`. Jobs/status = red privada + API key.

---

## 4. DNS y TLS

| Ítem | Valor |
|------|--------|
| Nombre | `gateway.paqsystems.com` |
| Registro | A → EIP de la EC2 (zona Route 53 `paqsystems.com`) |
| Cert | Let's Encrypt / cert interno en Nginx (ver plantilla) |
| Hub | `/agent-hub` |
| URL agentes | `https://gateway.paqsystems.com/agent-hub` |

Plantilla: [deploy/nginx-gateway.conf](deploy/nginx-gateway.conf) (`Upgrade` / `Connection` / timeouts largos).

---

## 5. Software base en la EC2

| Ítem | Notas | Hecho |
|------|-------|-------|
| .NET 8 **ASP.NET Core Runtime** (Linux x64) | No SDK en prod | [ ] |
| Nginx | TLS + proxy a Kestrel | [ ] |
| Usuario `paqgateway` sin shell | `useradd --system --home /opt/paqgateway --shell /usr/sbin/nologin paqgateway` | [ ] |
| systemd `paqgateway.service` | [deploy/paqgateway.service](deploy/paqgateway.service) | [ ] |
| `/etc/paqgateway/env` | Desde [deploy/env.example](deploy/env.example); chmod 640 | [ ] |
| UTC / NTP | Recomendado | [ ] |

---

## 6. Variables de entorno (producción)

**No** commitear valores reales. Archivo: `/etc/paqgateway/env`.

| Variable | Uso |
|----------|-----|
| `ASPNETCORE_ENVIRONMENT=Production` | Obligatoria |
| `ASPNETCORE_URLS=http://0.0.0.0:5100` | Escucha VPC; SG limita origen (Forge). `127.0.0.1` solo = Nginx local, Forge no conecta |
| `Gateway__InternalApiKey` | Laravel → Gateway (`X-Paq-Internal-Api-Key`) |
| `LaravelApi__BaseUrl` | URL **privada** de Laravel |
| `LaravelApi__InternalApiKey` | Gateway → Laravel authenticate |
| `Gateway__UseDevAuthStub=false` | N5 — **prohibido** `true` en prod |

Laravel (TANGO): base URL del Gateway = **IP/DNS privado**:5100, **no** el hostname público para jobs.

**Copia local de keys (ops, fuera de git) — instalación Paq-Gateway-IA 2026-09-05:**  
`C:\Programacion\KEYS\paq-gateway-ia\keys-solicitados-instalacion.txt`  
No versionar ese archivo. En el servidor: solo `/etc/paqgateway/env` (chmod 640).

---

## 7. Contrato Laravel (no en esta EC2)

| Ítem | Repo | Notas |
|------|------|--------|
| Alta modo agente / token | TANGO — TR-001 | Hecho en lab |
| `POST /api/internal/gateway/authenticate` | TANGO | Necesario para auth real de agentes en prod |
| Cliente HTTP al Gateway | TANGO — TR-006+ | URL interna + API key |

Sin authenticate en TANGO, el hub TLS puede estar up pero el handshake de agente con token real fallará hasta cablear auth.

---

## 8. Pruebas de aceptación infra

| # | Prueba | Cómo | OK |
|---|--------|------|-----|
| 1 | Proceso up | `systemctl status paqgateway` | [x] |
| 2 | TLS público | `curl -I https://gateway.paqsystems.com` (cert válido) | [x] |
| 3 | WSS hub | Ver procedimiento en [TR-003 § Prueba manual CA 10](../04-tareas/001-Conectividad/TR-003-deploy-gateway-aws.md#prueba-manual-ca-10--wss-público-labagentmock): `LabAgentMock` → `https://gateway.paqsystems.com/agent-hub` **fuera** de Tailscale | [ ] |
| 4 | Internal no público | `curl -X POST https://gateway.paqsystems.com/internal/jobs/send` → 404 (Nginx) | [x] |
| 5 | Internal desde Laravel | Desde Forge: `curl -H "X-Paq-Internal-Api-Key: …" http://10.0.1.224:5100/internal/agents/{id}/status` | [x] |
| 6 | 1433 cerrado | Revisar SG | [ ] |
| 7 | Sin stub / change-me | `grep -i UseDevAuthStub /etc/paqgateway/env` → false/ausente | [ ] |

---

## 9. Ficha de la instancia (referencia *2026-09-05*)

Detalle paso a paso: [deploy/instalacion-exhaustiva-paq-gateway-ia.md](deploy/instalacion-exhaustiva-paq-gateway-ia.md).

```text
Cuenta AWS / región: 655232113361 / us-east-2 (Ohio)
VPC id: vpc-0588b88f9c6772017 (paq-2021) — misma que Forge
Subnet: subnet-0b2e94121d57cadd1
EC2 instance id: i-026ab0a7c3a957fd2
Nombre / tag: Paq-Gateway-IA
AMI / SO: Amazon Linux 2023
Tipo de instancia: t3.micro
IP privada: 10.0.1.224
IP pública / EIP: 3.142.236.237 (auto-asignada; EIP fija pendiente opcional)
Security Group id(s): sg-038e5fa123db1b5c8 (paq-gateway-ia)
  - 443 ← 0.0.0.0/0
  - 80  ← 0.0.0.0/0 (ACME)
  - 5100 ← sg-012112202a70d9d29 (Forge)
  - 22 ← IP oficina /32
Forge (Laravel) referencia: i-0ab40b2f17c7894c9 / 10.0.1.147 / sg-012112202a70d9d29
DNS: gateway.paqsystems.com → 3.142.236.237 (zona paqsystems.com)
TLS: Let's Encrypt /etc/letsencrypt/live/gateway.paqsystems.com/
Puerto Kestrel: 5100  ASPNETCORE_URLS=http://0.0.0.0:5100
URL agentes: https://gateway.paqsystems.com/agent-hub
URL interna Laravel → Gateway: http://10.0.1.224:5100
LaravelApi__BaseUrl: http://10.0.1.147
Fecha de alta: 2026-09-05
PEM: C:\Users\PabloQ\.ssh\pq-ia-gateway.pem
Copia local de keys (ops, fuera de git): C:\Programacion\KEYS\paq-gateway-ia\keys-solicitados-instalacion.txt
```

---

## 10. Instalación del binario (pasos TR-003)

### 10.1 Publish (máquina de build / CI)

Desde la raíz del repo:

```bash
dotnet publish src/PaqGateway -c Release -o artifacts/paqgateway
```

Smoke local (opcional): el directorio debe contener `PaqGateway.dll`. La carpeta `artifacts/` está en `.gitignore`.

### 10.2 Usuario y directorios (EC2)

```bash
sudo useradd --system --home /opt/paqgateway --shell /usr/sbin/nologin paqgateway || true
sudo mkdir -p /opt/paqgateway /etc/paqgateway
sudo chown -R paqgateway:paqgateway /opt/paqgateway
```

### 10.3 Copiar artefactos

Desde la máquina de build (ajustar user/host):

```bash
rsync -avz --delete artifacts/paqgateway/ user@<ec2>:/tmp/paqgateway-new/
# en la EC2:
sudo rsync -a --delete /tmp/paqgateway-new/ /opt/paqgateway/
sudo chown -R paqgateway:paqgateway /opt/paqgateway
```

Alternativa: SSM Session Manager + upload, o pipeline CI.

### 10.4 Env y systemd

```bash
sudo cp /path/to/repo/docs/06-operacion/deploy/env.example /etc/paqgateway/env
sudo chmod 640 /etc/paqgateway/env
sudo chown root:paqgateway /etc/paqgateway/env
# editar secretos reales
sudo nano /etc/paqgateway/env

sudo cp /path/to/repo/docs/06-operacion/deploy/paqgateway.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now paqgateway
sudo systemctl status paqgateway
```

### 10.5 Nginx + cert

```bash
sudo cp /path/to/repo/docs/06-operacion/deploy/nginx-gateway.conf /etc/nginx/sites-available/gateway.paqsystems.com
# o /etc/nginx/conf.d/gateway.conf en Amazon Linux
# Descomentar ssl_certificate* tras emitir cert (certbot u otro)
sudo nginx -t && sudo systemctl reload nginx
```

DNS: apuntar `gateway.paqsystems.com` a la EIP/IP. Emitir certificado (certbot).  
Procedimiento completo documentado: [deploy/instalacion-exhaustiva-paq-gateway-ia.md](deploy/instalacion-exhaustiva-paq-gateway-ia.md).

### 10.6 Verificar

Ejecutar checklist §8. Completar ficha §9. Cerrar Traza en [TR-003](../04-tareas/001-Conectividad/TR-003-deploy-gateway-aws.md).

---

## 11. Prueba funcional post-deploy (operador)

Tras hub up + Laravel cableado:

1. Instalar un agente de lab/piloto con [instalacion-agente.md](instalacion-agente.md) apuntando a `https://gateway.paqsystems.com/agent-hub`.
2. Confirmar agente **online** (status interno desde Forge / PaqSuite).
3. Disparar **`diagnostics.run`** (piloto HU-005) y una operación **`auth.login`** (HU-006) según runbooks de lab.

Publish/systemd ya documentados en §10; no duplicar secretos aquí.

---

## 12. Qué no configurar (Gateway / AWS)

- Tailscale como camino de producción o de agentes.
- Abrir **1433** (SQL) a Internet o al SG del Gateway.
- Publicar `/internal/*` en Nginx público (solo VPC + API key).
- `Gateway__UseDevAuthStub=true` o `change-me-in-production` en el servidor.
- Apuntar Laravel a la URL **pública** para jobs/status (usar IP privada `:5100`).
- Fallback SQL modo agente si el Gateway/agente falla.

---

## 13. Referencias

- Plantillas: [deploy/](deploy/)
- URLs: [urls-deploy.md](urls-deploy.md)
- Instalador cliente: [instalacion-agente.md](instalacion-agente.md)
- Empaquetado zip: [empaquetado-instalador.md](empaquetado-instalador.md)
- Lab: [lab-local.md](lab-local.md)
- App Gateway: TR-002 (`src/PaqGateway`)
