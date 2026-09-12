# F0 — Inventario formal dual-path (Framework · Tango · Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-12 |
| Repos | FRAMEWORK · TANGO · AgenteCliente |
| Plan maestro | [plan-20260912-desarrollo-hibrido-plataforma-dominio.md](./plan-20260912-desarrollo-hibrido-plataforma-dominio.md) |
| Antecedente | [plan-20260908-integracion-tango-framework-agente.md](./plan-20260908-integracion-tango-framework-agente.md) (F0.1–0.3 pendientes → **cerrados aquí**) |
| Estado lab | F1–F3 verdes: login `agw.` + menú 166 ítems; whitelist agente = `diagnostics.run`, `auth.login`, `menu.authorized` |

Objetivo F0: no integrar a ciegas. Matrices = SoT para priorizar F4–F9 y oleadas D1–D6.

---

## Método de ataque (decisión de plan)

**Híbrido** — no solo fases ni solo vertical slices.

```text
Eje A — Plataforma (Fx): un componente SDK por fase
  Framework dual-path → Tango adopta → Agente op/SP solo si toca SQL tenant

Eje B — Dominio (Dx): una familia de ops Tango ya cableadas con sendJob
  Contrato → handler+SP Agente → smoke lab + regresión sin agente
```

| Opción | Qué es | Por qué no sola |
|--------|--------|-----------------|
| Solo Fx secuencial | F4→F5→…→F9 | Tango ya pide decenas de ops; pantallas quedan degraded hasta el final |
| Solo vertical (módulo Tango end-to-end) | Ej. “cerrar Acopios completo” | Shell post-login aún pega PDO (warmup/prefs); se reimplementa plataforma en el host |
| **Híbrido (elegido)** | Fx por componente **y** Dx en paralelo tras F3 | Desbloquea negocio (D1) sin abandonar MUST SP / adopción SDK |

**Orden práctico propuesto (a validar):**

1. **F4** (params / prefs / warmup) — quita PDO residual del shell.
2. **D1** en paralelo o justo después (maestros + `tango.version`) — valor visible.
3. Luego **F5/F6** (grillas/pivots) intercalado con **D2–D5** según dependencia de pantallas.
4. **F7–F9** y **D6** (SQL-only → sendJob) al final o bajo demanda.

Regla: **una op (o familia) por TR en Agente**; no whitelist masiva.

---

## Tabla A — Componentes `laravel-core`

| Componente | Toca SQL tenant? | Dual-path gateway hoy | Riesgo modo agente | Fase | Notas |
|------------|------------------|----------------------|--------------------|------|-------|
| `Agents/` | No (canal + snapshot) | **Sí** | Bajo | F1–F3 ✓ | Canal; no ops Tango |
| `Tenancy/` | Sí (resolvers/checkers) | **Sí** (`isGatewayMode`, DualPath menú) | Medio | F1–F3 ✓ | Anti-PDO ≠ `shouldUseGateway` |
| `Http/` | Indirecto | **Sí** (Apply, company, procedimiento) | Alto si Apply mal | F1–F3 ✓ | |
| `Security/` | Sí (admin + prefs) | **Parcial** (company DualPath; admin/prefs no) | Alto en ABM | F2 ✓; prefs **F4**; ABM diferido | |
| `Menu/` | Sí (repos) | Path agente = **snapshot F3** (no DualPath en carpeta) | Alto si Eloquent en gateway | F3 ✓ | |
| `Auth/` | Vía host | No en package | Medio | Host + F2/F3 ✓ | |
| `Parametros/` | Sí | **No** | Alto | **F4** | |
| Prefs (`UserPreferencesRepository`) | Sí | **No** | Alto | **F4** | |
| Grid layouts | En **host** hoy | Package **sin** carpeta | Alto | **F5** | Dual-path layouts pendiente en FW |
| `Pivots/` | Sí | **No** | Alto | **F6** | |
| `Notifications/` + `NotificationService/` | Sí | **No** | Alto | **F7** | |
| `Audit/` | Sí | **No** | Medio–alto | **F7** | |
| `Mail/` | No en package | N/A | Bajo | F7 si persiste tenant | |
| `Tasks/` | Sí | **No** | Alto | **F8** | |
| `GruposEmpresarios/` | Sí | **No** | Alto | **F8** | |
| `Arca/` | Store posible SQL | **No** | Medio–alto | **F9** | AFIP remoto ≠ dictionary |
| `Emissions/` | Sí | **No** | Alto | **F9** | |
| `ExcelImport/` | Sí | **No** | Alto | **F9** | |
| `SmartCapture/` / `ChatAssistant/` / `Llm/` | Indirecto | **No** | Bajo–medio | **F9** | Evaluar |
| `I18n/` | No | N/A | Nulo | N/A | Solo adopción |
| `Providers/` / `Support/` / `Console/` | — | DualPath extend en booting | Bajo | F1–F3 / F8 | |

---

## Tabla B — Gap Tango (host)

| Área host | Adopción F1–F3 | ¿PDO/SQL en gateway? | Dep. FW | Prioridad |
|-----------|----------------|----------------------|---------|-----------|
| Canal `AgentGateway*` / `shouldUseGateway` | F1 ✓ | No (HTTP Gateway) | Agents | Mantener |
| ResolveTenant / dictionary / Apply | F1 ✓ (skip en agente) | No si path correcto | Tenancy | Regresión |
| `SetCompanyConnection` | F2 ✓ early-return | No | F1/F2 | Hecho |
| Company Allowed / sin `ValidateCompanyId` | F2 ✓ | Snapshot | F2 | Hecho |
| Sesión `agw.` + store cache | F2 ✓ | Cache host | GatewaySession* | Hecho |
| `auth.session` job | Parcial | Op **no** en whitelist | F2 opcional | Media |
| Login `auth.login` | ✓ | Dual-path | Agente SP | Hecho |
| Menú + `menu.authorized` | F3 ✓ | Snapshot / job | F3 + Agente | Hecho lab |
| Procedimiento DualPath | F3 ✓ | SQL solo no-agente | F3 | Hecho |
| **Warmup** `PerfWarmup` | F4 no | **Sí** `company` Schema/DB | F4 | **Crítica** |
| **Preferencias** usuario | F4 no | **Sí** save USERS | F4 | Alta |
| **Parámetros** gral | F4 no | **Sí** company | F4 | Alta |
| Grillas `PqGridLayout` | F5 no | **Sí** Eloquent | F5 | Alta |
| Pivots | F6 no | **Sí** | F6 | Alta |
| Notifs / Audit | F7 no | Si usan tablas tenant | F7 | Media |
| Tasks / Emfaco grupos | F8 no | **Sí** | F8 | Media |
| Domain **con** `sendJob` | Dual-path host; Agente no | Falla op / degraded | **D1–D5** | Alta |
| Domain **SQL-only** (OC, asientos, partes CRUD, …) | Sin gateway | **Sí** siempre | **D6** | Media–alta |

---

## Tabla C — Ops Agente vs Tango

### En whitelist hoy

| Op | Pedida por | SP/handler | Estado |
|----|------------|------------|--------|
| `diagnostics.run` | MVP / health | DiagnosticsRunner | ✓ |
| `auth.login` | AuthService | `PAQ_Auth_Login` | ✓ |
| `menu.authorized` | GatewayMenuAuthzService | `PAQ_User_Menu_Authorized` | ✓ |

### Pedidas por Tango, **no** en whitelist

| Oleada | Ops |
|--------|-----|
| F2 opcional | `auth.session` |
| **D1** | `tango.version`, `clientes.buscar`, `clientes.obtener`, `articulos.buscar`, `articulos.obtener`, `pedidos.pendientes`, `stock.consultar`, `saldos.consultar`, `comprobantes.recientes` |
| **D2** | `informes.*` (ventas/compras/tesorería/stock — ~15 ops) |
| **D3** | `Acopios.*` (params, pedidos, facturas, saldos, asociaciones) |
| **D4** | `robinet.pedidos`, `robinet.deudas`, `robinet.cobranzas` |
| **D5** | `PartesProduccion.Parametros.List`, `PartesProduccion.InformesGestion.List` (+ CRUD partes = D6 si aún sin sendJob) |
| **D6** | PedidosVenta, OC, Tesorería movs, Asientos, Emfaco, Partes CRUD, … (hoy SQL-only) |
| Tras cada **Fx** | `params.*` / `prefs.*` / `grid.layouts.*` / `pivots.*` / notifs / audit / tasks (nombres a cerrar en SPEC) |

---

## Done when (F0)

- [x] Matriz A Framework
- [x] Matriz B gap Tango
- [x] Matriz C ops
- [x] Método de ataque documentado (híbrido)
- [ ] Aceptación humana del plan reformulado

---

## Referencias

- Override canal: Framework `docs/10-overrides-framework/07-agente-gateway-canal.md`
- SDD Framework: `docs/08-control/sdd-arranque-integracion-agente-plan-100.md`
- SDD Tango: `docs/08-control/sdd-arranque-integracion-agente-plan-100.md`
- F F3: `f-20260912-GEN-33-f1-f2-f3.md` (FW) · `f-20260912-menu-authorized.md` (Agente) · `f-20260912-GEN-33-adopcion-f3-menu.md` (Tango)
