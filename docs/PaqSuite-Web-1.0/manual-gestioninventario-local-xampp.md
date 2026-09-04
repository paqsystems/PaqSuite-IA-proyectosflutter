# Manual de instalación y recuperación

## GestionInventarioLaravel en servidores de clientes

**Fecha:** 3 de septiembre de 2026  
**Alcance:** instalación, reinstalación y actualización local en Windows mediante XAMPP/Apache.  
**Estado:** documento operativo basado en el relevamiento del repositorio. No se modificó código ni se ejecutó ninguna instalación.

## 1. Qué es este sistema

`GestionInventarioLaravel` es una aplicación Laravel 8 que se ejecuta localmente en el servidor físico de cada cliente. No corresponde administrarla como un sitio Forge.

Repositorio: `paqsystems/GestionInventarioLaravel`  
PHP declarado: `^7.3|^8.0`  
Frontend: Laravel Mix  
Entrada web: `public/index.php`

El repositorio contiene:

- `composer.lock`;
- `package-lock.json`;
- `.env.example`;
- migraciones Laravel;
- configuraciones para varias conexiones de base;
- `public/leeme.txt`, que indica retirar o inhabilitar `web.config` para Apache.

No contiene un instalador, batch, servicio Windows ni configuración de Apache propios. Por lo tanto, la instalación depende de la infraestructura del cliente y de una configuración manual documentada.

## 2. Arquitectura esperada

```text
Servidor Windows del cliente
  ├─ XAMPP
  │   ├─ Apache/PHP → GestionInventarioLaravel/public
  │   └─ MySQL      → sólo si esa instalación lo utiliza
  └─ SQL Server/Tango → si las operaciones del cliente lo requieren
```

No debe suponerse que XAMPP/MySQL sea la única base. El proyecto define una conexión de diccionario y varias conexiones de negocio, incluyendo conexiones SQL Server. Cada cliente debe tener una ficha de instalación con las bases realmente utilizadas.

## 3. Información que debe relevarse antes de instalar

- Nombre del equipo y versión de Windows.
- Ubicación de XAMPP.
- Versión de Apache, PHP y MySQL.
- Puerto de Apache y posibles VirtualHosts.
- Ruta actual del proyecto.
- Usuario de Windows que ejecuta Apache.
- Si Apache se inicia manualmente o como servicio.
- Si MySQL de XAMPP está en uso.
- Instancia, puerto y autenticación de SQL Server/Tango.
- Nombre de la base de diccionario.
- Ubicación de archivos cargados por usuarios.
- Backup disponible y procedimiento de restauración probado.
- Tareas programadas o procesos nocturnos externos.

El repositorio no permite deducir estos valores por cliente. Deben observarse en el servidor operativo.

## 4. Preparación de XAMPP

1. Instalar una versión compatible con la versión PHP utilizada actualmente.
2. Instalar Apache y, si corresponde, MySQL desde el panel de XAMPP.
3. Verificar que los puertos de Apache no estén ocupados.
4. Habilitar los módulos necesarios para Laravel, especialmente `mod_rewrite`.
5. Permitir `AllowOverride All` para el directorio público, de modo que Laravel pueda utilizar `.htaccess`.
6. Instalar `pdo_mysql` si se usa MySQL.
7. Instalar los controladores SQL Server/PDO SQLSRV si se utilizan las conexiones de SQL Server.
8. Registrar si Apache y MySQL se ejecutarán como servicios de Windows o desde el panel de XAMPP.

No es obligatorio crear un servicio Laravel adicional si la aplicación sólo atiende solicitudes web.

## 5. Copia de la aplicación

1. Obtener una versión identificada del repositorio o un paquete de release aprobado.
2. Copiar el proyecto en una carpeta estable del servidor.
3. Mantener el `.env` del cliente fuera de Git y fuera de paquetes públicos.
4. Respaldar y conservar `storage` si contiene archivos del cliente.
5. No copiar `vendor` desde otra máquina como método de instalación confiable.
6. No colocar el document root en la raíz del repositorio.

El document root debe ser:

```text
<carpeta-de-la-aplicacion>/public
```

## 6. Configuración de Apache

La aplicación contiene `public/index.php` y debe recibir todas las rutas Laravel mediante Apache. Puede configurarse mediante un VirtualHost o mediante la estructura de `htdocs`, pero el document root siempre debe terminar en `public`.

El archivo `public/web.config` es para IIS. La nota del propio proyecto indica que debe inhabilitarse o retirarse cuando se utiliza Apache. No debe dejarse como mecanismo de reescritura de Apache.

Después de configurar Apache:

1. Iniciar Apache.
2. Probar la página desde el propio servidor.
3. Probar desde otro equipo de la red.
4. Confirmar que una ruta Laravel distinta de la página inicial no devuelva 404 de Apache.
5. Revisar los logs de Apache y `storage/logs/laravel.log` ante cualquier error 500.

## 7. Dependencias PHP y frontend

Desde la raíz del proyecto:

```text
composer install --no-dev --prefer-dist --optimize-autoloader
npm ci
npm run production
```

Si el servidor no tendrá Node.js, ejecutar la compilación en un entorno de construcción controlado y copiar la salida generada al release. `npm run production` es el script confirmado en `package.json`.

## 8. Archivo `.env`

Crear el `.env` usando `.env.example` como guía, pero no copiarlo literalmente a producción.

Configurar como mínimo:

- `APP_KEY`;
- `APP_ENV=production`;
- `APP_DEBUG=false`;
- `APP_URL`;
- conexión MySQL, si corresponde;
- conexión de diccionario;
- conexiones de SQL Server/Tango que correspondan;
- correo;
- sesiones, cache y colas;
- almacenamiento y servicios externos.

La variable personalizada `DB_DICCIONARIO` aparece en el entorno observado y debe verificarse especialmente.

Si se recupera una instalación existente, conservar el `APP_KEY` original. Generar uno nuevo puede inutilizar datos cifrados o sesiones existentes.

El `.env` debe estar protegido para que Apache nunca lo pueda descargar.

## 9. Bases de datos

Antes de ejecutar comandos:

1. Hacer backup de todas las bases involucradas.
2. Confirmar si la base de aplicación/diccionario es MySQL local o remota.
3. Confirmar si las bases de negocio ya existen en SQL Server/Tango.
4. Revisar las migraciones contra la estructura actual.
5. Registrar el estado de migraciones.

Las migraciones observadas crean tablas del sistema y utilizan la conexión `diccionario`. No se debe asumir que crean o actualizan automáticamente las bases de negocio del cliente.

Nunca ejecutar en una base con datos:

```text
php artisan migrate:fresh
php artisan db:wipe
```

Las migraciones normales sólo deben ejecutarse después de ser revisadas y respaldadas:

```text
php artisan migrate --force
```

No ejecutar seeders sin confirmar su finalidad e idempotencia.

## 10. Inicialización Laravel

Después de validar `.env` y las bases:

```text
php artisan storage:link
php artisan config:clear
php artisan route:clear
php artisan view:clear
php artisan config:cache
php artisan route:cache
php artisan view:cache
```

No cachear la configuración antes de cargar el `.env` correcto.

## 11. Inicio automático

El repositorio no contiene evidencia de un servicio o batch propio. En el servidor actual hay que revisar:

- servicios de Windows;
- tareas del Programador de tareas;
- `shell:startup`;
- carpetas de inicio;
- archivos `.bat`, `.cmd` y `.ps1` fuera del repositorio;
- opciones de XAMPP para iniciar Apache/MySQL como servicios;
- reglas del firewall y puertos de red.

Para la instalación provisoria, registrar como mínimo:

- si Apache inicia con Windows;
- bajo qué usuario;
- qué puerto escucha;
- dónde están sus logs;
- si MySQL también inicia automáticamente.

## 12. Actualización de una instalación existente

1. Registrar commit o versión actualmente instalada.
2. Hacer backup de base, aplicación, `.env`, `storage` y archivos persistentes.
3. Probar la nueva versión en otra carpeta o servidor.
4. Preparar `vendor` y los archivos frontend.
5. Detener Apache o usar una nueva carpeta de release.
6. Copiar la nueva aplicación sin reemplazar `.env` ni datos persistentes.
7. Ejecutar sólo las migraciones aprobadas.
8. Limpiar y reconstruir caches Laravel.
9. Iniciar Apache.
10. Mantener la versión anterior hasta completar la aceptación.

## 13. Validación final

- Apache inicia sin errores.
- La URL responde desde el servidor y desde la red.
- Las rutas Laravel funcionan mediante rewrite.
- `APP_DEBUG` está desactivado.
- El login funciona.
- La aplicación lee y escribe en la base correcta.
- Se accede a las bases SQL Server/Tango necesarias.
- Los archivos persistentes se visualizan.
- No aparecen errores nuevos en logs.
- El backup puede localizarse y restaurarse.
- La versión publicada queda registrada.

## 14. Rollback

1. Detener Apache.
2. Conservar la carpeta nueva para análisis.
3. Restaurar la carpeta anterior o el release anterior.
4. Restaurar la base sólo si hubo cambios incompatibles y existe un backup validado.
5. Iniciar Apache.
6. Repetir la validación.

Nunca improvisar una migración inversa de base en producción.

## 15. Conclusión

La reinstalación de Inventario no puede reducirse a “copiar la carpeta y prender XAMPP”. El procedimiento correcto requiere documentar Apache, PHP, extensiones, document root, `.env`, base de diccionario, conexiones SQL Server/Tango, archivos persistentes y servicios de Windows. El repositorio aporta la aplicación Laravel, pero no aporta el instalador ni la configuración completa del servidor del cliente.
