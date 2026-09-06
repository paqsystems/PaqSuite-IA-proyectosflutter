# HU-008 — Documentación de instalación (cliente y AWS)

| Campo | Valor |
|-------|--------|
| Identificador | HU-008 |
| Estado | Pendiente de Revisión |
| Épica | MVP conectividad (001-Conectividad) |
| Prioridad | MUST |
| Roles | Operador PaqSystems, administrador del cliente |
| Dependencias | HU-002, HU-003 Finalizado |
| Clasificación | HU SIMPLE |
| Repo de implementación | este (`docs/06-operacion/`) |
| TR | [TR-009](../../04-tareas/001-Conectividad/TR-009-docs-instalacion.md) — Pendiente de Revisión |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) |
| C1 | [c1-20260906-TR-009.md](../../08-control/c1-20260906-TR-009.md) — Apto |
| F1 | [f1-20260906-TR-009.md](../../08-control/f1-20260906-TR-009.md) — Aprobado con observaciones |
| F | [f-20260906-TR-009.md](../../08-control/f-20260906-TR-009.md) — apto Finalizado |

### Narrativa

Como **quien instala** quiero **un instructivo paso a paso, con URLs reales de descarga y checklist de AWS**, para repetir el piloto en el siguiente cliente sin preguntarle al programador.

### Criterios de aceptación

Dos instructivos de operación, listos para usar en el siguiente cliente / la siguiente EC2:

| Documento | Audiencia | Contenido mínimo |
|-----------|-----------|------------------|
| [instalacion-agente.md](../../06-operacion/instalacion-agente.md) | Admin del servidor del cliente | Descarga **pública** del instalador, prerrequisitos, campos, verificación online, troubleshooting |
| [deploy-gateway-aws.md](../../06-operacion/deploy-gateway-aws.md) | Operador PaqSystems | VPC, SG, DNS, TLS, systemd, env, prueba hub + `/internal/*` |

Detalle:

1. Dónde descargar el instalador (URL pública estable, p. ej. `releases/latest` + SHA256). Cada servidor nuevo se instala desde ahí, sin acceso al código fuente.
2. Prerrequisito .NET 8 Desktop Runtime (link Microsoft).
3. Qué datos pide el instalador y de dónde sale cada uno (alta Laravel vs SQL local).
4. Cómo verificar servicio + logs + online en PaqSuite.
5. Gateway AWS: VPC, SG, DNS, certificado, systemd, env vars, prueba `diagnostics.run`.
6. Qué **no** configurar: Tailscale, IP pública, puerto 1433 a Internet.
7. Troubleshooting mínimo: servicio no parte, SQL test fail, agente no online (443 saliente).

Sin estos documentos el MVP no se acepta, aunque el código funcione en el laboratorio.

### Verificación (paso D)

| CA | Evidencia |
|----|-----------|
| 1–4, 6–7 | `instalacion-agente.md` completo + checklist 10 pasos |
| 5 | `deploy-gateway-aws.md` §1–10 + §11 diagnostics/auth + §12 prohibidos |
| URLs | `urls-deploy.md` → `PaqSuite-IA-proyectosflutter/releases/latest` |

Siguiente: humano puede marcar **Finalizado** (salvedad: publicar zip+SHA256 en GitHub Releases).
