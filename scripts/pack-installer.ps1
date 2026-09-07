#Requires -Version 5.1
param(
    [switch]$LabLayout
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
Set-Location $repoRoot

$artifacts = Join-Path $repoRoot "artifacts"
$staging = Join-Path $artifacts "pack-staging"
$agentOut = Join-Path $staging "agent"
$payloadZip = Join-Path $staging "agent-payload.zip"
$installerOut = Join-Path $staging "installer"

if (Test-Path $staging) {
    Remove-Item -Recurse -Force $staging
}
New-Item -ItemType Directory -Path $agentOut | Out-Null

dotnet publish src/PaqAgent/PaqAgent.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o $agentOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish PaqAgent failed."
}

if ($LabLayout) {
    $lab = Join-Path $artifacts "installer-lab"
    if (Test-Path $lab) {
        Remove-Item -Recurse -Force $lab
    }
    New-Item -ItemType Directory -Path (Join-Path $lab "agent") | Out-Null
    Copy-Item -Path (Join-Path $agentOut "*") -Destination (Join-Path $lab "agent") -Recurse -Force
    dotnet publish src/PaqAgentInstaller/PaqAgentInstaller.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -o $lab
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish PaqAgentInstaller (lab) failed."
    }
    Write-Host "Lab layout: $lab (exe + carpeta agent/). No es el asset de cliente."
    return
}

if (Test-Path $payloadZip) {
    Remove-Item -Force $payloadZip
}
Compress-Archive -Path (Join-Path $agentOut "*") -DestinationPath $payloadZip -Force

New-Item -ItemType Directory -Path $installerOut | Out-Null
$payloadZipFull = [System.IO.Path]::GetFullPath($payloadZip)
dotnet publish src/PaqAgentInstaller/PaqAgentInstaller.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:AgentPayloadZip=$payloadZipFull `
    -o $installerOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish PaqAgentInstaller failed."
}

$srcExe = Join-Path $installerOut "PaqAgentInstaller.exe"
$destExe = Join-Path $artifacts "PaqAgentSetup.exe"
Copy-Item -Path $srcExe -Destination $destExe -Force
$hash = (Get-FileHash -Path $destExe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $artifacts "PaqAgentSetup.exe.sha256") -Value "$hash  PaqAgentSetup.exe" -Encoding ascii
Write-Host "PaqAgentSetup.exe"
Write-Host "SHA256 $hash"
Write-Host $destExe
