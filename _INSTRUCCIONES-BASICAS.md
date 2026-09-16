# Cómo instalar agente y gateway

Ver `docs\06-operacion\README.md`

en Resumen : 
- Agente : `docs/06-operacion/instalacion-agente.md`
    El paso a paso para instalar, con obtención de token está en MANUAL-DEL-PROGRAMADOR.md (ver abajo), capítulo `## 4. Instalación de un cliente — orden de ejecución`
- Gateway 
    (corto) :`docs/06-operacion/deploy-gateway-aws.md`
    (exhaustivo) : `docs/06-operacion/deploy/instalacion-exhaustiva-paq-gateway-ia.md`

# Reinstalación Agente (manual)

1) Powershell como Administrador

cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ

# 1) Compilar el agente nuevo
dotnet publish src\PaqAgent\PaqAgent.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o artifacts\paqagent-update

# 2) Parar el servicio (si el exe queda trabado, matarlo)
Stop-Service PaqAgent -Force
Get-Process PaqAgent -ErrorAction SilentlyContinue | Stop-Process -Force

# 3) Copiar binarios SIN tocar la config
$dest = "C:\PaqSystems\PaqAgent"
Copy-Item artifacts\paqagent-update\* $dest -Recurse -Force -Exclude "appsettings.local.json"

# 4) Arrancar
Start-Service PaqAgent
Get-Service PaqAgent

2) Comprobación de que volvió a conectar

Get-Content C:\PaqSystems\PaqAgent\appsettings.local.json
Get-ChildItem C:\PaqSystems\PaqAgent\logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 40


# Manual del Programador

Ejecución scripts :  

`C:\Programacion\PaqSuite-IA-FRAMEWORK\packages\php\laravel-core\database\sp\pq_sp_grid_layout_core.sql`

`C:\Programacion\PaqSuite-IA-FRAMEWORK\packages\php\laravel-core\database\sp\pq_grid_layouts_unique_q5.sql`


# Ejecuciones SQL 

ver `docs\00-contexto\MANUAL-DEL-PROGRAMADOR.md`

# Cómo hacer pruebas smoke

1) Gateway
cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ
dotnet run --project src\PaqGateway --urls http://127.0.0.1:5100
Firefox: http://127.0.0.1:5100 → 404 OK (conecta).

2) Agente
Servicio/app del agente en marcha, con el mismo agent_id / token que la fila lenovo en 192.168.41.2 / PAQSYSTEMS.

3) BE Tango (preparacion)
.env con AGENT_GATEWAY_ENABLED=true, AGENT_GATEWAY_URL al gateway local 
(en tu máquina ya figura http://127.0.0.1:5100 + key), 
y la BD de tenants/empresas_conexion donde está lenovo con agent_id.

4) FE Tango (Preparación)
En frontend/.env:
VITE_API_URL=http://127.0.0.1:8000/api → npm run dev.
VITE_TENANT_LOCAL=lenovo

5) BE Tango (Ejecución)
cd C:\Programacion\PaqSuite-IA-TANGO\backend
php artisan serve --host=127.0.0.1 --port=8000
Probar: http://127.0.0.1:8000/api/v1/health → debe responder ya (si cuelga, no sigas al login).

6) FE Tango (ejecución)

- si ya estaba levantado y hay que bajarlo:
    Get-NetTCPConnection -LocalPort 3000 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }

cd C:\Programacion\PaqSuite-IA-TANGO\frontend
npm install            
npm run dev

(npm install solo si aún no instalaste deps.)

7) Login (ventana privada)
http://localhost:3000/?cliente=lenovo
Network: POST a http://127.0.0.1:8000/api/v1/auth/login
Terminal artisan: tiene que aparecer ese POST.

Si algo falla, pará en el primer paso que no cumpla (health / 5100 / POST en artisan) y lo vemos.