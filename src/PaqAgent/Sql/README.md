# SQL diccionario + company — PaqAgent

Scripts del agente para SP del cliente.

## Diccionario (`dictionary/`)

| Sí | No |
|----|-----|
| Base **diccionario** Tango (`USERS`, `pq_empresa`, …) | Tabla Laravel `empresas_conexion` |

Orden SSMS (diccionario):

1. `2026_06_23_000000_b_ensure_users_columns.sql`
2. `2026_06_24_000001_create_paq_auth_login.sql`
3. `2026_06_29_000002_fix_col_rol_pk_fallback.sql`
4. `2026_09_12_000003_create_paq_user_menu_authorized.sql`
5. `2026_09_15_000004_create_paq_auth_change_password.sql` → `PAQ_Auth_ChangePassword` (`auth.changePassword`)
6. `2026_09_16_000005_create_paq_seguridad_admin_lists.sql` → 5 SP listados admin Seguridad (`seguridad.*.list`)

## Company (`company/`) — D1 clientes

Aplicar en la **base operativa** de cada empresa (no el diccionario):

1. `2026_09_13_000001_create_paq_clientes_buscar.sql` → `PAQ_Clientes_Buscar`
2. `2026_09_13_000002_create_paq_clientes_obtener.sql` → `PAQ_Clientes_Obtener`
3. `2026_09_13_000003_create_paq_articulos_buscar.sql` → `PAQ_Articulos_Buscar`
4. `2026_09_13_000004_create_paq_articulos_obtener.sql` → `PAQ_Articulos_Obtener`
5. `2026_09_13_000005_create_paq_stock_consultar.sql` → `PAQ_Stock_Consultar`
6. `2026_09_13_000006_create_paq_saldos_consultar.sql` → `PAQ_Saldos_Consultar`
7. `2026_09_13_000007_create_paq_pedidos_pendientes.sql` → `PAQ_Pedidos_Pendientes`
8. `2026_09_13_000008_create_paq_comprobantes_recientes.sql` → `PAQ_Comprobantes_Recientes`

Jobs company requieren `_database`. `tango.version` **no** usa SQL company: lee HKLM (TR-019).

### F3 Seguridad admin (`000005`)

5 SP dictionary (CC #9): `PAQ_Seguridad_Roles_List`, `Users_List`, `Empresas_List`, `Permisos_List`, `GruposEmpresarios_List`. Catálogo: `SeguridadCatalog` + `SeguridadGatewayRunner`.

Hoy se aplican a mano (SSMS). Circuito objetivo (mismo exe que el agente, install vs update, MULTI/MONO): [circuito-objetos-sql-agente-funcional.md](../../../docs/02-producto/circuito-objetos-sql-agente-funcional.md).

### D2 Informes (`000010`–`000025`)

16 SP dual result-set. Catálogo: `InformesCatalog`.

### D3 Acopios (`000030`–`000043`)

14 SP (params, listas, CRUD acopio/asociación, detalles, batch, listas de precios). Catálogo: `AcopiosCatalog`.

### D4 Robinet (`000044`–`000046`)

3 SP dual result-set (`PAQ_Robinet_Deudas` / `_Pedidos` / `_Cobranzas`). Catálogo: `RobinetCatalog` + runner Informes.

### D5 Partes (`000047`–`000048`)

2 SP (`PAQ_PartesProduccion_ParametrosList`, `PAQ_PartesProduccion_InformesGestion`). Catálogo: `PartesCatalog` + `PartesGatewayRunner`.

### D6.5.1 Partes Maquinas.Get (`000057`)

1 SP (`PAQ_PartesProduccion_MaquinasGet`). Misma familia `PartesCatalog` / `PartesGatewayRunner` (shape `MaquinaGet`).

### D6.5.2 Partes Maquinas.Create (`000058`)

1 SP (`PAQ_PartesProduccion_MaquinasCreate`) con `resultCode` (`OK` / `DUPLICATE_CODE`). Shape `MaquinaCreate`.

### D6.5.3 Partes Maquinas.Update (`000059`)

1 SP (`PAQ_PartesProduccion_MaquinasUpdate`) patch `nombre`/`activa`. Shape `MaquinaUpdate`.

### D6.5.4 Partes Maquinas.Delete (`000060`)

1 SP (`PAQ_PartesProduccion_MaquinasDelete`) borrado físico + `REFERENCED`. Shape `MaquinaDelete`.

### D6.5.5–8 Partes TiposTarea CRUD (`000061`–`000064`)

4 SP (`TiposTareaGet/Create/Update/Delete`). Payload `activo`. Shapes `TipoTarea*`.

### D6.5.9–12 Partes Operaciones CRUD (`000065`–`000068`)

4 SP (`OperacionesGet/Create/Update/Delete`). Payload `activa` (espejo Máquinas). Shapes `Operacion*`.

### D6.5.13–16 Partes Turnos CRUD (`000069`–`000072`)

4 SP (`TurnosGet/Create/Update/Delete`). Payload `activo` + `hora_inicio`/`hora_fin` (`HH:mm:ss` o null; `24:00`→`23:59:59` en fin). Update con `@SetHoraInicio`/`@SetHoraFin`. Errores: `INVALID_SCHEDULE`, `REFERENCED`. Shapes `Turno*`.

### D6.5.17–20 Partes ConceptosTiempo CRUD (`000073`–`000076`)

4 SP (`ConceptosTiempoGet/Create/Update/Delete`). Schema moderno `HABITUAL` + `ID_TIPO_TAREA` (FK TiposTarea). Create exige `id_tipo_tarea` activo; habitual único por tipo con `aviso_*` → runner `aviso_habitual`. Update patch + `@SetIdTipoTarea`. Delete precheck `PARTES_ENTRADAS`/`ASIGNACIONES_ITEMS`. Errores: `DUPLICATE_CODE`, `TIPO_TAREA_NOT_FOUND`, `HABITUAL_SIN_TIPO`, `REFERENCED`, `schemaIncompleto`. Shapes `ConceptoTiempo*`.

### D6.6.1 MovimientosTesoreria.Get (`000077`)

1 SP multi-RS (`PAQ_MovimientosTesoreria_Get`) — RS0 SBA04, RS1 SBA05, RS2 SBA27 (opcional). Catálogo: `MovimientosTesoreriaCatalog` + `MovimientosTesoreriaGatewayRunner`. `rowVersion` se calcula en runner (base64 `id|situacion|fechaUlt|horaUlt|nInterno`).

### D6.6.2 MovimientosTesoreria.Create (`000078` nota)

Alta orquestada en `MovimientosTesoreriaCreateRunner` (TX SBA04/SBA05 + saldos + asiento/cotización opcionales). Matriz clases 1–9 en C#. Sin SP monolítico. SP consolidado = futuro.

### D6.6.3 MovimientosTesoreria.Reversion (`000079` nota)

Reversión orquestada en `MovimientosTesoreriaReversionRunner` (TX: SBA04 REV + SBA05 invertidos D↔H + saldos + SBA27 + origen SITUACION=A + asiento opcional). Reusa helpers Create (`NextNComp`, asiento, `EncodeRowVersion`). Sin SP monolítico.

### D6.7.1 AsientosContables.Get (`000080`)

1 SP multi-RS (`PAQ_AsientosContables_Get`) — RS0 cabecera (+ tipo/moneda), RS1 renglones+importe, RS2 auxiliares, RS3 subauxiliares (vacíos si tablas ausentes). Catálogo: `AsientosContablesCatalog` + `AsientosContablesGatewayRunner`. `rowVersion` = base64 del `ROW_VERSION` TIMESTAMP (espejo `PedidosVentaRowVersion::encode`). Árbol `tiposAuxiliar` armado en runner.

### D6.7.2 AsientosContables.Create (`000081` nota)

Alta orquestada en `AsientosContablesCreateRunner` (TX cabecera→renglón→importe→auxiliar→subauxiliar; numeración; partida doble; SinAsignar). Sin SP monolítico. Reload vía Get + `creado=true`. SP consolidado = futuro.

### D6.7.3 AsientosContables.Update (`000082` nota)

PATCH orquestado en `AsientosContablesUpdateRunner` (rowVersion/Registrado/ejercicio; replace renglones opcional; fecha/moneda/nroAsiento opcionales). Reload vía Get (sin `creado`). Sin SP monolítico.

### D6.7.4 AsientosContables.Delete (`000083` nota)

DELETE orquestado en `AsientosContablesDeleteRunner` (rowVersion/Registrado/ejercicio; deleteHijos → cabecera). Success `{ nroInternoAnalitico, eliminado: true }`. Sin SP monolítico.

### D6.5.21–25 Partes maestros List (`000084`–`000088`)

5 SP (`MaquinasList` / `TiposTareaList` / `OperacionesList` / `TurnosList` / `ConceptosTiempoList`). Filtros opcionales + sort whitelist. Envelope listado `{items,page,page_size,total,total_pages}`. Conceptos: rama moderna con join TiposTarea si existen columnas `HABITUAL`/`ID_TIPO_TAREA`. Shapes `*List`.

### D6.8.1 OrdenesTrabajo.List (`000089`)

1 SP (`PAQ_PartesProduccion_OrdenesTrabajoList`) con `@Agrupado`. Paginación real (meta RS0 + items RS1). Agrupado: GROUP BY `CODIGO_OT`, MIN(id), MAX(estado)/HAVING. Por fila: join operaciones + artículo (`pq_vwarticulos`/`STA11`). Shape `OrdenTrabajoList` en `PartesCatalog` / `PartesGatewayRunner`.

### D6.8.2 OrdenesTrabajo.Get (`000090`)

1 SP (`PAQ_PartesProduccion_OrdenesTrabajoGet`) con `@IdOrdenTrabajo` **o** `@CodigoOt`. Filas detalle con join operación + artículo. Shape `OrdenTrabajoGet`: by id → item `formatItem`; by codigo → lote (`operaciones_incluidas`, estado MAX). Empty → NOT_FOUND (runner).

### D6.8.3 OrdenesTrabajo.Create (`000091` nota)

Alta orquestada en `OrdenesTrabajoCreateRunner` (TX multi-fila `PQ_PRD_ORDENES_TRABAJO` + numeración OT + validaciones artículo/operación/std). Sin SP monolítico. Shape espejo store host (`formatItem` + `items[]`). SP consolidado = futuro.

### D6.8.4 OrdenesTrabajo.Update (`000092` nota)

Update orquestado en `OrdenesTrabajoUpdateRunner`: por `id_orden_trabajo` (fila) o por `codigo_ot` (sync multi-op). Reutiliza helpers std/ops/artículo. Sin numeración. Shape espejo update / updateByCodigo host.

### D6.8.5 OrdenesTrabajo.Delete (`000093` nota)

Delete orquestado en `OrdenesTrabajoDeleteRunner`: baja por `id_orden_trabajo` (estado editable + vínculos). `usuario_codigo` EMP → FORBIDDEN. Shape vacío espejo destroy host.

### D6.8.6 OrdenesTrabajo.PatchEstado / CambioMasivoEstado (`000094` / `000095` nota)

- `OrdenesTrabajoPatchEstadoRunner`: `id_orden_trabajo` + `estado` (0|1|2) + reglas transición (Borrador/asignaciones). Success = formatItem.
- `OrdenesTrabajoCambioMasivoEstadoRunner`: `ids_json` + `operacion` (`cerrar`|`reabrir`). Success = `{actualizadas}`.

### D6.9.1 Asignaciones.List (`000096`)

1 SP (`PAQ_PartesProduccion_AsignacionesList`) con paginación real (meta RS0 + items RS1). Filtros fecha/turno/tipo_tarea/estado + sort whitelist. Join company turno/tipo_tarea; supervisor labels null (USERS host). Shape `AsignacionList`. VALIDATION si desde > hasta.

### D6.9.2 Asignaciones.Get (`000097`)

1 SP (`PAQ_PartesProduccion_AsignacionesGet`) con `@IdAsignacion`. Cabecera show host + `observaciones`; join turno/tipo_tarea; supervisor labels null. Empty → NOT_FOUND. Shape `AsignacionGet`.

### D6.9.3 Asignaciones.Create (`000098` nota)

Alta orquestada en `AsignacionesCreateRunner` (TX INSERT Draft + validaciones fecha/turno/tipo_tarea). Sin SP monolítico. Shape store host (sin observaciones). SP consolidado = futuro.

### D6.9.4 Asignaciones.Update (`000099` nota)

Update Draft orquestado en `AsignacionesUpdateRunner` (ESTADO=0, bloqueo cambio tipo_tarea con items). Shape update host (con observaciones).

### D6.9.5 Asignaciones.Publicar (`000100` nota)

Publicar orquestado en `AsignacionesPublicarRunner`: plan mínimo (items+operarios), congelar `UNIDADES_HORA_STD` desde STD vigente, ESTADO=1 + FECHA_PUBLICACION.

### D6.9.6 Asignaciones.Cerrar (`000101` nota)

Cerrar orquestado en `AsignacionesCerrarRunner` (ESTADO=1 → 2 + FECHA_CIERRE).

### D6.9.7 Asignaciones.Cancelar (`000102` nota)

Cancelar orquestado en `AsignacionesCancelarRunner` (Draft/Published → ESTADO=3). Siguiente libre: `000103` (Items).

### D6.10.1 PartesOperario.Get (`000112`)

1 SP (`PAQ_PartesProduccion_PartesOperarioGet`) con `@IdParteOperario` + `@UsuarioId`. Resuelve operario en `PQ_SUELD_LEGAJOS`; filtra dueño. Meta RS0 `no_legajo`/`not_found`. Shape `ParteOperarioGet`.

### D6.10.2 PartesOperario.Create (`000113` nota)

Alta orquestada en `PartesOperarioCreateRunner` (resolver operario, validar turno, unicidad fecha/turno/operario, INSERT ESTADO=0). Sin SP monolítico. Shape store host (`id`, `fecha_parte`, `id_turno`, `estado`).

### D6.10.3 PartesOperario.Update (`000114` nota)

Edición Open orquestada en `PartesOperarioUpdateRunner` (propietario + ESTADO=0, UPDATE observaciones). CONFLICT si no Open. Shape `id` + `observaciones`.

### D6.10.4 PartesOperario.Enviar (`000115` nota)

Open→Submitted orquestado en `PartesOperarioEnviarRunner` (circuito activo, propietario, entradas válidas). Shape `id` + `estado`.

### D6.10.5 PartesOperario.Aprobar (`000116` nota)

Submitted→Reviewed orquestado en `PartesOperarioAprobarRunner` (supervisor, sin filtro de propietario, FECHA_REVISION).

### D6.10.6 PartesOperario.Devolver (`000117` nota)

Submitted→Open orquestado en `PartesOperarioDevolverRunner` (limpia revisión).

### D6.10.7 PartesOperario.Cerrar (`000118` nota)

Reviewed→Locked orquestado en `PartesOperarioCerrarRunner`. Oleada A cabecera cerrada.

### D6.10.8 ParteContexto (`000119`)

1 SP (`PAQ_PartesProduccion_PartesOperarioParteContexto`) con `@FechaParte`, `@IdTurno` (nullable), `@UsuarioId`. Resuelve operario; match fecha/turno (sin turno = ID_TURNO null o 0). Vacío / sin tabla / sin legajo → `id_parte_operario` null. Shape `ParteOperarioParteContexto`.

### D6.10.9 MisPartes.List (`000120`)

1 SP multi-RS (`PAQ_PartesProduccion_PartesOperarioMisPartesList`). RS0 meta+verificación, RS1 cabeceras, RS2 `planificado_items` (asignaciones publicadas + carga libre). Sin `usuario_id` válido / sin tabla / sin legajo → envelope vacío `page_size` 0. Shape `ParteOperarioMisPartesList`.

### D6.10.10 PartesRevisar.List (`000121`)

1 SP (`PAQ_PartesProduccion_PartesOperarioPartesRevisarList`). Supervisor, **sin** `usuario_id`. Default estados Open+Submitted+Reviewed. Sin tabla → `items` vacío `page_size` 50. Shape `ParteOperarioPartesRevisarList`.

### D6.10.11 PartesRevisar.Get (`000122`)

1 SP (`PAQ_PartesProduccion_PartesOperarioPartesRevisarGet`). Supervisor, **sin** `usuario_id`. RS0 cabecera + RS1 `entradas[]`. Sin parte / sin tabla → `NOT_FOUND`. Sin tabla entradas → `entradas` vacío. Shape `ParteOperarioPartesRevisarGet`.

### D6.11.1 Entradas.List (`000123`)

1 SP (`PAQ_PartesProduccion_PartesOperarioEntradasList`) con `@IdParteOperario`, `@UsuarioId`. RS0 meta `no_legajo`/`not_found`; RS1 `items[]`. Dueño del parte. Sin tabla entradas → `items` vacío. Shape `ParteOperarioEntradasList`.

### D6.11.2 Entradas.Create (`000124` nota)

Alta orquestada en `PartesEntradasCreateRunner` (espejo PHP `storeLocal`). Parte Open + dueño. OT o ítem de asignación; máquina si existe catálogo.

### D6.11.3 Entradas.Update (`000125` nota)

Patch orquestado en `PartesEntradasUpdateRunner`. No toca `ID_ASIGNACION_ITEM` / `ORIGEN_CARGA`.

### D6.11.4 Entradas.Delete (`000126` nota)

Borrado orquestado en `PartesEntradasDeleteRunner`. Parte Open + dueño.

### D6.11.5 Entradas.Reclasificar (`000127` nota)

Reclasificación supervisor en `PartesEntradasReclasificarRunner`. Parte Reviewed + circuito activo; **sin** filtro de dueño. `notas_revision` obligatorio. Siguiente libre: `000128`.

### D6.1 PedidosVenta.Get (`000049`)

1 SP multi-RS (`PAQ_PedidosVenta_Get`). Catálogo: `PedidosVentaCatalog` + `PedidosVentaGatewayRunner`.

### D6.2 PedidosVenta.Create (`000050` nota)

Alta MVP orquestada en `PedidosVentaCreateRunner` (TX + crypto PROXIMO). Sin Delta6/ocasional. SP consolidado = futuro.

## Contratos

### Company SPs D1

Ver scripts 000001–000008 (clientes → comprobantes).

### Company SPs D2 / D3

Port desde agente legado.

### `tango.version`

- Param: `llave` (ej. `000205/012`)
- HKLM: `Axoft\Astor\{llave}\Client` (`SystemVersion`/`SystemDir`) luego `axoft\Tango2000\llaves\{llave}` (`ClientVersion`/`SystemDir`)
- Success: `{ version, systemDir? }`

## Conexión

`encrypt` / `trustServerCertificate` según SSMS. Detalle: [instalacion-agente.md](../../../docs/06-operacion/instalacion-agente.md) §3.1.
