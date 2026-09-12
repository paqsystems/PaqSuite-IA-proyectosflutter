# Cómo instalar agente y gateway

Ver `docs\06-operacion\README.md`

en Resumen : 
- Agente : `docs/06-operacion/instalacion-agente.md`
    El paso a paso para instalar, con obtención de token está en MANUAL-DEL-PROGRAMADOR.md (ver abajo), capítulo `## 4. Instalación de un cliente — orden de ejecución`
- Gateway 
    (corto) :`docs/06-operacion/deploy-gateway-aws.md`
    (exhaustivo) : `docs/06-operacion/deploy/instalacion-exhaustiva-paq-gateway-ia.md`

# Manual del Programador

ver `docs\00-contexto\MANUAL-DEL-PROGRAMADOR.md`

# Cómo hacer pruebas smoke

## BE (backend): 
.env con AGENT_GATEWAY_ENABLED=true, AGENT_GATEWAY_URL al gateway local (en tu máquina ya figura http://127.0.0.1:5100 + key), y la BD de tenants/empresas_conexion donde está lenovo con agent_id.

## FE (frontend):
VITE_API_URL=http://localhost:8000/api → npm run dev.
VITE_TENANT_LOCAL=lenovo

## Levantar el BE

php artisan serve (o el puerto que uses; el FE espera :8000).

## levantar el FE

- si ya estaba levantado y hay que bajarlo:
    Get-NetTCPConnection -LocalPort 3000 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }

cd C:\Programacion\PaqSuite-IA-TANGO\frontend
npm install            
npm run dev

(npm install solo si aún no instalaste deps.)

# Cómo hacer pruebas smoke (2)

1) Gateway
cd C:\Programacion\PaqSuite-IA-AgenteCliente-PAQ
dotnet run --project src\PaqGateway --urls http://127.0.0.1:5100
Firefox: http://127.0.0.1:5100 → 404 OK (conecta).

2) Agente
Servicio/app del agente en marcha, con el mismo agent_id / token que la fila lenovo en 192.168.41.2 / PAQSYSTEMS.

3) BE Tango
cd C:\Programacion\PaqSuite-IA-TANGO\backend
php artisan serve --host=127.0.0.1 --port=8000
Probar: http://127.0.0.1:8000/api/v1/health → debe responder ya (si cuelga, no sigas al login).

4) FE Tango
En frontend/.env:

VITE_API_URL=http://127.0.0.1:8000/api
VITE_TENANT_LOCAL=lenovo

cd C:\Programacion\PaqSuite-IA-TANGO\frontend
npm run dev

5) Login (ventana privada)
http://localhost:3000/?cliente=lenovo
Network: POST a http://127.0.0.1:8000/api/v1/auth/login
Terminal artisan: tiene que aparecer ese POST.

Si algo falla, pará en el primer paso que no cumpla (health / 5100 / POST en artisan) y lo vemos.