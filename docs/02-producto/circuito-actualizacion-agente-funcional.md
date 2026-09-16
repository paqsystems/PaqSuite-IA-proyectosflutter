# Cómo se actualiza el agente — explicación para funcionales

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-13 |
| Audiencia | Producto, operaciones PaqSystems, quien coordina Tango / Framework / Agente |
| Estado | Acuerdo de dirección (aún no hay SPEC formal ni código de update) |
| Insumo para SPEC | [agente-gateway/insumo-spec-agw-002-update-agente.md](agente-gateway/insumo-spec-agw-002-update-agente.md) |
| Objetos SQL (mismo exe / misma oleada) | [circuito-objetos-sql-agente-funcional.md](circuito-objetos-sql-agente-funcional.md) |

Este documento cuenta **cómo funcionaría el circuito** cuando PaqSuite suma o cambia APIs que el agente tiene que entender. No es un instructivo de instalación ni un SPEC.

---

## 1. El problema, en una frase

PaqSuite vive en Amazon. El agente es un programa en el **servidor Windows de cada cliente**, al lado de Tango. Cada vez que el producto necesita una operación nueva (buscar clientes, stock, una grilla…), ese programa del cliente tiene que saber hacerla. No podemos entrar nosotros por SQL ni pedir que el cliente clone código. Hay que **hacerle llegar una versión nueva del agente**, de forma controlada, sin perder la configuración que ya cargó el administrador del servidor.

Hoy, con uno o dos pilotos, eso se hace a mano (soporte detiene el servicio, copia archivos, lo vuelve a arrancar). Eso no escala cuando hay muchos clientes en modo agente.

---

## 2. Tres ideas que hay que tener claras

### No se actualiza el agente por cada pantalla

Las APIs nuevas entran en **oleadas**: un paquete con nombre (por ejemplo “maestros D1”, “grillas F5”), no un envío cada vez que Tango o Framework termina una pantalla. PaqSystems publica una oleada cuando esa familia está lista para los clientes con agente.

### El producto pregunta qué sabe hacer este agente

Antes de pedirle un trabajo, PaqSuite mira si **ese** agente ya trae esa capacidad. Si todavía no (sigue en una oleada anterior), el usuario no ve un error técnico opaco: el sistema sabe que falta actualizar el agente de ese cliente. Llamamos a esto **handshake de capacidades**.

### Un solo instalador, para el alta y para el update

El archivo que baja el administrador la primera vez (`PaqAgentSetup.exe`) es **el mismo** que usa el agente cuando PaqSystems dispara una actualización. El programa reconoce si ya está instalado:

- **No está** → asistente: pide datos, prueba conexión, aplica el paquete SQL de **instalación**, deja el servicio andando.
- **Ya está** → actualiza el programa **sin** volver a pedir usuario, token ni datos de SQL, **sin** borrar la configuración local, y aplica el paquete SQL de **actualización**.

La página de descarga es pública (sin login y sin token en el enlace). El token se carga en el asistente, una sola vez, cuando se da de alta el cliente.

El detalle de tablas, seeds y SP (MULTI/MONO) está en [circuito-objetos-sql-agente-funcional.md](circuito-objetos-sql-agente-funcional.md).

---

## 3. Quién interviene (y quién no)

| Quién | Qué hace en este circuito |
|-------|---------------------------|
| **Operador PaqSystems** | Publica la oleada, confirma el envío, mira quién actualizó y quién no. Usa una pantalla **staff en Tango** (no está en el menú de los usuarios) o un comando desde Forge. |
| **Administrador del servidor del cliente** | Solo en la **primera instalación**, o si hace falta la “oleada 0” de soporte. En las actualizaciones siguientes no tiene que sentarse frente al wizard. |
| **Usuario de PaqSuite** | Usa la app. No ve esta pantalla. No “actualiza el agente” él. |
| **Tango (Laravel)** | Guarda qué versión queremos en cada cliente, muestra la consola staff, avisa al agente a través del Gateway. |
| **Agente** | Dice qué versión es y qué oleada/capacidades tiene. Cuando le piden actualizar, baja el instalador, comprueba que sea el archivo correcto, se actualiza y vuelve a avisarnos. |
| **Gateway** | Solo transporta el aviso. No decide versiones ni conoce Tango. |

Los usuarios de un tenant **no** tienen esta función en el menú. Es exclusiva de PaqSystems.

---

## 4. Cómo queda un cliente la primera vez (instalación nueva)

1. PaqSystems da de alta el cliente en Tango (identidad del agente: identificador y token).
2. El administrador del servidor entra a la **página pública de descarga** (el mismo árbol donde después vivirán las actualizaciones), baja `PaqAgentSetup.exe`, verifica la huella del archivo (SHA256) y lo ejecuta como administrador.
3. El asistente pide los datos de PaqSystems y los de SQL local, prueba que el Gateway responda, instala el servicio y lo deja corriendo.
4. En Tango el cliente aparece **en línea**. A partir de ahí, Laravel no habla con el SQL del cliente por IP: todo pasa por el agente.

Esa página de descarga es la de siempre (decisión D9). Falta fijar el host exacto; el diseño no cambia.

---

## 5. Cómo sale una oleada nueva (el circuito completo)

Piense en cuatro momentos. El Deploy cotidiano de Tango (un arreglo de PHP, un menú) **no** dispara esto. Solo cuando PaqSystems decide “esta oleada del agente sale”.

### Momento A — Hay algo que el agente tiene que aprender

Framework y Tango avanzan en paralelo. Cuando una familia de operaciones está lista para modo agente, se arma **un** instalador nuevo, con número de versión (por ejemplo 1.4.0) y la lista de cosas que esa oleada sabe hacer.

Eso no se publica “de a una API”. Se publica el paquete.

### Momento B — El archivo queda disponible para bajar

En la misma página de descarga de siempre se publica:

- el instalador;
- su huella SHA256;
- un manifiesto corto: “la versión vigente es X, el archivo está acá, la huella es esta”.

Hasta que eso no está, **nadie** avisa a los agentes. Si avisáramos antes, bajarían un archivo viejo o inexistente.

La instalación nueva y la actualización usan **ese mismo árbol**. Un humano ve la página; el agente usa el manifiesto y el archivo.

### Momento C — PaqSystems confirma el envío

Hay dos formas de apretar el botón. Las dos hacen lo mismo por detrás.

**Pantalla staff en Tango** (acceso manual, interactivo):

- No figura en el menú de la aplicación para usuarios.
- Solo la usan operadores PaqSystems.
- Muestra: versión publicada, cada cliente con agente, si está en línea, qué versión tiene, qué oleada declara.
- La regla de producto es avisar a **todos** los clientes que ya tienen agente.
- En esta pantalla se puede **filtrar por cliente** (por ejemplo solo el piloto) antes de confirmar. Sirve para no disparar producción en el primer intento.
- Hay que **confirmar** de forma explícita: “aplicar la versión X a estos N agentes”.

**Forge** (el otro disparador):

- Desde el panel donde PaqSystems publica Tango se puede lanzar un comando: “avisá a todos, versión X, lo confirmo”.
- Forge **siempre** avisa a todos los que tienen agente (broadcast total). No es el lugar para filtrar un cliente: eso queda en la pantalla.
- Ese comando **no** corre en cada Deploy de Tango. Un cambio de PHP no debe reescribir el Windows de todos los clientes. Solo corre cuando un operador lo lanza, o —si en un release puntual Tango **exige** una oleada nueva de agente— cuando ese deploy declara esa oleada a propósito.

### Momento D — Cada agente se actualiza

Para cada cliente que tiene agente:

1. Tango anota “la versión que queremos es X”.
2. Si el agente está **en línea**, se le avisa ahora: “actualizate a X, el archivo está en esta URL, la huella es esta”.
3. El agente baja el manifiesto, comprueba que sea la X pedida, baja el exe, comprueba la huella, ejecuta el **mismo** instalador en modo actualización (sin pantallas: el servicio no puede mostrar un asistente).
4. El instalador reemplaza el programa, **conserva** la configuración local (identidad y SQL) y aplica el paquete SQL de esa oleada (modo update).
5. El servicio vuelve a arrancar y avisa: “ahora soy la versión X, este es el esquema, y estas son mis capacidades”.

Si el agente está **apagado o sin internet**, no se pierde el pedido. Queda anotado “queremos X”. Cuando vuelva a conectarse, se entera solo y se actualiza, **sin** que PaqSystems tenga que volver a apretar el botón para ese cliente.

La pantalla staff sirve de tablero: actualizó / sigue pendiente (estaba apagado) / falló. Un fallo no cancela el resto del lote.

---

## 6. Qué nota el usuario de PaqSuite

En el día a día, nada: sigue trabajando.

Si su cliente todavía no tiene la oleada que una pantalla necesita, PaqSuite **no debería** mandar ese trabajo al agente viejo y mostrar un error críptico. Debería poder decir, en lenguaje de producto, que ese cliente no tiene aún esa capacidad (falta la oleada). Eso es el handshake: el agente declara lo que sabe; Tango no pide lo que no está.

Cuando la oleada se aplica, esas pantallas pasan a funcionar en modo agente sin que el usuario instale nada.

---

## 7. La “oleada 0”: el primer empujón, a mano

El aviso automático (“actualizate”) solo lo entiende un agente que **ya** fue construido para entenderlo.

Los agentes que hoy están en un piloto **no** tienen ese comportamiento. La primera vez que hay que dejarlos en una versión que sí sabe actualizarse sola, es **procedimiento de soporte**: alguien con acceso al servidor (o el administrador del cliente) corre el instalador nuevo, o se reemplazan los archivos sin borrar la configuración.

A partir de esa versión, las oleadas siguientes salen por la pantalla staff o por Forge.

Oleada 0 no es un botón de producto. Es el puente de una vez.

---

## 8. Qué no entra en este texto (sí en el hermano SQL)

Este relato cubre el **programa**. Los objetos SQL (tablas, seeds, SP) van en el **mismo exe y la misma oleada**; el detalle MULTI/MONO, install vs update y alta de empresa está en [circuito-objetos-sql-agente-funcional.md](circuito-objetos-sql-agente-funcional.md). No es un segundo producto ni un migrate desde Forge.

Sigue fuera de ambos circuitos:

- **Usuarios de PaqSuite** actualizando el agente.
- **Cada Deploy de Tango** actualizando agentes (salvo opt-in de oleada).
- **GitHub** como página que se le manda al cliente (puede seguir siendo archivo interno de PaqSystems).
- Pedir de nuevo el token o la clave de SQL en cada update.

---

## 9. Receta operativa (para tener en la cabeza)

```text
1. Cerrar una oleada de APIs (no una pantalla suelta) — handlers + scripts SQL de esa familia.
2. Armar UN instalador de esa versión (programa + paquete SQL).
3. Publicarlo en la página de descarga (archivo + huella + manifiesto).
4. Confirmar el envío:
      - pantalla staff (todos, o un cliente si se filtra), o
      - comando Forge (siempre todos).
5. Los que están en línea se actualizan ahora (binario y esquema); los otros, al volver.
6. Tango ya no manda a un agente trabajos de oleadas que ese agente no declara (capacidades + esquema listo).
```

Instalación nueva: el administrador usa el **paso 3** (la misma página), no el paso 4.

---

## 10. Preguntas que este texto no cierra (a propósito)

Siguen abiertas a propósito, porque no cambian el circuito:

- El **host exacto** de la página de descarga.
- El nombre exacto de la ruta staff en Tango.
- Si el agente declara una lista de operaciones o un nombre de oleada (o ambos). Eso lo baja el SPEC.

Lo que sí está acordado: staff en Tango; broadcast a todos como regla, con filtro por cliente en la pantalla; misma página de descarga para alta y update; mismo instalador (programa **y** paquete SQL); Forge como disparador aparte del Deploy diario; oleada 0 como soporte. Detalle SQL: [circuito-objetos-sql-agente-funcional.md](circuito-objetos-sql-agente-funcional.md).
