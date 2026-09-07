# Empaquetado release — PaqAgentSetup (D9)

Artefacto de cara al cliente: **un** exe autoejecutable. El administrador no descomprime nada.

| Campo | Valor |
|-------|--------|
| SPEC | [SPEC-AGW-001](../02-producto/SPEC-AGW-001-producto.md) §5.1 |
| Decisión | [D9](../02-producto/decisiones-tecnicas.md) |
| Porqué | [MANUAL-DEL-PROGRAMADOR](../00-contexto/MANUAL-DEL-PROGRAMADOR.md) § D9 |
| Canal cliente | Landing pública TANGO (URL exacta = Q-D9-1) |
| Asset | `PaqAgentSetup.exe` + `PaqAgentSetup.exe.sha256` |

**Prohibido entregar al cliente:** zip suelto, SFX 7-Zip/WinRAR, carpeta `agent/` suelta.

---

## Comando canónico

Desde la raíz del repo, Windows x64:

```powershell
.\scripts\pack-installer.ps1
```

Sale:

```text
artifacts/PaqAgentSetup.exe
artifacts/PaqAgentSetup.exe.sha256
```

El script:

1. Publica `PaqAgent` self-contained win-x64 (`PublishSingleFile`) a un staging.
2. Comprime **el contenido** de esa carpeta a `agent-payload.zip` (insumo de build; no se commitea).
3. Publica `PaqAgentInstaller` self-contained win-x64 con ese zip como **EmbeddedResource**.
4. Copia el exe a `artifacts/PaqAgentSetup.exe` y escribe el SHA256.

Subir esos dos archivos a Forge/S3 y apuntar la landing TANGO. **No** commitear el exe (pesa decenas/cientos de MB; `artifacts/` ya está en `.gitignore`).

### Lab (carpeta `agent/` al lado, sin payload)

Para F5 / `artifacts/installer-lab-*` sin reembebido:

```powershell
.\scripts\pack-installer.ps1 -LabLayout
```

Genera `PaqAgentInstaller.exe` + `agent/` adyacente. Sirve para desarrollo. **No** es el asset de cliente.

---

## Qué hay dentro del exe de cliente

| Pieza | Origen |
|-------|--------|
| Asistente WinForms | `src/PaqAgentInstaller` (SC, `PublishSingleFile`) |
| Payload `PaqAgentInstaller.agent-payload.zip` | Publish de `src/PaqAgent` (SC, `PublishSingleFile`) |

Al instalar, el wizard extrae el payload al directorio elegido (default `C:\PaqSystems\PaqAgent`). Si existe carpeta `agent/` junto al exe (lab), usa esa y no exige el recurso embebido.

Prerrequisito documentado: .NET 8 Desktop Runtime x64 (el instalador avisa si falta; el paquete es self-contained).

---

## Verificar SHA256 (cliente / ops)

```powershell
Get-FileHash .\PaqAgentSetup.exe -Algorithm SHA256
Get-Content .\PaqAgentSetup.exe.sha256
```

Comparar con el hash publicado en la landing TANGO (D9). No depender de un `latest` de GitHub.

---

## Publicación en TANGO (no se codea aquí)

Cuando Q-D9-1 cierre la URL:

1. Copiar `PaqAgentSetup.exe` y `.sha256` al disco público (Forge `storage` / S3).
2. Landing `https://<host-tango>/descargas/agente`: versión, fecha, SHA256, botón, enlace al instructivo.
3. Directo: `https://<host-tango>/descargas/agente/PaqAgentSetup.exe`.

GitHub Releases, si se usa, es **espejo interno de CI**. No es la URL del administrador Tango.
