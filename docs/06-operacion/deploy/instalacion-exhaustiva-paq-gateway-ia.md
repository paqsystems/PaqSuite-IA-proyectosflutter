# Instalación exhaustiva — PaqGateway en AWS (instancia de referencia)

| Campo | Valor |
|-------|--------|
| Origen | TR-003 / HU-002 / SPEC-AGW-001 §6 |
| Fecha de alta (referencia) | *2026-09-05* |
| Instancia de referencia | *Paq-Gateway-IA* (`*i-026ab0a7c3a957fd2*`) |
| Runbook corto | [../deploy-gateway-aws.md](../deploy-gateway-aws.md) |
| Plantillas | [paqgateway.service](paqgateway.service), [nginx-gateway.conf](nginx-gateway.conf), [env.example](env.example) |

Este documento describe **todo el proceso real** de la primera instalación productiva del Gateway, desde la generación de la EC2 hasta las verificaciones Laravel↔Gateway y TLS público.

**Convención:** todo valor *entre asteriscos* es una **constante de esta instalación** (o un valor canónico acordado). Al crear una **nueva** instancia, copiá el procedimiento y **reemplazá** esos valores; no reutilices IPs/IDs de otra EC2.

**Prohibido (SPEC / modo agente):** Tailscale como camino de producción; abrir SQL *1433* a Internet; publicar `/internal/*` en Nginx público; `UseDevAuthStub=true` o keys `change-me-in-production` en el servidor.

---

## 0. Glosario de constantes (instalación *2026-09-05*)

### 0.1 AWS — cuenta y red

| Concepto | Constante (referencia) | Notas al recrear |
|----------|------------------------|------------------|
| Cuenta AWS | *655232113361* (PaqSystems) | Misma cuenta salvo decisión contraria |
| Región | *us-east-2* (Ohio) | Misma que Laravel/Forge |
| VPC | *vpc-0588b88f9c6772017* (*paq-2021*) | **Obligatorio:** misma VPC que Forge |
| Subnet | *subnet-0b2e94121d57cadd1* | Misma subnet pública que Forge en esta instalación |
| Forge (Laravel) instance id | *i-0ab40b2f17c7894c9* | Solo referencia; no reutilizar como Gateway |
| Forge IP privada | *10.0.1.147* | `LaravelApi__BaseUrl` apunta acá |
| Forge IP pública / EIP | *13.59.42.169* | Admin Forge; no es el hub de agentes |
| Forge Security Group | *sg-012112202a70d9d29* (*default* en esa VPC) | Origen permitido hacia puerto *5100* del Gateway |
| Forge usuario SSH (consola) | *ubuntu* | AMI Ubuntu Forge |

### 0.2 EC2 Gateway (esta instalación)

| Concepto | Constante (referencia) | Notas al recrear |
|----------|------------------------|------------------|
| Nombre / tag Name | *Paq-Gateway-IA* | Nuevo nombre si es otra env |
| Instance id | *i-026ab0a7c3a957fd2* | Nuevo al crear EC2 |
| AMI / SO | *Amazon Linux 2023* | Preferir la misma familia |
| Tipo | *t3.micro* | Ajustar si hay carga |
| Usuario SSH | *ec2-user* | AL2023 |
| IP privada | *10.0.1.224* | Nueva al crear; Laravel usará esta |
| IP pública (auto) | *3.142.236.237* | **Volátil** si no hay EIP; ver §1.5 |
| Security Group | *sg-038e5fa123db1b5c8* (*paq-gateway-ia*) | Nuevo SG o reutilizar reglas |
| Key pair (local Windows) | *`C:\Users\PabloQ\.ssh\pq-ia-gateway.pem`* | Permisos ACL restringidos; no en git |
| Hostname DNS público | *gateway.paqsystems.com* | Zona Route 53 *paqsystems.com* |
| Hub agentes (WSS) | *https://gateway.paqsystems.com/agent-hub* | URL de fábrica del instalador (ops) |
| URL interna Laravel→Gateway | *http://10.0.1.224:5100* | Solo VPC; path `/internal/*` |
| Puerto Kestrel | *5100* | No publicar a Internet |
| Puerto Nginx HTTPS | *443* | Público agentes |
| Puerto Nginx HTTP | *80* | Redirect + ACME Let's Encrypt |
| Puerto SSH | *22* | Solo IP oficina / soporte |

### 0.3 Paths y servicio en el SO

| Concepto | Constante |
|----------|-----------|
| Binarios | */opt/paqgateway* (`PaqGateway.dll`) |
| Staging upload | */tmp/paqgateway-new* |
| Env producción | */etc/paqgateway/env* (`chmod 640`, `root:paqgateway`) |
| Unit systemd | */etc/systemd/system/paqgateway.service* |
| Vhost Nginx (AL2023) | */etc/nginx/conf.d/gateway.conf* |
| Cert Let's Encrypt | */etc/letsencrypt/live/gateway.paqsystems.com/fullchain.pem* |
| Key cert | */etc/letsencrypt/live/gateway.paqsystems.com/privkey.pem* |
| Usuario de proceso | *paqgateway* (system, nologin) |
| ExecStart | */usr/bin/dotnet /opt/paqgateway/PaqGateway.dll* |
| Copia local keys (fuera de git) | *`C:\Programacion\KEYS\paq-gateway-ia\keys-solicitados-instalacion.txt`* |

### 0.4 Variables de entorno (nombres canónicos)

Valores secretos: **solo** en el archivo de keys local y en `*/etc/paqgateway/env*` — no en este repo.

| Variable | Rol |
|----------|-----|
| `ASPNETCORE_ENVIRONMENT` | *Production* |
| `ASPNETCORE_URLS` | *http://0.0.0.0:5100* (ver §7 — lección N4) |
| `Gateway__InternalApiKey` | Header `X-Paq-Internal-Api-Key` (Laravel → Gateway) |
| `LaravelApi__BaseUrl` | *http://10.0.1.147* (Forge privado; sin Tailscale) |
| `LaravelApi__InternalApiKey` | Gateway → Laravel `authenticate` |
| `Gateway__UseDevAuthStub` | *false* |

### 0.5 DNS — decisión respecto al SPEC

El SPEC/docs históricos mencionan `gateway.paqsuite.com`. En Route 53 de la cuenta **no** existe zona `paqsuite.com` (solo *paqsystems.com* y *paqsystems.ar*).  
**Decisión ops *2026-09-05*:** hostname real = *gateway.paqsystems.com* (registro A en zona *paqsystems.com*).

---

## 1. Crear la instancia EC2

### 1.1 Principios

1. **No** reutilizar la EC2 de un programador ni la de Forge como Gateway.
2. Misma *VPC* (*vpc-0588b88f9c6772017*) y preferible misma *subnet* que Forge.
3. Key pair dedicada; PEM solo en máquina de ops con ACL restringida.

### 1.2 Parámetros de lanzamiento (como se hizo)

1. Consola EC2 → *Launch instance* en región *us-east-2*.
2. Name: *Paq-Gateway-IA*.
3. AMI: *Amazon Linux 2023*.
4. Instance type: *t3.micro*.
5. Key pair: la asociada a *`pq-ia-gateway.pem`* (guardar en *`%USERPROFILE%\.ssh\`*).
6. Network:
   - VPC: *vpc-0588b88f9c6772017*
   - Subnet: *subnet-0b2e94121d57cadd1*
   - Auto-assign public IP: habilitado (o asociar EIP después, §1.5)
7. Security group nuevo: *paq-gateway-ia* (detalle §2).
8. Storage: default (gp3 suficiente para MVP).
9. Launch → anotar *instance id*, IP privada y pública.

### 1.3 Referencia resultante

```text
Instance: i-026ab0a7c3a957fd2 (Paq-Gateway-IA)
Private:  10.0.1.224
Public:   3.142.236.237
SG:       sg-038e5fa123db1b5c8
```

### 1.4 SSH desde Windows (PowerShell)

```powershell
ssh -i "$env:USERPROFILE\.ssh\pq-ia-gateway.pem" ec2-user@3.142.236.237
```

Prompt esperado: `[ec2-user@ip-10-0-1-224 ~]$`.

Si la IP pública cambió (sin EIP), sustituir el host. Verificar SG puerto *22* con tu IP `/32` actual.

### 1.5 Elastic IP (recomendado antes de DNS definitivo)

La IP *3.142.236.237* es **auto-asignada**: un stop/start puede cambiarla.

**Si llegase a ocurrir** que tras reiniciar la EC2 el DNS o el cert dejan de resolver:

1. Allocate Elastic IP en *us-east-2*.
2. Associate a *i-026ab0a7c3a957fd2* (o la nueva instancia).
3. Actualizar registro A de *gateway.paqsystems.com* a la EIP.
4. No hace falta reemitir el cert si el hostname no cambió.

---

## 2. Security Group *paq-gateway-ia*

Reglas inbound de la instalación de referencia (*sg-038e5fa123db1b5c8*):

| Tipo | Puerto | Origen | Motivo |
|------|--------|--------|--------|
| HTTPS | *443* | *0.0.0.0/0* | Agentes WSS / Nginx |
| HTTP | *80* | *0.0.0.0/0* | Let's Encrypt HTTP-01 (+ redirect) |
| Custom TCP | *5100* | *sg-012112202a70d9d29* (Forge) | `/internal/*` solo desde Laravel |
| SSH | *22* | IP oficina `/32` (ej. al crear) | Admin |

Outbound: default allow (Gateway debe alcanzar Laravel privado *10.0.1.147* para authenticate).

**Prohibido:** inbound *1433* desde Internet.

**Si llegase a ocurrir** que Forge no alcanza el *5100*:

- Verificar que el origen del SG sea el **SG de Forge**, no solo un CIDR equivocado.
- Verificar que Kestrel escuche en *0.0.0.0:5100* (no solo *127.0.0.1*) — §7.
- Desde Forge: `curl` a *http://10.0.1.224:5100/...* (no desde la propia Gateway usando la IP privada si aún estaba en loopback).

**Si llegase a ocurrir** que certbot falla con timeout en el challenge HTTP:

- Confirmar regla inbound *80* en el SG.
- Confirmar que Nginx escucha en *80* y que el DNS A apunta a la IP pública correcta.

**Si llegase a ocurrir** que tu IP de oficina cambió y no entra SSH:

- Actualizar la regla *22* al nuevo `/32` (o usar SSM Session Manager).

---

## 3. Software base en la EC2

Con sesión SSH como *ec2-user*:

### 3.1 Paquetes

```bash
sudo dnf update -y
sudo dnf install -y nginx
# Runtime ASP.NET Core 8 (paquete Microsoft / doc .NET en AL2023)
# En la instalación se instaló aspnetcore/dotnet 8 runtime vía el procedimiento ops vigente.
dotnet --list-runtimes   # debe listar Microsoft.AspNetCore.App 8.x
```

### 3.2 Usuario y directorios

```bash
sudo useradd --system --home /opt/paqgateway --shell /usr/sbin/nologin paqgateway || true
sudo mkdir -p /opt/paqgateway /etc/paqgateway
sudo chown -R paqgateway:paqgateway /opt/paqgateway
```

Constantes: usuario *paqgateway*, home/binarios */opt/paqgateway*, config */etc/paqgateway*.

---

## 4. Publish y copia del binario

### 4.1 En la máquina de build (Windows / CI), raíz del repo

```powershell
dotnet publish src/PaqGateway -c Release -o artifacts/paqgateway
```

Debe existir `artifacts/paqgateway/PaqGateway.dll`. La carpeta `artifacts/` está en `.gitignore`.

### 4.2 Upload a la EC2

```powershell
scp -i "$env:USERPROFILE\.ssh\pq-ia-gateway.pem" -r artifacts/paqgateway/* ec2-user@3.142.236.237:/tmp/paqgateway-new/
```

En la EC2:

```bash
sudo mkdir -p /tmp/paqgateway-new
sudo rsync -a --delete /tmp/paqgateway-new/ /opt/paqgateway/
sudo chown -R paqgateway:paqgateway /opt/paqgateway
ls -la /opt/paqgateway/PaqGateway.dll
```

Referencia: en *2026-09-05* quedaron binarios en */opt/paqgateway* y staging en */tmp/paqgateway-new*.

---

## 5. Archivo de entorno `/etc/paqgateway/env`

### 5.1 Crear sin editores interactivos (recomendado)

Preferir `sudo tee` / heredoc frente a `nano`/`vi` para evitar corrupción del archivo.

```bash
sudo tee /etc/paqgateway/env > /dev/null <<'EOF'
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5100
Gateway__InternalApiKey=<PEGAR_DESDE_KEYS_LOCAL>
LaravelApi__BaseUrl=http://10.0.1.147
LaravelApi__InternalApiKey=<PEGAR_DESDE_KEYS_LOCAL>
Gateway__UseDevAuthStub=false
EOF

sudo chown root:paqgateway /etc/paqgateway/env
sudo chmod 640 /etc/paqgateway/env
```

Keys reales: *`C:\Programacion\KEYS\paq-gateway-ia\keys-solicitados-instalacion.txt`*.

### 5.2 Lectura

```bash
sudo cat -A /etc/paqgateway/env
```

**Si llegase a ocurrir** `Permission denied` al leer el env:

- Es esperado sin `sudo` (`640` `root:paqgateway`).
- En cadenas `cmd1 && cmd2`, un `grep` sin `sudo` **corta** el resto (p. ej. no se ejecuta el `systemctl restart`). Usar siempre `sudo grep` / `sudo cat`.

### 5.3 Contenido esperado (forma)

Solo líneas `CLAVE=valor`. Sin comandos shell pegados dentro del archivo. Terminaciones Unix (`$` al final de cada línea con `cat -A`).

---

## 6. systemd `paqgateway.service`

### 6.1 Crear unit

```bash
sudo tee /etc/systemd/system/paqgateway.service > /dev/null <<'EOF'
[Unit]
Description=PaqGateway (SignalR agent hub)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=paqgateway
Group=paqgateway
WorkingDirectory=/opt/paqgateway
ExecStart=/usr/bin/dotnet /opt/paqgateway/PaqGateway.dll
Restart=always
RestartSec=5
KillSignal=SIGINT
TimeoutStopSec=30
EnvironmentFile=-/etc/paqgateway/env
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
SyslogIdentifier=paqgateway
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF
```

Nota: **no** fijar `ASPNETCORE_URLS` en el unit si ya está en el `EnvironmentFile` (evita confusión con *127.0.0.1* vs *0.0.0.0*). Plantilla del repo: [paqgateway.service](paqgateway.service).

### 6.2 Activar

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now paqgateway
sudo systemctl status paqgateway --no-pager
```

Esperado: `active (running)`, proceso `/usr/bin/dotnet /opt/paqgateway/PaqGateway.dll`.

### 6.3 Smoke local (en la Gateway)

Endpoint real (no existe `/internal/status`):

```bash
curl -sS -i -H "X-Paq-Internal-Api-Key: <Gateway__InternalApiKey>" \
  http://127.0.0.1:5100/internal/agents/lab-agent-01/status
```

Esperado: `HTTP/1.1 200` y JSON `{"agentId":"lab-agent-01","status":"offline",...}`.

**Si llegase a ocurrir** `404` en `/internal/status`:

- Usar `GET /internal/agents/{agentId}/status` o `POST /internal/jobs/send` (contrato TR-002).

---

## 7. Bind de Kestrel: lección crítica (N4 revisada)

### 7.1 Problema

La plantilla inicial usaba `ASPNETCORE_URLS=http://127.0.0.1:5100` (solo loopback).  
Nginx en la misma máquina funciona vía *127.0.0.1*, pero **Laravel en Forge** habla a *http://10.0.1.224:5100*.

**Si llegase a ocurrir** desde Forge:

```text
curl: (7) Failed to connect to 10.0.1.224 port 5100
```

aunque `systemctl` diga `active` y el curl a *127.0.0.1:5100* funcione en la Gateway:

→ Kestrel no está escuchando en la interfaz privada.

### 7.2 Corrección (producción VPC)

En `/etc/paqgateway/env`:

```text
ASPNETCORE_URLS=http://0.0.0.0:5100
```

```bash
sudo systemctl restart paqgateway
ss -lntp | grep 5100
```

Esperado: `LISTEN ... 0.0.0.0:5100`.

La exposición a Internet se controla con el **SG** (solo *sg-012112202a70d9d29* → *5100*), no con el bind a loopback.

### 7.3 Verificación desde Forge

Consola AWS / SSH a Forge (*ubuntu@paq-2021*, *10.0.1.147*):

```bash
curl -sS -i -H "X-Paq-Internal-Api-Key: <Gateway__InternalApiKey>" \
  http://10.0.1.224:5100/internal/agents/lab-agent-01/status
```

Esperado (*2026-09-05*): `200` + `"status":"offline"`.

---

## 8. Nginx (HTTP primero, luego TLS)

### 8.1 Por qué no copiar la plantilla HTTPS “a ciegas”

La plantilla [nginx-gateway.conf](nginx-gateway.conf) incluye `listen 443 ssl` y líneas de certificado comentadas.

**Si llegase a ocurrir** que `nginx -t` falla o el servicio no arranca tras pegar la plantilla completa sin cert:

→ Primero un vhost **solo HTTP** en */etc/nginx/conf.d/gateway.conf*; después `certbot --nginx` reescribe TLS.

### 8.2 Vhost HTTP inicial (Amazon Linux: `conf.d`)

```bash
sudo tee /etc/nginx/conf.d/gateway.conf > /dev/null <<'EOF'
upstream paqgateway_kestrel {
    server 127.0.0.1:5100;
    keepalive 32;
}

server {
    listen 80;
    listen [::]:80;
    server_name gateway.paqsystems.com;

    location /agent-hub {
        proxy_pass http://paqgateway_kestrel;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }

    location /internal {
        return 404;
    }

    location / {
        return 404;
    }
}
EOF

sudo nginx -t && sudo systemctl enable --now nginx && sudo systemctl reload nginx
```

### 8.3 Smoke Nginx (Host correcto)

En AL2023 hay un server default. Pedir *127.0.0.1* **sin** header `Host` puede dar *404* del default, no del vhost Gateway.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -H 'Host: gateway.paqsystems.com' http://127.0.0.1/agent-hub
curl -sS -o /dev/null -w '%{http_code}\n' -H 'Host: gateway.paqsystems.com' http://127.0.0.1/internal/x
```

Esperado: hub ≈ *400* (Kestrel/SignalR ante GET simple); internal *404* (bloqueado por Nginx).

**Si llegase a ocurrir** ambos *404* sin header Host:

- Repetir con `-H 'Host: gateway.paqsystems.com'`.
- Evitar `server_name ... _` si ya hay conflicto con el default (`conflicting server name "_"`).

---

## 9. DNS Route 53

### 9.1 Zona

Zona alojada: **paqsystems.com** (no *paqsystems.ar* — esa es Laravel sitio AR; no *paqsuite.com* — no existe).

### 9.2 Registro

| Campo | Valor |
|--------|--------|
| Nombre | *gateway* → FQDN *gateway.paqsystems.com* |
| Tipo | *A* |
| Valor | *3.142.236.237* (o EIP cuando exista) |
| TTL | *300* (o default) |

Verificación en la EC2:

```bash
getent hosts gateway.paqsystems.com
# Esperado: 3.142.236.237  gateway.paqsystems.com
```

Actualizar también `server_name` en Nginx si se partió de otro nombre:

```bash
sudo sed -i 's/gateway.paqsuite.com/gateway.paqsystems.com/g' /etc/nginx/conf.d/gateway.conf
sudo nginx -t && sudo systemctl reload nginx
```

---

## 10. TLS (Let's Encrypt + certbot)

### 10.1 Prerrequisitos

- DNS A ya resuelve a la IP pública de la EC2.
- SG permite *80* y *443*.
- Nginx activo con vhost para *gateway.paqsystems.com*.

### 10.2 Comando (instalación real)

```bash
sudo dnf install -y certbot python3-certbot-nginx
sudo certbot --nginx -d gateway.paqsystems.com \
  --non-interactive --agree-tos \
  --register-unsafely-without-email --redirect
```

Resultado esperado:

- Cert en */etc/letsencrypt/live/gateway.paqsystems.com/*
- Vhost actualizado en */etc/nginx/conf.d/gateway.conf*
- URL *https://gateway.paqsystems.com*

Renovación: timer/cron de certbot (dejar habilitado).

### 10.3 Verificación pública

```bash
curl -sS -o /dev/null -w 'hub:%{http_code}\n' https://gateway.paqsystems.com/agent-hub
curl -sS -o /dev/null -w 'internal:%{http_code}\n' https://gateway.paqsystems.com/internal/jobs/send
```

Esperado (*2026-09-05*): `hub:400`, `internal:404`.

---

## 11. Checklist de aceptación (estado referencia *2026-09-05*)

| # | Prueba | Resultado referencia |
|---|--------|----------------------|
| 1 | `systemctl status paqgateway` → active | OK |
| 2 | `ss` → `0.0.0.0:5100` | OK |
| 3 | Local `GET /internal/agents/.../status` + API key → 200 | OK |
| 4 | HTTPS hub público → ~400 | OK |
| 5 | HTTPS `/internal` → 404 Nginx | OK |
| 6 | Desde Forge → `http://10.0.1.224:5100/...` → 200 | OK |
| 7 | `UseDevAuthStub=false` | OK |
| 8 | SG sin 1433 público | OK |
| 9 | WSS real con agente / LabAgentMock desde Internet | Pendiente producto |
| 10 | Laravel TANGO `.env` con URL interna + keys cableadas | Pendiente app |

---

## 12. Configuración Laravel (Forge) — pendiente de app

No se hace en la EC2 Gateway. En TANGO / Forge:

| Ítem | Valor de referencia |
|------|---------------------|
| Base URL Gateway (jobs/status) | *http://10.0.1.224:5100* |
| Header | `X-Paq-Internal-Api-Key` = mismo que `Gateway__InternalApiKey` |
| Auth agentes | Endpoint Laravel que consume `LaravelApi__*` del Gateway |
| **No** usar | *https://gateway.paqsystems.com* para `/internal/*` |
| **No** usar | Tailscale (*100.x*) como camino de producción |

---

## 13. Recrear una instancia nueva (resumen operativo)

1. Nueva EC2 en *misma VPC/subnet/región*; nuevo Name; anotar IDs/IPs.
2. SG con reglas §2 (ajustar origen *5100* al SG Laravel vigente).
3. Key PEM + SSH.
4. Runtime .NET 8 + Nginx + usuario *paqgateway*.
5. `dotnet publish` → scp → */opt/paqgateway*.
6. `/etc/paqgateway/env` con *0.0.0.0:5100* y keys nuevas o rotadas.
7. systemd enable --now.
8. Nginx HTTP → DNS A → certbot → HTTPS.
9. Smoke local + desde Forge + público.
10. Actualizar ficha §14 y `urls-deploy.md` / keys fuera de git.
11. Preferir **EIP** antes de fijar DNS.

---

## 14. Ficha viva — instalación *Paq-Gateway-IA*

```text
Cuenta AWS / región: 655232113361 / us-east-2
VPC: vpc-0588b88f9c6772017 (paq-2021)
Subnet: subnet-0b2e94121d57cadd1
EC2: i-026ab0a7c3a957fd2  Name=Paq-Gateway-IA
AMI: Amazon Linux 2023   Tipo: t3.micro   User: ec2-user
IP privada: 10.0.1.224
IP pública: 3.142.236.237 (EIP fija: pendiente opcional)
SG: sg-038e5fa123db1b5c8 (paq-gateway-ia)
  443 ← 0.0.0.0/0
  80  ← 0.0.0.0/0
  5100 ← sg-012112202a70d9d29 (Forge)
  22  ← IP oficina /32
Forge: i-0ab40b2f17c7894c9 / 10.0.1.147 / sg-012112202a70d9d29
DNS: gateway.paqsystems.com → 3.142.236.237 (zona paqsystems.com)
TLS: Let's Encrypt → /etc/letsencrypt/live/gateway.paqsystems.com/
Nginx: /etc/nginx/conf.d/gateway.conf
Kestrel: ASPNETCORE_URLS=http://0.0.0.0:5100  puerto 5100
URL agentes: https://gateway.paqsystems.com/agent-hub
URL interna Laravel→GW: http://10.0.1.224:5100
LaravelApi__BaseUrl: http://10.0.1.147
Keys (fuera de git): C:\Programacion\KEYS\paq-gateway-ia\keys-solicitados-instalacion.txt
PEM: C:\Users\PabloQ\.ssh\pq-ia-gateway.pem
Fecha alta: 2026-09-05
```

---

## 15. Contingencias (solo problemas de sistema / diseño)

| Síntoma | Causa probable | Acción |
|---------|----------------|--------|
| Forge no conecta a `:5100` | Bind solo `127.0.0.1` | `ASPNETCORE_URLS=http://0.0.0.0:5100` + restart; SG Forge→5100 |
| `nginx -t` OK pero curl a IP da 404 en hub | Server default / Host | Probar con `Host: gateway.paqsystems.com` |
| Certbot challenge falla | SG sin 80 o DNS mal | Abrir 80; corregir A |
| Unit no arranca / permission env | Owner/chmod | `root:paqgateway` `640`; user servicio *paqgateway* |
| Cadena `&&` se corta tras `grep` env | Sin sudo | `sudo grep` / `sudo cat` |
| DNS `paqsuite.com` no aparece en Route 53 | Dominio inexistente en cuenta | Usar *gateway.paqsystems.com* |
| IP pública cambió tras stop/start | Sin EIP | Asociar EIP; actualizar A |
| `404` en `/internal/status` | Ruta inexistente | Usar `/internal/agents/{id}/status` |
| PEM SSH rechazada en Windows | ACL demasiado abierta | Mover a `.ssh` y restringir ACL al usuario |

---

## 16. Referencias

- Runbook índice: [../deploy-gateway-aws.md](../deploy-gateway-aws.md)
- URLs: [../urls-deploy.md](../urls-deploy.md)
- TR-003: [../../04-tareas/001-Conectividad/TR-003-deploy-gateway-aws.md](../../04-tareas/001-Conectividad/TR-003-deploy-gateway-aws.md)
- HU-002: [../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md](../../03-historias-usuario/001-Conectividad/HU-002-gateway-aws.md)
- Lab local: [../lab-local.md](../lab-local.md)
