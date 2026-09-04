# Instalación del agente (cliente)

| Campo | Valor |
|-------|--------|
| Estado | **Borrador de alcance** — el instructivo paso a paso se escribe en **HU-008 / TR-009** (cuando el instalador y el caño estén verdes; D10) |
| Público objetivo | Administrador del servidor SQL del cliente |
| Descarga | Sitio **público** (MVP: GitHub Releases; ver D9 y [urls-deploy.md](urls-deploy.md)) |

Lab de desarrollo (sin instalador): [lab-local.md](lab-local.md).  
Gateway en AWS (operador PaqSystems): [deploy-gateway-aws.md](deploy-gateway-aws.md).

---

## Compromiso de entrega (TR-009)

Cuando se ejecute TR-009, este archivo debe quedar como **instructivo completo** para instalar el agente en **cada servidor nuevo**, sin preguntarle al programador:

1. **Dónde descargar** el instalador (URL pública estable + SHA256 de la release).
2. Prerrequisito: .NET 8 Desktop Runtime x64 (link Microsoft).
3. Datos que pide el instalador y de dónde salen (alta Laravel: AgentId / ClientId / AgentToken / Gateway URL; SQL local: servidor, base, usuario, password).
4. Pasos: ejecutar .exe como Administrador → probar SQL → probar salida al Gateway → instalar servicio.
5. Cómo verificar: servicio Windows `PaqAgent` Running, logs, online en PaqSuite.
6. Qué **no** configurar: Tailscale, IP pública del cliente, 1433 a Internet, editar JSON a mano en producción.
7. Troubleshooting mínimo (servicio no parte, SQL fail, agente no online / 443 saliente).

**Prohibido en el texto final:** Tailscale como requisito; `dev-agent-token`; fallback SQL modo agente.

---

## Descarga pública (MVP — D9)

Objetivo de producto: en **cada servidor nuevo** se baja el instalador desde un lugar público, sin repo privado ni Visual Studio.

| Ítem | Valor MVP |
|------|-----------|
| Canal | GitHub Releases del repo del agente (público) |
| URL canónica | Ver [urls-deploy.md](urls-deploy.md) → `releases/latest` |
| Asset | `PaqAgentInstaller.zip` (o el .exe empaquetado) |
| Integridad | SHA256 en notas de release / `SHA256SUMS` |
| Gateway URL por defecto en el instalador | `https://gateway.paqsuite.com/agent-hub` |

Fase 2 (no bloquea MVP): botón “Descargar agente” en PaqSuite, o bucket propio, apuntando al mismo artefacto.

Si el nombre del repo en GitHub cambia, actualizar **solo** [urls-deploy.md](urls-deploy.md) y este instructivo; el resto del SPEC sigue hablando de “releases públicas”.

---

## Relación con el alta en Laravel

Antes de instalar en el servidor del cliente:

1. Operador PaqSystems da de alta el tenant modo agente (HU-001) y obtiene AgentId, ClientId, AgentToken (una vez).
2. Entrega esos datos + link de descarga al administrador del servidor.
3. El administrador descarga, instala y deja el servicio corriendo.
4. PaqSuite debe ver el agente **online** (heartbeat + TTL).

Sin alta previa no hay token válido que pegar en el instalador.
