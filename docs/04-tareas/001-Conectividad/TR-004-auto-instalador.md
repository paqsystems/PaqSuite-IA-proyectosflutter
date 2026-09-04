# TR-004 — Auto-instalador

| Campo | Valor |
|-------|--------|
| TR | TR-004 |
| Estado | Pendiente |
| HU | [HU-003](../../03-historias-usuario/001-Conectividad/HU-003-auto-instalador.md) |
| Repo | este (`src/PaqAgentInstaller`) |
| Orden D10 | 7 (después de la vertical lab) |

### Tareas

- [ ] WinForms .NET 8: campos del SPEC sección 5, **AgentToken obligatorio**.
- [ ] Default Gateway URL de producción, editable (`https://gateway.paqsuite.com/agent-hub`).
- [ ] Probar SQL; bloquear instalación si falla o si falta identidad (sin crear servicio).
- [ ] Probar salida al Gateway; si falla, abortar sin servicio (D14). Checkbox override “Instalar de todos modos”, default off.
- [ ] Escribir `appsettings.local.json`; crear servicio `PaqAgent` auto-start **solo** tras SQL OK y (gateway OK o override).
- [ ] Empaquetar zip de release (exe + SNI nativo si aplica). Sin token de GitHub embebido.
- [ ] Publicar **SHA256** del asset en notas de release / `SHA256SUMS` (D9).
- [ ] Test manual documentado en la TR (máquinas Windows); automatizar lo automatizable (validación de campos).
- [ ] No pide IP pública. No pide Tailscale. Sin `dev-agent-token`.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Lab: TR-005 puede usar `appsettings.local.json` manual (D10). Este instalador viene **después**. |
| Pendientes | |
