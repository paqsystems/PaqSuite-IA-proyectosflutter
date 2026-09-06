# TR-004 — Auto-instalador

| Campo | Valor |
|-------|--------|
| TR | TR-004 |
| Estado | Finalizado |
| HU | [HU-003](../../03-historias-usuario/001-Conectividad/HU-003-auto-instalador.md) |
| SPEC | [SPEC-AGW-001](../../02-producto/SPEC-AGW-001-producto.md) §5, §8–§10 |
| Decisiones | D1, D9, D10, D14, D19 |
| **Repo** | **este** (`src/PaqAgentInstaller` + publish `src/PaqAgent`) — **no** TANGO |
| Orden D10 | 7 |
| Dependencia | HU-004…HU-007 Finalizado; HU-001 (identidad) |
| Clasificación | HU COMPLEJA → **D1 obligatorio** |
| C1 | [c1-20260905-TR-004.md](../../08-control/c1-20260905-TR-004.md) — Apto; Q1–Q8 |
| D1 | [d1-20260906-TR-004.md](../../08-control/d1-20260906-TR-004.md) — confirmado; D ejecutado |
| F1 | [f1-20260906-TR-004.md](../../08-control/f1-20260906-TR-004.md) — Aprobado con observaciones |
| F | [f-20260906-TR-004.md](../../08-control/f-20260906-TR-004.md) — apto Finalizado (salvedad release GitHub) |

### Decisiones cerradas (post-C1)

| ID | Tema | Decisión |
|----|------|----------|
| Q1 | Default Gateway URL | `https://gateway.paqsystems.com/agent-hub` (editable) |
| Q2 | Zip | Instalador + `agent/` publish **self-contained** win-x64 PaqAgent |
| Q3 | Prueba Gateway | HTTPS/alcance hub; no exigir online; abort si falla salvo override D14 |
| Q4 | Oferta runtime | Descarga oficial Microsoft (URL/`winget`); sin redist embebido en MVP |
| Q5 | Entrypoint | Instalador **self-contained** win-x64 |
| Q6 | Puerto SQL | Campo UI + `sql.port` opcional en `AgentOptions` / JSON |
| Q7 | Servicio | Nombre fijo `PaqAgent` |
| Q8 | TLS SQL | Defaults UI `encrypt=true`, `trustServerCertificate=true` (avanzado editable) |
| D19 | Paso 0 | Detectar Desktop x64; aviso + posible reinicio; SHOULD descarga; Continuar con ACK |

### Repo

| Pieza | Dónde |
|-------|--------|
| Asistente | `src/PaqAgentInstaller` |
| Binarios servicio | `agent/` en el zip (publish PaqAgent SC) |
| `appsettings.local.json` | Alineado a `AgentOptions` (+ `sql.port`) |
| Default install | `C:\PaqSystems\PaqAgent` |
| `empresas_conexion` | Fuera de alcance |

### Asistente por pasos

| Paso | Nombre | Responsabilidad |
|-----:|--------|-----------------|
| 0 | Runtime | D19 (detección, aviso, reinicio, oferta SHOULD) |
| 1 | Credenciales | SPEC §5 + puerto + TLS avanzado (Q8) |
| 2 | Pruebas | SQL + Gateway (Q3) + override D14 |
| 3 | Instalar | Copiar `agent/`; local.json; servicio `PaqAgent` |
| 4 | Resultado | Running / error |

### Tareas

- [x] Shell asistente pasos **0–4**.
- [x] Paso 0 D19 MUST + SHOULD (Q4); Continuar con ACK si Desktop falta (SC).
- [x] Instalador publish self-contained (Q5) — flags en [empaquetado-instalador.md](../../06-operacion/empaquetado-instalador.md).
- [x] Paso 1: campos §5; AgentToken sin default; default Gateway Q1; `sql.port` (Q6); TLS Q8.
- [x] Validar vacíos; sin `dev-agent-token`; sin IP/Tailscale.
- [x] Paso 2: probar SQL; probar Gateway (Q3); override D14 default off.
- [x] Paso 3: copiar agent SC; escribir local.json (confirmar overwrite si existe); servicio `PaqAgent` auto-start solo tras OK.
- [x] Paso 4: resultado accionable.
- [x] Zip release + SHA256 (D9); notas prerrequisito Desktop — doc ops (asset GitHub al publicar).
- [x] Tests: validación; detección runtime; smoke manual Windows en Traza.
- [x] Ajuste `AgentOptions` / example con `sql.port` si aplica.

### Prohibido

Tailscale; `dev-agent-token`; escribir `empresas_conexion`; SQL libre.

### Criterios HU-003 (verificación D + F1)

| CA | Cómo se verificó |
|----|------------------|
| 1 | UI paso 1 + CredentialValidator / tests |
| 2 | Default `https://gateway.paqsystems.com/agent-hub` |
| 3–5 | Gates SQL/Gateway/vacíos; unitarios |
| 6 | Smoke: servicio Running + local.json en `C:\PaqSystems\PaqAgent` |
| 7 | Sin IP/Tailscale |
| 8–9 | Doc empaquetado; release GitHub pendiente de publicación |
| 10–11 | Paso 0 D19 + wizard 0→4 (UX post-smoke) |

### Traza

| | |
|--|--|
| Archivos | `src/PaqAgentInstaller/*`; `SqlOptions.Port` + factory; example JSON; `tests/PaqAgentInstaller.Tests`; `docs/06-operacion/empaquetado-instalador.md`; f1/f 20260906 |
| Comandos | Unitarios F1: **6+2 passed**; smoke wizard lab → `Get-Service PaqAgent` Running |
| Notas | Finalizado por autorización humana 2026-09-06. Default install `C:\PaqSystems\PaqAgent`. |
| Pendientes | Zip+SHA256 en release GitHub; commit cuando lo pidan |

Siguiente D10: **HU-008** (documentación instalación).
