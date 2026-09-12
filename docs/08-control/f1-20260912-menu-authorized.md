# Verificación del agente - menu.authorized (F3 Agente)

| Campo | Valor |
|-------|--------|
| Fecha | 2026-09-12 |
| Repo | PaqSuite-IA-AgenteCliente-PAQ |
| Alcance | Op `menu.authorized` + SP `PAQ_User_Menu_Authorized` + wiring whitelist |
| SPEC / plan | Plan 100% F3 · adopción GEN-33 en Tango/Framework |

## Resultado

- **Aprobado con observaciones**

## Evidencia revisada

- Código: `JobOperations.MenuAuthorized`, `MenuAuthorizedRunner`, `SqlMenuAuthorizedSpExecutor`, `AgentGatewayConnector`, script SQL dictionary.
- Tests unitarios: `MenuAuthorizedRunnerTests` — **4 passed** (2026-09-12).
- Smoke lab `lenovo`: `POST /api/v1/auth/login` → token `agw.*`; `GET /api/v1/user/menu` + `X-Company-Id=11` → **error=0, items=166**.
- UI: shell post-empresa con árbol de menú visible (tras fix contraste tema dark en Tango).

## Hallazgos críticos

- Ninguno para el alcance menú/authz.

## Advertencias

- `dotnet test` de la solución completa puede fallar el build de `PaqGateway` si el proceso está en ejecución (lock DLL); los tests del agente corren aislados.
- Ops de dominio (Acopios, `tango.version`, etc.) siguen fuera de whitelist — esperado hasta F4+/dominio.
- No versionar `appsettings.Development.json` local ni tokens reales.

## Tests

```text
dotnet test tests/PaqAgent.Tests/PaqAgent.Tests.csproj --filter "FullyQualifiedName~MenuAuthorized"
→ Correctas! Superado: 4
```

Smoke HTTP lab (artisan + gateway + agente up): login 0 / menu 166 ítems.

## Pendientes

- Humano: `Finalizado` de HU/TR GEN-33 en Framework (pedido explícito 2026-09-12).
- Siguiente fase plan: **F4** prefs/warmup (no menú).

## Recomendación final

Cerrar F3 lado Agente como hecho. Pasar a commit/push del tramo menú. No abrir F4 en este F1.
