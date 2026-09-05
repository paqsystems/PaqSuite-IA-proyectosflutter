# TR-006 — diagnostics.run e2e

| Campo | Valor |
|-------|--------|
| TR | TR-006 |
| Estado | Especificado |
| HU | [HU-005](../../03-historias-usuario/001-Conectividad/HU-005-diagnostics-run.md) |
| Repos | **este** (`PaqAgent`) + **TANGO** (`AgentGatewayClient`) |
| Orden D10 | 4 |
| Dependencia | TR-005 Finalizado |
| C1 | [c1-20260905-TR-006.md](../../08-control/c1-20260905-TR-006.md) — Apto; Q1–Q8 |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| Q1 | Header Laravel→GW | `X-Paq-Internal-Api-Key` (M1); corregir TANGO |
| Q2 | `traceId` | Obligatorio en `sendJob`; generar en Laravel si falta |
| Q3 | Payload success | `agentId`, `agentVersion`, `sqlConnectionOk`, `readiness` (+ opcionales) |
| Q4 | Agente | `diagnostics.run` real (SQL ping); degraded si SQL down |
| Q5 | Laravel | `runDiagnostics` / `sendJob` + config URL interna |
| Q6 | Prueba | Lab primero; AWS en Traza ops |
| Q7 | Timeout | Default 30 s |
| Q8 | Logs duración | Agente + Laravel (+ Gateway ids) |

### Tareas

**Este repo**

- [ ] `diagnostics.run` en PaqAgent: SQL ping + `data` Q3 + readiness; degraded si SQL inaccesible.
- [ ] Sin SQL libre; otras operations no expandir (auth.login = TR-007).
- [ ] Logs de duración del job en agente.
- [ ] Lab: curl/`jobs/send` diagnostics + caso degraded (SQL mal configurado). Actualizar [lab-local.md](../../06-operacion/lab-local.md) tramo 4.
- [ ] Tests automatizados mínimos (mock SQL o branch degraded/success).

**TANGO**

- [ ] Header `X-Paq-Internal-Api-Key`; body con `traceId`.
- [ ] Método `runDiagnostics` (o equiv.) → `sendJob(..., diagnostics.run)`.
- [ ] Logs `traceId` / duración; sin secretos.
- [ ] Sin fallback SQL por `host` si `agent_id` presente (al menos en este camino).

**Ops / e2e**

- [ ] Lab e2e sin Tailscale.
- [ ] (Opcional) AWS: Forge → `http://10.0.1.224:5100` → agente Windows.

### Traza

| | |
|--|--|
| Archivos | |
| Comandos | |
| Notas | Parte Laravel en `PaqSuite-IA-TANGO`. Cliente legacy usaba `X-Internal-Api-Key` sin `traceId` → Q1/Q2. |
| Pendientes | D1 → D |

Siguiente: **paso D1** (plan TR-006).
