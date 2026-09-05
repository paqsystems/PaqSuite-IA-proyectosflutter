# SQL diccionario — piloto `auth.login`

Scripts reutilizados del agente legado (`paqsuite-IA-AgenteCliente`) para el SP `dbo.PAQ_Auth_Login`.

## Qué base es esta (y qué no es)

| Sí | No |
|----|-----|
| SQL Server del **cliente**: base **diccionario** Tango (`USERS`, `pq_empresa`, …) | Tabla Laravel `empresas_conexion` |
| Host/IP del SQL del lab (ej. `192.168.41.2`) o `localhost` | Obligatoriedad de una IP concreta del SPEC |

El nombre de base va en `appsettings.local.json` → `sql.database` (ej. `diccionario_000205_012`).

## Orden de aplicación

En SSMS, conectado a **esa** base diccionario:

1. `2026_06_23_000000_b_ensure_users_columns.sql`
2. `2026_06_24_000001_create_paq_auth_login.sql`
3. `2026_06_29_000002_fix_col_rol_pk_fallback.sql`

## Contrato del SP

- Parámetro: `@Codigo` = código Tango (`USERS.codigo`), **no** el login SQL (`sql.user`).
- 2 result sets: header + empresas.
- El SP **no** recibe password; Laravel hace `Hash::check` sobre `password_hash`.

## Conexión desde el agente vs SSMS

Si SSMS entra con cifrado **Opcional** y el agente falla en handshake SSL (`SQL_UNREACHABLE`), en `appsettings.local.json`:

```json
"encrypt": false,
"trustServerCertificate": true
```

Reiniciar PaqAgent después de cambiar el JSON.
