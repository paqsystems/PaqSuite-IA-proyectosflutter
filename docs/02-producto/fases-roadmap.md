# Fases del producto Agente + Gateway

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-04 |
| Estado | Vigente — mapa de alcance (no sustituye los SPEC) |
| Modo | AGENTE-GATEWAY |

Tres fases **secuenciales**. No mezclar alcance: primero el caño, después update del binario, después el dueño de los objetos SQL.

```text
Fase 1 — MVP caño          →  SPEC-AGW-001  (ahora)
Fase 2 — Update del agente →  parte de SPEC-AGW-002
Fase 3 — Objetos SQL       →  parte de SPEC-AGW-002 (decisión formal)
```

Análisis previo (no altera el MVP): [agente-gateway/plan-ciclo-sql-y-updates.md](agente-gateway/plan-ciclo-sql-y-updates.md).

---

## Fase 1 — MVP: caño completo (agente + gateway)

**SPEC:** [SPEC-AGW-001-producto.md](SPEC-AGW-001-producto.md)  
**HU/TR:** épica `001-Conectividad` (orden D10).

### Qué es “listo”

Un cliente en **modo agente**, sin Tailscale y sin IP obligatoria en `empresas_conexion`, puede:

1. Instalar/conectar el agente (lab manual → luego instalador).
2. Aparecer **online** en PaqSuite (heartbeat + TTL).
3. Ejecutar `diagnostics.run` y el piloto **`auth.login`** vía Gateway.
4. Si el agente cae → `AGENT_OFFLINE` **sin** fallback SQL por `host`.
5. Gateway en AWS (`gateway.paqsuite.com`) + docs de instalación.

### Entra

- PaqGateway + PaqAgent + instalador (MUST al cierre) + contrato Laravel en TANGO.
- Un SP piloto embebido OK; **no** migraciones masivas de esquema PQ.
- Descarga pública del instalador (releases) + SHA256.

### No entra (queda para fases 2 / 3)

- Auto-update / “el agente sabe que debe actualizarse”.
- Bootstrap masivo de tablas/seeds/SP de producto en SQL del cliente.
- Definir el ciclo completo “quién aplica DDL” (agente vs host).
- Resto de operaciones de negocio, N agentes, Redis, etc.

**Regla:** si el caño de Fase 1 no está verde, no se arranca Fase 2 ni 3.

---

## Fase 2 — Los agentes reconocen que deben actualizarse

**SPEC (a formalizar):** [SPEC-AGW-002](agente-gateway/SPEC-AGW-002-ciclo-sql-y-updates.md) — frente **C** del plan (nueva versión del binario).

### Qué es

Canal para que cada instalación sepa que hay una versión nueva del agente (y, en su momento, aplicarla).

### Incluye (dirección; detalle en el SPEC-002)

- Catálogo de versión deseada / mínima (publicado desde AWS; no SQL del cliente).
- El agente informa su versión (p. ej. en heartbeat — ganchos ya previstos en contratos).
- Señal clara: “hay update” (UI, log, status en PaqSuite).
- Más adelante: auto-update o flujo asistido de descarga/reinstalación (firma Authenticode = no MVP).

### No incluye

- Aplicar DDL/seeds masivos en SQL (eso es Fase 3).
- Reabrir el diseño del caño de Fase 1.

### Dependencia

Fase 1 aceptada. Releases públicas del instalador ya existen (D9).

---

## Fase 3 — Objetos SQL: ¿agente o host?

**SPEC (a formalizar):** mismo SPEC-AGW-002 — frente **A** del plan (bootstrap / update SQL PQ).

### La pregunta

En **modo agente**, Laravel en AWS **no tiene** connection string al SQL del cliente. Entonces:

> ¿Quién crea/actualiza tablas, seeds y stored procedures en el SQL del tenant: el **agente** (y/o su instalador) o el **host** Laravel (como en el camino legacy con SQL directo)?

### Dirección ya analizada (pendiente de cierre en SPEC)

| Camino | Quién aplica objetos SQL |
|--------|---------------------------|
| Modo agente (`agent_id`) | **En el cliente**: instalador y/o runner del agente. Forge **no** puede `migrate` remoto. |
| Legacy (sin `agent_id`) | Laravel/Forge puede seguir con SQL directo hasta la transformación (D5). |

Fase 3 **define y especifica** (HU/TR): mismo instalador en dos fases (conectividad vs esquema PQ), versión de esquema deseada, alta de empresa, etc. No se improvisa en runtime de Fase 1.

### Dependencia

Fase 1 verde. Conviene tener al menos el **reconocimiento de versión** (Fase 2) esbozado, porque el esquema PQ también versiona; pueden solaparse en el mismo SPEC-002 pero **no** en la misma entrega que el MVP.

---

## Resumen rápido

| Fase | Nombre corto | Pregunta que responde | Artefacto |
|------|--------------|------------------------|-----------|
| **1** | Caño | ¿Laravel habla con Tango sin VPN ni IP del cliente? | SPEC-AGW-001 |
| **2** | Update agente | ¿Cada instalación sabe / puede actualizar el binario? | SPEC-AGW-002 (frente C) |
| **3** | Objetos SQL | ¿Agente/instalador o host Laravel aplica el esquema PQ? | SPEC-AGW-002 (frente A) |

Trabajo Laravel del contrato del MVP: repo **PaqSuite-IA-TANGO**.  
Este repo: agente, gateway, instalador, docs.
