# Cómo se instalan y actualizan los objetos SQL — explicación para funcionales

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-13 |
| Audiencia | Producto, operaciones PaqSystems, quien coordina Tango / Framework / Agente |
| Estado | Acuerdo de dirección (aún no hay SPEC formal ni código del runner SQL) |
| Insumo para SPEC | [agente-gateway/insumo-spec-agw-002-objetos-sql.md](agente-gateway/insumo-spec-agw-002-objetos-sql.md) |
| Circuito hermano (binario) | [circuito-actualizacion-agente-funcional.md](circuito-actualizacion-agente-funcional.md) |
| Norma Framework | GEN-18 (`SPEC-001-18`) y GEN-24 (matriz de módulos) — se **alinean**, no se copian ciegos |

Este documento cuenta **cómo viajan las tablas, seeds y stored procedures** hasta el SQL del cliente cuando el producto está en **modo agente**. Complementa el circuito del instalador: el mismo `PaqAgentSetup.exe` actualiza el programa **y** aplica el paquete SQL. No es un instructivo de SSMS ni un SPEC.

---

## 1. El problema, en una frase

En modo agente Laravel **no tiene** connection string al SQL del cliente. Forge **no puede** hacer `migrate` ni `CREATE PROCEDURE` sobre esa base. Si una oleada nueva trae APIs (`clientes.buscar`, una grilla, un informe), el Windows del cliente tiene que recibir **el programa y los objetos SQL**. Hoy los SP `PAQ_*` se aplican a mano (SSMS). Eso no escala y deja el login o las pantallas rotos aunque el servicio esté en línea.

---

## 2. Tres ideas que hay que tener claras

### El instalador trae dos cargas, no dos productos

`PaqAgentSetup.exe` es **uno**. Adentro viajan:

1. el programa (servicio Windows);
2. el **paquete SQL** de esa oleada (scripts de diccionario, de empresa, seeds de Framework y de producto).

La primera vez aplica el paquete en modo **instalación**. Las oleadas siguientes lo aplican en modo **actualización**. No hay un segundo exe “de base de datos”.

### Instalación y actualización no son el mismo script

Igual que GEN-18:

| | Primera vez | Después (oleada / update de host) |
|--|-------------|-----------------------------------|
| Usuarios `admin` y `PQ`, rol Supervisor, permisos iniciales | **Sí** (si no existen) | **No** se vuelven a crear ni se pisan contraseñas |
| Empresa inicial | **Una** (MULTI y MONO) | No se recrea |
| Tablas, menús, parámetros, SP, grillas, excels, pivots, tareas, … | Insertar / generar | Insertar o actualizar lo que cambió |
| Valores de parámetros que el cliente ya cargó | Defaults de fábrica | **No** se pisan (`valor_*` intactos; sí pueden cambiar caption/tooltip) |

### Diccionario y empresa no son lo mismo (salvo MONO)

- **Diccionario:** seguridad, `pq_menus`, usuarios, rol, `pq_empresa`, catálogo de menús/params, SP de plataforma, etc.
- **Empresa:** tablas y SP de la regla de negocio, params de esa empresa, componentes que viven en la BD operativa.

En **MONO** suele ser la misma base (`dictionary_database` = `database_name`): se aplica todo ahí. En **MULTI** (Tango) hay un diccionario y **N** bases de empresa en `PQ_EMPRESA`.

---

## 3. Quién interviene (y quién no)

| Quién | Qué hace con los objetos SQL |
|-------|------------------------------|
| **Operador PaqSystems** | Publica la oleada (el exe ya lleva el paquete SQL). Confirma el envío como en el circuito del binario. No entra por SSMS al cliente. |
| **Administrador del servidor del cliente** | Solo en la **primera instalación**: el wizard usa las credenciales SQL que carga. En las oleadas siguientes no vuelve a pedirlas. |
| **Usuario de PaqSuite** | No aplica scripts. Si falta esquema, ve un error de producto (“falta actualizar el agente / el esquema”), no un error de SQL. |
| **Tango (Laravel)** | En modo agente **no** migra el SQL del tenant. Publica `desiredSchemaVersion` junto con la oleada. En alta de empresa manda un trabajo al agente. En clientes **sin** agente sigue GEN-18 (Artisan install/update contra SQL directo). |
| **Agente / instalador** | Aplica el paquete **en el Windows del cliente**, con la conexión local ya guardada. |
| **Gateway** | Solo transporta el aviso o el job de alta de empresa. No interpreta T-SQL. |
| **Forge** | No hace `migrate` al SQL del cliente modo agente. El comando de oleada es el mismo del binario. |

---

## 4. Qué objetos SQL hay que considerar

Familias (GEN-18 §8–§9 + pedido de producto). El SPEC baja el catálogo exacto; acá no se inventa DDL fila a fila.

### 4.1 Primera instalación (cliente nuevo)

En **diccionario** (y, en MONO, en la única BD):

1. Tablas de seguridad, `pq_menus`, `pq_parametros_gral`.
2. Tablas del Framework: excels, grillas, reportes, tableros, tareas programadas, y el resto de componentes GEN.
3. Usuarios **admin** y **PQ** (passwords seed de GEN-18; sin firstLogin obligatorio).
4. Rol **Supervisor**.
5. **Una empresa** en `PQ_EMPRESA` (MULTI y MONO), con el nombre de la instalación.
6. Permiso: esa empresa + rol Supervisor + ambos usuarios.
7. Insertar en diccionario: stored procedures, registros `pq_menus`, registros `pq_parametros_gral` (Framework completo + producto / módulos contratados).

En la **BD de esa empresa inicial** (MULTI; en MONO es la misma BD): tablas PQ de empresa, SP de negocio, params de empresa, componentes que correspondan.

Idempotencia de esta pasada: **fail-if-exists** si ya hay instalación usable (criterio GEN-18). No es el camino de “actualizar”.

### 4.2 Actualización (oleada / nueva versión del host)

No re-seed de users ni de la empresa inicial.

- Update/generar tablas de seguridad, `pq_menus`, `pq_parametros_gral`.
- Update/generar tablas del Framework (excels, grillas, reportes, tableros, tareas, …).
- Diccionario: update/insert de stored procedures, registros `pq_menus`, registros `pq_parametros_gral`.
- Cada empresa **habilitada** en `PQ_EMPRESA`: lo mismo del lado empresa (tablas, SP, params solo meta).

---

## 5. MULTI vs MONO — cuándo se toca qué

### MULTI (Tango / `tenancy=multi`, bases partidas)

| Momento | Diccionario | Empresas |
|---------|-------------|----------|
| **Instalar un cliente nuevo** | Sí: tablas PQ, seguridad, seeds, menús, params, SP | **Una** empresa inicial en `PQ_EMPRESA` (y su BD) |
| **Actualizar versiones** (oleada del agente / update de host) | Sí: update/insert; **no** re-seed users | **Todas las empresas habilitadas** en `PQ_EMPRESA`, en secuencia |
| **Dar de alta una empresa** en el host | Sí: lo que esa empresa necesite en diccionario (fila `pq_empresa` ya creada por el ABM; no re-seed de users/rol) | **Solo esa** empresa (paquete “empresa nueva”) |

El resto de empresas MULTI **no** nace en el instalador: nace en el ABM Empresas. El ABM no puede migrar SQL desde AWS: dispara un trabajo al agente (`schema.seedEmpresa` / contrato GEN-18 `seedEmpresaNueva`).

### MONO (`tenancy=single`; una BD típica)

| Momento | Qué ocurre |
|---------|------------|
| **Instalar un cliente nuevo** | Insertar en esa base **todo**: seguridad y configuración (grillas, excels, pivots, tareas, …) **y** la regla de negocio |
| **Actualizar el host** | Insert/update de elementos generales y de negocio en esa misma base |
| **Alta de empresas** | **No hay.** La empresa seed es editable y no eliminable (GEN-19) |

---

## 6. Cómo se engancha en el circuito del instalador

Piense en los mismos cuatro momentos del [circuito del binario](circuito-actualizacion-agente-funcional.md). El SQL **no** es un quinto circuito: es la otra mitad de la oleada.

### Momento A — Hay algo que el agente tiene que aprender

Framework y Tango cierran una familia (plataforma o dominio). La oleada no es solo handlers C#: incluye los scripts SQL de esa familia (y el acumulado idempotente de oleadas anteriores). Un número de **versión de esquema** viaja junto al `agentVersion` del exe.

### Momento B — El archivo queda disponible para bajar

El mismo árbol de descarga, el mismo exe, el mismo SHA256. El manifiesto de la oleada declara también la versión de esquema que ese archivo sabe aplicar. Hasta que eso no está, **nadie** avisa a los agentes.

### Momento C — PaqSystems confirma el envío

La misma pantalla staff / el mismo comando Forge. Confirmar “versión X” significa: **binario X y esquema X**. No hay un botón aparte “solo SQL” en v1 (salvo el trabajo de alta de empresa, que no es oleada).

### Momento D — Cada agente se actualiza

Para cada cliente con agente, después de reemplazar el programa **sin** pisar la configuración local:

1. El runner aplica el paquete en modo **update** (diccionario fail-fast; empresas habilitadas en secuencia).
2. Si el esquema queda bien, el heartbeat dice: versión del programa, versión de esquema, capacidades, *listo*.
3. Si el SQL falla a mitad, el servicio puede estar en línea pero **no listo de esquema**. Tango no le manda trabajos de esa oleada. Un fallo en un cliente no cancela el lote.

**Instalación nueva:** el administrador usa la misma página de descarga. El wizard, **después** de probar SQL y Gateway, aplica el paquete en modo **install** (diccionario + una empresa) y recién ahí deja el servicio andando.

**Oleada 0** (agentes viejos): el procedimiento de soporte que ya está acordado para el binario incluye, a partir de esta dirección, correr el instalador nuevo para que también aplique SQL pendiente. Sigue sin ser un botón de producto.

---

## 7. Alta de empresa (solo MULTI) — no es una oleada

1. El usuario (o ops) da de alta la empresa en PaqSuite. La fila en `pq_empresa` la crea el ABM.
2. La BD física de esa empresa **ya existe** (ops / ERP). El agente **no** crea bases.
3. Tango, en modo agente, manda al agente el trabajo de sembrar **esa** empresa.
4. El agente aplica el paquete empresa (idempotente: se puede reintentar). No recrea `admin`/`PQ` ni el rol.
5. Si falla, el ABM no deja una empresa a medias (rollback de la fila, criterio GEN-18 / GEN-06).

En clientes **sin** agente, el host sigue pudiendo sembrar por SQL directo (GEN-18).

---

## 8. Qué nota el usuario de PaqSuite

En el día a día, nada.

Si el binario ya entiende una operación pero el esquema de ese cliente todavía no, o al revés, PaqSuite **no** debería mandar el trabajo y mostrar un error de SQL. Handshake: el agente declara programa **y** esquema; Tango no pide lo que no está listo.

Cuando la oleada se aplica (programa + SQL), esas pantallas pasan a funcionar en modo agente sin que el usuario instale nada.

---

## 9. Qué no cambia respecto de Framework (GEN-18 / GEN-24)

Se **reutiliza** la norma:

- Vocabulario instalación vs actualización (par cliente + producto).
- Framework **siempre completo**; varían módulos de producto.
- MULTI: una empresa al instalar; el resto por ABM + `seedEmpresaNueva`.
- Update: no re-seed de users; params solo caption/tooltip.
- Diccionario fail-fast; empresas en secuencia; abortar y reportar la que falló.
- Matriz de módulos en PAQSYSTEMS **dev** → seeders (GEN-24). En modo agente esos seeders **entran al exe** de la oleada; no se editan en el SQL del cliente ni hay ABM de matriz en producción.
- SP de un módulo **pueden** instalarse aunque el módulo no esté contratado; menús/params seedados siguen el contrato; OpenAPI no publica lo no contratado.

Lo que **cambia** (el hueco que GEN-18 deja al gateway): **quién ejecuta**. En modo agente ejecuta el instalador/agente en el cliente, no Laravel/Forge.

Para **Tango en modo agente** la dirección de producto vigente es **producto completo** (plan de integración). La matriz GEN-24 sigue existiendo para otros hosts y para recortes comerciales futuros; no se inventa acá un instalador distinto por cliente.

---

## 10. Receta operativa (para tener en la cabeza)

```text
1. Cerrar una oleada de APIs (handlers + scripts SQL de esa familia).
2. Armar UN instalador: programa + paquete SQL, misma versión / mismo esquema.
3. Publicarlo (archivo + huella + manifiesto con versión de programa y de esquema).
4. Confirmar el envío (staff o Forge) — igual que el circuito del binario.
5. Cada agente: reemplaza el programa, aplica SQL (install o update según el caso), avisa heartbeat.
6. Alta de empresa MULTI: job al agente; no oleada.
7. Tango no manda trabajos si faltan capacidades o el esquema no está listo.
```

Instalación nueva: el administrador usa el **paso 3**. El wizard aplica SQL en modo install.

Clientes **sin** `agent_id`: el circuito GEN-18 del host (Artisan) sigue; este texto no los obliga a pasar por el exe.

---

## 11. Preguntas que este texto no cierra (a propósito)

- Nombre de columnas de “empresa habilitada” en `PQ_EMPRESA`.
- Si el usuario SQL del wizard es el mismo que el de runtime (DDL vs solo `EXEC`).
- Si un update a mitad de N empresas deja capacidades de la oleada anterior o ninguna nueva hasta `schema_ready`.
- Overlay de matriz GEN-24 por cliente cuando Tango **no** sea producto completo.
- Host exacto de la landing (sigue Q-D9-1).

Lo que sí está acordado: mismo exe y misma oleada para programa y SQL; Laravel no migra el tenant modo agente; MULTI vs MONO como arriba; install ≠ update; alta de empresa = job, no wizard; handshake de esquema además del de capacidades.
