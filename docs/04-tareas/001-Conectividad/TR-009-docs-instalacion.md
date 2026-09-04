# TR-009 — Documentación de instalación

| Campo | Valor |
|-------|--------|
| TR | TR-009 |
| Estado | Pendiente |
| HU | [HU-008](../../03-historias-usuario/001-Conectividad/HU-008-documentacion-instalacion.md) |
| Repo | este (`docs/06-operacion/`) |
| Orden D10 | 8 |

Los documentos `instalacion-agente.md` (alcance + descarga pública) y `deploy-gateway-aws.md` (checklist AWS) ya tienen borrador. Esta TR los **completa** como instructivos finales cuando el instalador y el deploy estén verdes.

### Tareas

- [ ] Completar `docs/06-operacion/instalacion-agente.md` como instructivo cliente: URL **pública** de descarga, SHA256, prerrequisitos, campos, verificación, troubleshooting.
- [ ] Completar `docs/06-operacion/deploy-gateway-aws.md` con comandos reales de publish/systemd (además del checklist de definición).
- [ ] Asegurar que [urls-deploy.md](../../06-operacion/urls-deploy.md) apunte al canal público vigente (`releases/latest` o el que se acuerde).
- [ ] README del repo alineado al SPEC (nada de “Gateway pendiente de implementar” si ya está).
- [ ] Checklist de alta de cliente: 10 pasos, copiable (alta Laravel → descarga pública → instalar → online).
- [ ] Qué **no** configurar: Tailscale, IP pública, puerto 1433 a Internet, fallback SQL modo agente.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | |
| Pendientes | |
