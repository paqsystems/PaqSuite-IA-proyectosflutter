# TR-007 — Operación piloto (`auth.login`)

| Campo | Valor |
|-------|--------|
| TR | TR-007 |
| Estado | Pendiente |
| HU | [HU-006](../../03-historias-usuario/001-Conectividad/HU-006-auth-login.md) |
| Repos | este + **TANGO** |
| Orden D10 | 5 |

### Tareas

- [ ] Handler específico `auth.login` (multi result set) + registro en lista blanca.
- [ ] Migración/script SQL del SP piloto `PAQ_Auth_Login` (reutilizar archivo existente si ya está bien). No migraciones masivas.
- [ ] Laravel llama `auth.login` vía Gateway; normaliza JSON camelCase; propaga `traceId`.
- [ ] Lista blanca: todo lo demás `OPERATION_NOT_ALLOWED` (incl. `clientes.buscar` hasta fase 2).
- [ ] Test: operación no listada rechazada; login feliz con SQL de laboratorio.
- [ ] Sin SQL libre desde AWS. Sin Tailscale.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Trabajo Laravel en `PaqSuite-IA-TANGO`. |
| Pendientes | |
