# F0 — Inventario formal dual-path (Framework · Tango · Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-12 |
| Repos | FRAMEWORK · TANGO · AgenteCliente |
| Plan maestro | [plan-20260912-desarrollo-hibrido-plataforma-dominio.md](./plan-20260912-desarrollo-hibrido-plataforma-dominio.md) |
| Antecedente | [plan-20260908-integracion-tango-framework-agente.md](./plan-20260908-integracion-tango-framework-agente.md) (F0.1–0.3 pendientes → **cerrados aquí**) |
| Estado lab | **D1–D5 + D6.1–D6.4.4 + D6.5.1–D6.5.25 + D6.6–D6.7 + D6.8–D6.11.5** en código. Deploy SP `000001`–`000123` (+ notas `000124`–`000127`) + restart pendiente |

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
| `clientes.buscar` | ClientesService | `PAQ_Clientes_Buscar` | ✓ D1 (2026-09-13) |
| `clientes.obtener` | ClientesService | `PAQ_Clientes_Obtener` | ✓ D1 (2026-09-13) |
| `articulos.buscar` | ArticulosService | `PAQ_Articulos_Buscar` | ✓ D1 (2026-09-13) |
| `articulos.obtener` | ArticulosService | `PAQ_Articulos_Obtener` | ✓ D1 (2026-09-13) |
| `stock.consultar` | StockService | `PAQ_Stock_Consultar` | ✓ D1 (2026-09-13) |
| `saldos.consultar` | SaldosService | `PAQ_Saldos_Consultar` | ✓ D1 (2026-09-13) |
| `pedidos.pendientes` | PedidosService | `PAQ_Pedidos_Pendientes` | ✓ D1 (2026-09-13) |
| `comprobantes.recientes` | ComprobantesService | `PAQ_Comprobantes_Recientes` | ✓ D1 (2026-09-13) |
| `tango.version` | TangoVersionGateway | HKLM (Astor/Tango2000) | ✓ D1 (2026-09-13) |
| `informes.*` (16) | InformesGestion/* | `PAQ_*` company dual-RS | ✓ D2 (2026-09-13) |
| `Acopios.*` (14) | Acopios* services | `PAQ_Acopios_*` | ✓ D3 (2026-09-13) |
| `robinet.deudas` / `pedidos` / `cobranzas` | Robinet*Service | `PAQ_Robinet_*` | ✓ D4 (2026-09-13) |
| `PartesProduccion.Parametros.List` | PartesProduccionParametros | `PAQ_PartesProduccion_ParametrosList` | ✓ D5 (2026-09-13) |
| `PartesProduccion.InformesGestion.List` | InformesGestionService | `PAQ_PartesProduccion_InformesGestion` | ✓ D5 (2026-09-13) |
| `PedidosVenta.Get` | PedidosVentaService::show | `PAQ_PedidosVenta_Get` | ✓ D6.1 (2026-09-13) |
| `PedidosVenta.Create` | PedidosVentaService::store | runner TX (MVP) | ✓ D6.2 (2026-09-14) |
| `PedidosVenta.Update` | PedidosVentaService::update | runner TX patch (MVP) | ✓ D6.3 (2026-09-14) |
| `PedidosVenta.Delete` | PedidosVentaService::destroy | runner TX delete (MVP) | ✓ D6.3b (2026-09-14) |
| `OrdenesCompra.Get` | OrdenesCompraService::show | `PAQ_OrdenesCompra_Get` | ✓ D6.4.1 (2026-09-14) |
| `OrdenesCompra.Create` | OrdenesCompraService::store | runner TX (MVP) | ✓ D6.4.2 (2026-09-14) |
| `OrdenesCompra.Update` | OrdenesCompraService::update | runner TX patch (MVP) | ✓ D6.4.3 (2026-09-14) |
| `OrdenesCompra.Delete` | OrdenesCompraService::destroy | runner TX anulación (MVP) | ✓ D6.4.4 (2026-09-14) |
| `PartesProduccion.Maquinas.Get` | MaquinasService::show | `PAQ_PartesProduccion_MaquinasGet` | ✓ D6.5.1 (2026-09-14) |
| `PartesProduccion.Maquinas.Create` | MaquinasService::store | `PAQ_PartesProduccion_MaquinasCreate` | ✓ D6.5.2 (2026-09-14) |
| `PartesProduccion.Maquinas.Update` | MaquinasService::update | `PAQ_PartesProduccion_MaquinasUpdate` | ✓ D6.5.3 (2026-09-14) |
| `PartesProduccion.Maquinas.Delete` | MaquinasService::destroy | `PAQ_PartesProduccion_MaquinasDelete` | ✓ D6.5.4 (2026-09-14) |
| `PartesProduccion.TiposTarea.Get` | TiposTareaService::show | `PAQ_PartesProduccion_TiposTareaGet` | ✓ D6.5.5 (2026-09-14) |
| `PartesProduccion.TiposTarea.Create` | TiposTareaService::store | `PAQ_PartesProduccion_TiposTareaCreate` | ✓ D6.5.6 (2026-09-14) |
| `PartesProduccion.TiposTarea.Update` | TiposTareaService::update | `PAQ_PartesProduccion_TiposTareaUpdate` | ✓ D6.5.7 (2026-09-14) |
| `PartesProduccion.TiposTarea.Delete` | TiposTareaService::destroy | `PAQ_PartesProduccion_TiposTareaDelete` | ✓ D6.5.8 (2026-09-14) |
| `PartesProduccion.Operaciones.Get` | OperacionesService::show | `PAQ_PartesProduccion_OperacionesGet` | ✓ D6.5.9 (2026-09-14) |
| `PartesProduccion.Operaciones.Create` | OperacionesService::store | `PAQ_PartesProduccion_OperacionesCreate` | ✓ D6.5.10 (2026-09-14) |
| `PartesProduccion.Operaciones.Update` | OperacionesService::update | `PAQ_PartesProduccion_OperacionesUpdate` | ✓ D6.5.11 (2026-09-14) |
| `PartesProduccion.Operaciones.Delete` | OperacionesService::destroy | `PAQ_PartesProduccion_OperacionesDelete` | ✓ D6.5.12 (2026-09-14) |
| `PartesProduccion.Turnos.Get` | TurnosService::show | `PAQ_PartesProduccion_TurnosGet` | ✓ D6.5.13 (2026-09-14) |
| `PartesProduccion.Turnos.Create` | TurnosService::store | `PAQ_PartesProduccion_TurnosCreate` | ✓ D6.5.14 (2026-09-14) |
| `PartesProduccion.Turnos.Update` | TurnosService::update | `PAQ_PartesProduccion_TurnosUpdate` | ✓ D6.5.15 (2026-09-14) |
| `PartesProduccion.Turnos.Delete` | TurnosService::destroy | `PAQ_PartesProduccion_TurnosDelete` | ✓ D6.5.16 (2026-09-14) |
| `PartesProduccion.ConceptosTiempo.Get` | ConceptosTiempoService::show | `PAQ_PartesProduccion_ConceptosTiempoGet` | ✓ D6.5.17 (2026-09-14) |
| `PartesProduccion.ConceptosTiempo.Create` | ConceptosTiempoService::store | `PAQ_PartesProduccion_ConceptosTiempoCreate` | ✓ D6.5.18 (2026-09-14) |
| `PartesProduccion.ConceptosTiempo.Update` | ConceptosTiempoService::update | `PAQ_PartesProduccion_ConceptosTiempoUpdate` | ✓ D6.5.19 (2026-09-14) |
| `PartesProduccion.ConceptosTiempo.Delete` | ConceptosTiempoService::destroy | `PAQ_PartesProduccion_ConceptosTiempoDelete` | ✓ D6.5.20 (2026-09-14) |
| `PartesProduccion.Maquinas.List` | MaquinasService::index | `PAQ_PartesProduccion_MaquinasList` | ✓ D6.5.21 (2026-09-14) |
| `PartesProduccion.TiposTarea.List` | TiposTareaService::index | `PAQ_PartesProduccion_TiposTareaList` | ✓ D6.5.22 (2026-09-14) |
| `PartesProduccion.Operaciones.List` | OperacionesService::index | `PAQ_PartesProduccion_OperacionesList` | ✓ D6.5.23 (2026-09-14) |
| `PartesProduccion.Turnos.List` | TurnosService::index | `PAQ_PartesProduccion_TurnosList` | ✓ D6.5.24 (2026-09-14) |
| `PartesProduccion.ConceptosTiempo.List` | ConceptosTiempoService::index | `PAQ_PartesProduccion_ConceptosTiempoList` | ✓ D6.5.25 (2026-09-14) |
| `PartesProduccion.OrdenesTrabajo.List` | OrdenesTrabajoService::index | `PAQ_PartesProduccion_OrdenesTrabajoList` | ✓ D6.8.1 (2026-09-14) |
| `PartesProduccion.OrdenesTrabajo.Get` | OrdenesTrabajoService::show / showByCodigo | `PAQ_PartesProduccion_OrdenesTrabajoGet` | ✓ D6.8.2 (2026-09-14) |
| `PartesProduccion.OrdenesTrabajo.Create` | OrdenesTrabajoService::store | runner orquestado (`000091` nota) | ✓ D6.8.3 (2026-09-14) |
| `PartesProduccion.Asignaciones.List` | AsignacionesService::index | `PAQ_PartesProduccion_AsignacionesList` | ✓ D6.9.1 (2026-09-14) |
| `PartesProduccion.Asignaciones.Get` | AsignacionesService::show | `PAQ_PartesProduccion_AsignacionesGet` | ✓ D6.9.2 (2026-09-14) |
| `PartesProduccion.Asignaciones.Create` | AsignacionesService::store | runner `AsignacionesCreateRunner` (`000098`) | ✓ D6.9.3 (2026-09-14) |
| `MovimientosTesoreria.Get` | MovimientosTesoreriaService::show | `PAQ_MovimientosTesoreria_Get` | ✓ D6.6.1 (2026-09-14) |
| `MovimientosTesoreria.Create` | MovimientosTesoreriaService::store / storeInterno | runner orquestado (`000078` nota) | ✓ D6.6.2 (2026-09-14) |
| `MovimientosTesoreria.Reversion` | MovimientosTesoreriaService::revertir | runner orquestado (`000079` nota) | ✓ D6.6.3 (2026-09-14) |
| `AsientosContables.Get` | AsientosContablesService::show | `PAQ_AsientosContables_Get` | ✓ D6.7.1 (2026-09-14) |
| `AsientosContables.Create` | AsientosContablesService::store | runner orquestado (`000081` nota) | ✓ D6.7.2 (2026-09-14) |
| `AsientosContables.Update` | AsientosContablesService::update | runner orquestado (`000082` nota) | ✓ D6.7.3 (2026-09-14) |
| `AsientosContables.Delete` | AsientosContablesService::destroy | runner orquestado (`000083` nota) | ✓ D6.7.4 (2026-09-14) |
| `PartesProduccion.PartesOperario.Get` | PartesOperarioService::show | `PAQ_PartesProduccion_PartesOperarioGet` | ✓ D6.10.1 (2026-09-15) |
| `PartesProduccion.PartesOperario.Create` | PartesOperarioService::store | runner orquestado (`000113` nota) | ✓ D6.10.2 (2026-09-15) |
| `PartesProduccion.PartesOperario.Update` | PartesOperarioService::update | runner orquestado (`000114` nota) | ✓ D6.10.3 (2026-09-15) |
| `PartesProduccion.PartesOperario.Enviar` | PartesOperarioService::enviar | runner orquestado (`000115` nota) | ✓ D6.10.4 (2026-09-15) |
| `PartesProduccion.PartesOperario.Aprobar` | PartesOperarioService::aprobar | runner orquestado (`000116` nota) | ✓ D6.10.5 (2026-09-15) |
| `PartesProduccion.PartesOperario.Devolver` | PartesOperarioService::devolver | runner orquestado (`000117` nota) | ✓ D6.10.6 (2026-09-15) |
| `PartesProduccion.PartesOperario.Cerrar` | PartesOperarioService::cerrar | runner orquestado (`000118` nota) | ✓ D6.10.7 (2026-09-15) |
| `PartesProduccion.PartesOperario.ParteContexto` | PartesOperarioService::parteContexto | `PAQ_PartesProduccion_PartesOperarioParteContexto` | ✓ D6.10.8 (2026-09-15) |
| `PartesProduccion.PartesOperario.MisPartes.List` | PartesOperarioService::misPartesList | `PAQ_PartesProduccion_PartesOperarioMisPartesList` | ✓ D6.10.9 (2026-09-15) |
| `PartesProduccion.PartesOperario.PartesRevisar.List` | PartesOperarioService::partesRevisarList | `PAQ_PartesProduccion_PartesOperarioPartesRevisarList` | ✓ D6.10.10 (2026-09-15) |
| `PartesProduccion.PartesOperario.PartesRevisar.Get` | PartesOperarioService::partesRevisarGet | `PAQ_PartesProduccion_PartesOperarioPartesRevisarGet` | ✓ D6.10.11 (2026-09-15) |
| `PartesProduccion.PartesOperario.Entradas.List` | PartesEntradasService::index | `PAQ_PartesProduccion_PartesOperarioEntradasList` | ✓ D6.11.1 (2026-09-15) |
| `PartesProduccion.PartesOperario.Entradas.Create` | PartesEntradasService::store | runner orquestado (`000124` nota) | ✓ D6.11.2 (2026-09-15) |
| `PartesProduccion.PartesOperario.Entradas.Update` | PartesEntradasService::update | runner orquestado (`000125` nota) | ✓ D6.11.3 (2026-09-15) |
| `PartesProduccion.PartesOperario.Entradas.Delete` | PartesEntradasService::destroy | runner orquestado (`000126` nota) | ✓ D6.11.4 (2026-09-15) |
| `PartesProduccion.PartesOperario.Entradas.Reclasificar` | PartesEntradasService::reclasificar | runner orquestado (`000127` nota) | ✓ D6.11.5 (2026-09-15) |

### Pedidas por Tango, **no** en whitelist

| Oleada | Ops |
|--------|-----|
| F2 opcional | `auth.session` |
| **D6.7 cerrado** | Asientos Get/Create/Update/Delete |
| **D6.6 resto** | Tesorería Update/Delete |
| **D6.9 cerrado** | Asignaciones cabecera + Items + OperariosPlan (`000096`–`000111`) |
| **D6.10 oleada A cerrado** | PartesOperario Get/Create/Update/Enviar/Aprobar/Devolver/Cerrar |
| **D6.10 oleada B cerrado** | ParteContexto / MisPartes.List / PartesRevisar.List/Get (`000119`–`000122`) |
| **D6.11 cerrado** | Entradas List/Create/Update/Delete/Reclasificar (`000123`–`000127`) |

---

## Done when (F0)

- [x] Matriz A Framework
- [x] Matriz B gap Tango
- [x] Matriz C ops
- [x] Método de ataque documentado (híbrido)
- [x] Aceptación humana del plan reformulado (**F4 completo antes de D1**, 2026-09-12)

---

## Referencias

- Override canal: Framework `docs/10-overrides-framework/07-agente-gateway-canal.md`
- SDD Framework: `docs/08-control/sdd-arranque-integracion-agente-plan-100.md`
- SDD Tango: `docs/08-control/sdd-arranque-integracion-agente-plan-100.md`
- F F3: `f-20260912-GEN-33-f1-f2-f3.md` (FW) · `f-20260912-menu-authorized.md` (Agente) · `f-20260912-GEN-33-adopcion-f3-menu.md` (Tango)
