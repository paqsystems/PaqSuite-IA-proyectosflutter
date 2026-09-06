# Instalación del agente (cliente)

| Campo | Valor |
|-------|--------|
| Estado | Instructivo **final** (HU-008 / TR-009) |
| Público objetivo | Administrador del servidor SQL del cliente |
| Descarga | GitHub Releases (público) — [urls-deploy.md](urls-deploy.md) |
| Empaquetado (build) | [empaquetado-instalador.md](empaquetado-instalador.md) |
| Gateway (operador) | [deploy-gateway-aws.md](deploy-gateway-aws.md) |
| Lab desarrollo | [lab-local.md](lab-local.md) |

**Prohibido:** Tailscale; IP pública del SQL hacia Internet; abrir 1433 a Internet; `dev-agent-token`; editar `appsettings.local.json` a mano en producción; fallback SQL modo agente.

---

## Checklist de alta de cliente (10 pasos)

Copiable para el operador PaqSystems + admin del servidor:

1. Alta modo agente en Laravel (TANGO) → obtener **AgentId**, **ClientId**, **AgentToken** (el token se muestra **una sola vez**).
2. Confirmar Gateway de producción reachable: `https://gateway.paqsystems.com/agent-hub`.
3. Entregar al admin del servidor: los tres valores + esta guía + URL de descarga.
4. En el servidor Windows del cliente: instalar **.NET 8 Desktop Runtime x64** si el instalador lo pide (o ya está).
5. Descargar el zip desde `releases/latest`, verificar **SHA256**, descomprimir.
6. Ejecutar `PaqAgentInstaller.exe` **como Administrador**.
7. Completar el asistente: runtime → credenciales (identidad + SQL + Gateway) → Probar SQL → Probar Gateway → Instalar.
8. Verificar servicio Windows **`PaqAgent`** en estado **Running** (inicio automático).
9. Confirmar en PaqSuite que el agente aparece **online** (heartbeat; TTL ~90 s).
10. Guardar evidencias (captura servicio / online) y archivar el token fuera del ticket público.

---

## 1. Dónde descargar

| Ítem | Valor |
|------|-------|
| Canal | GitHub Releases (repo público) |
| URL canónica | https://github.com/paqsystems/PaqSuite-IA-proyectosflutter/releases/latest |
| Asset típico | `PaqAgentInstaller-win-x64.zip` |
| Integridad | SHA256 en notas de la release o archivo `SHA256SUMS` (D9) |
| Gateway URL por defecto en el instalador | `https://gateway.paqsystems.com/agent-hub` (editable) |
| Carpeta de instalación por defecto | `C:\PaqSystems\PaqAgent` |

Cada servidor nuevo se instala desde esa URL; **no** hace falta clonar el código ni Visual Studio.

Verificar SHA256 (PowerShell):

```powershell
Get-FileHash .\PaqAgentInstaller-win-x64.zip -Algorithm SHA256
# Comparar con el hash publicado en la release
```

Si el nombre del repo o del asset cambia, actualizar solo [urls-deploy.md](urls-deploy.md) y esta sección.

---

## 2. Prerrequisitos

| Requisito | Notas |
|-----------|--------|
| Windows x64, cuenta **Administrador** | El instalador pide elevación (servicio Windows) |
| .NET 8 **Desktop** Runtime x64 | El paso 0 del asistente lo detecta; descarga Microsoft: https://dotnet.microsoft.com/download/dotnet/8.0 |
| Salida HTTPS/443 hacia el Gateway | DNS/TLS; sin VPN Tailscale |
| SQL Server local alcanzable | Credenciales de solo lo necesario para el diccionario |
| Datos del alta Laravel | AgentId, ClientId, AgentToken |

El paquete del instalador es **self-contained**; igual conviene tener Desktop Runtime por herramientas y por el aviso del paso 0.

---

## 3. De dónde sale cada dato

| Campo en el instalador | Origen |
|-------------------------|--------|
| AgentId | Alta Laravel (HU-001) |
| ClientId | Alta Laravel |
| AgentToken | Alta Laravel (**una vez**; password-char; sin default) |
| Gateway URL | Default de fábrica prod; lab puede usar `http://127.0.0.1:5100/agent-hub` |
| Servidor / base / usuario / contraseña SQL | Admin del servidor (SQL local del cliente) |
| Puerto SQL | Opcional (vacío = 1433); si el servidor es instancia con `\`, no hace falta puerto |
| encrypt / trustServerCertificate | Avanzado; defaults del asistente: true / true (lab puede desmarcar encrypt) |
| Dir. instalación | Default `C:\PaqSystems\PaqAgent` |

---

## 4. Pasos del asistente

1. **Runtime** — Si falta Desktop 8, aviso claro (+ posible reinicio) y opción de descargar; con runtime OK, continuar.
2. **Credenciales** — Completar identidad, SQL y Gateway; token obligatorio.
3. **Pruebas** — **Probar SQL** (obligatorio OK). **Probar Gateway** (obligatorio OK, salvo override avanzado desmarcado por defecto).
4. **Instalar** — Copia binarios, escribe `appsettings.local.json`, crea servicio **`PaqAgent`** (`start=auto`) solo si las pruebas pasaron.
5. **Resultado** — Debe indicar servicio **Running** y “esperando online en PaqSuite”.

---

## 5. Cómo verificar

```powershell
Get-Service PaqAgent
# Status = Running, StartType = Automatic

Get-Content C:\PaqSystems\PaqAgent\appsettings.local.json
# Debe contener agentId / clientId / gatewayUrl (no compartir el token)

Get-WinEvent -LogName Application -MaxEvents 20 |
  Where-Object { $_.ProviderName -like '*Paq*' -or $_.Message -like '*PaqAgent*' }
# Alternativa: revisar logs junto al binario si el agente escribe archivos de log
```

En PaqSuite: el cliente/agente debe figurar **online** tras el heartbeat (TTL de online ~90 s según SPEC).

---

## 6. Qué no configurar

- Tailscale (ni como “requisito de red”).
- IP pública del servidor SQL ni port-forward de **1433** a Internet.
- Token de desarrollo / `dev-agent-token`.
- Editar JSON a mano en producción (solo lab).
- “Fallback” a SQL directo si el agente está offline (modo agente = error claro).

---

## 7. Troubleshooting mínimo

| Síntoma | Qué mirar |
|---------|-----------|
| Instalador no abre / UAC | Ejecutar como Administrador |
| Paso 0: falta runtime | Instalar Desktop 8 x64; reinicio posible; volver a detectar |
| Probar SQL falla | Servidor/base/usuario/clave; firewall local; puerto; encrypt/trust |
| Probar Gateway falla | DNS, TLS, salida **443**; URL correcta; no usar IP Tailscale |
| No crea el servicio | No avanzar sin SQL OK (y Gateway OK u override); elevación admin |
| Servicio no parte | `Get-Service PaqAgent`; Event Viewer; `appsettings.local.json` junto al exe |
| Agente no online | Gateway URL prod; token válido del alta; 443 saliente; esperar TTL heartbeat |

---

## Relación con el alta en Laravel

1. Operador PaqSystems da de alta el tenant modo agente y obtiene AgentId / ClientId / AgentToken.
2. Entrega esos datos + link de descarga al administrador del servidor.
3. El administrador descarga, verifica SHA256, instala y deja el servicio corriendo.
4. PaqSuite debe ver el agente **online**.

Sin alta previa no hay token válido para el instalador.
