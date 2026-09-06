# Empaquetado release — PaqAgentInstaller (TR-004)

Desde la raíz del repo (Windows x64):

```powershell
$dist = "artifacts/installer"
Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "$dist/agent" | Out-Null

dotnet publish src/PaqAgent/PaqAgent.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o "$dist/agent"

dotnet publish src/PaqAgentInstaller/PaqAgentInstaller.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o "$dist"

Compress-Archive -Path "$dist\*" -DestinationPath "artifacts/PaqAgentInstaller-win-x64.zip" -Force
Get-FileHash "artifacts/PaqAgentInstaller-win-x64.zip" -Algorithm SHA256
```

El zip debe contener `PaqAgentInstaller.exe` y la carpeta `agent/` con `PaqAgent.exe`.

Prerrequisito documentado: .NET 8 Desktop Runtime x64 (el instalador avisa si falta; el paquete es self-contained).
