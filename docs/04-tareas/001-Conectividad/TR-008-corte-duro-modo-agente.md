# TR-008 — Corte duro modo agente (legacy SQL directo permanece)

| Campo | Valor |
|-------|--------|
| TR | TR-008 |
| Estado | Pendiente |
| HU | [HU-007](../../03-historias-usuario/001-Conectividad/HU-007-corte-duro-modo-agente.md) |
| **Repo** | **`PaqSuite-IA-TANGO`** (no se scaffoldea ni implementa en este repo) |
| Orden D10 | 6 |

### Tareas

- [ ] Selector: si hay `agent_id` → **solo** Gateway. Offline → error 503 `AGENT_OFFLINE`. Nunca SQL por `host`.
- [ ] Si **no** hay `agent_id` → camino SQL directo legacy **sigue permitido** en MVP (transición hasta transformación total).
- [ ] Quitar o no usar `host` en resolución de consultas live **cuando** hay `agent_id`.
- [ ] Test que falle si se reintroduce fallback SQL para un tenant con `agent_id`.
- [ ] Test (o caso documentado) de tenant sin `agent_id` que aún usa SQL directo.
- [ ] Grep de control: ningún servicio de negocio nuevo mezcla “agente offline → SQL directo”.
- [ ] **Prohibido:** Tailscale, fallback modo agente, `host` como llave de ruteo del tenant con `agent_id`.

### Traza (completar al ejecutar en TANGO)

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Trabajo Laravel: repo TANGO, mismos IDs de HU/TR. |
| Pendientes | |
