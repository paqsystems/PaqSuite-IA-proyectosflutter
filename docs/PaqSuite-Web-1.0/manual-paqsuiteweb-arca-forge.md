# Manual de instalación, despliegue y recuperación

## PaqSuiteWeb1.0Backend / integración ARCA-ARCA/AFIP

**Fecha:** 3 de septiembre de 2026  
**Alcance:** sitios de desarrollo y producción administrados por Laravel Forge.  
**Estado:** documento operativo basado en el relevamiento del repositorio y Forge. No se modificó código ni se ejecutó ningún deploy.

## 1. Sitios confirmados

Repositorio: `paqsystems/PaqSuiteWeb1.0Backend`

| Ambiente | Dominio | Rama | Sitio Forge |
|---|---|---|---|
| Desarrollo / homologación | `afip_desarrollo.paqsystems.ar` | `afip_desarrollo` | `2450042` |
| Producción | `afip.paqsystems.ar` | `afip_produccion` | `2450046` |

Ambos sitios están dentro del proyecto Forge `paq-2021`, usan PHP 8.1 y tienen document root en el directorio `public` de cada aplicación.

## 2. Función del proyecto

El proyecto contiene la integración con ARCA/AFIP, incluyendo:

- autenticación WSAA;
- firma CMS mediante OpenSSL;
- WSDL locales;
- consulta de persona;
- consulta del último comprobante;
- operaciones de comprobantes;
- endpoints diferenciados para desarrollo y producción.

Los archivos relevantes están en controladores AFIP, rutas API, configuración AFIP y `public/wsdl`.

## 3. Requisitos del servidor

Verificar antes de una reinstalación:

- PHP 8.1;
- Composer;
- PDO para MySQL;
- SQL Server/PDO SQLSRV si el ambiente lo necesita;
- `soap`;
- `openssl`;
- `mbstring`;
- `xml`;
- `curl`;
- `fileinfo`;
- `zip`;
- binario OpenSSL ejecutable por PHP-FPM;
- salida HTTPS hacia los servicios ARCA/AFIP;
- hora y zona horaria correctas;
- permisos de escritura en `storage` y `bootstrap/cache`.

El servidor Forge observado utiliza Ubuntu 20.04 y Forge lo marca fuera de ciclo de vida. No se recomienda reproducir esa plataforma en un servidor nuevo sin un plan de migración a un sistema soportado. Tampoco debe actualizarse el servidor productivo actual sin una réplica previa.

## 4. Información sensible que debe recuperarse

El repositorio no tiene `.env.example`. Antes de reinstalar hay que recuperar del ambiente vigente:

- `APP_KEY`;
- conexión MySQL;
- conexiones SQL Server si corresponden;
- correo;
- almacenamiento y servicios externos;
- rutas de certificados ARCA/AFIP;
- claves privadas y contraseñas asociadas;
- ambiente de homologación o producción;
- valores de tenant/empresa.

Los secretos deben conservarse en un gestor seguro. No deben ponerse en Git, manuales, capturas ni deploy hooks.

Como control de seguridad, revisar las configuraciones fuente: se observaron referencias y valores sensibles embebidos en archivos de configuración. En una reinstalación conviene reemplazarlos por variables de entorno y rotar credenciales cuando sea posible.

## 5. Alta de un sitio en Forge

1. Crear o seleccionar un servidor soportado.
2. Crear el sitio con el dominio correspondiente.
3. Conectar el repositorio GitHub correcto.
4. Seleccionar la rama del ambiente.
5. Configurar DNS.
6. Emitir y validar el certificado TLS.
7. Confirmar document root `<raíz-del-sitio>/public`.
8. Crear base y usuario con privilegios mínimos.
9. Cargar el `.env` del ambiente.
10. Instalar dependencias.
11. Instalar certificados fuera del repositorio.
12. Revisar permisos, caches y logs.
13. Restaurar la base o ejecutar la inicialización aprobada.
14. Probar primero en homologación.

## 6. Instalación de dependencias

Desde la raíz del sitio:

```text
composer install --no-dev --no-interaction --prefer-dist --optimize-autoloader
php artisan storage:link
php artisan config:clear
php artisan route:clear
php artisan view:clear
php artisan config:cache
php artisan route:cache
php artisan view:cache
```

No se debe ejecutar `composer update` durante una reinstalación productiva. La instalación debe respetar `composer.lock`.

## 7. Configuración ARCA/AFIP

Verificar:

- certificados y claves legibles por el usuario de PHP-FPM;
- rutas configuradas correctamente;
- WSDL presentes en `public/wsdl`;
- `wsaa.wsdl` disponible en la ubicación configurada;
- OpenSSL ejecutable desde el contexto de PHP-FPM;
- permisos de archivos temporales;
- salida HTTPS a los endpoints correctos;
- ambiente correcto: homologación para desarrollo, producción para el sitio operativo.

El código genera `TRA.xml` mediante una ruta relativa. Debe probarse desde PHP-FPM, no únicamente desde SSH, porque el directorio de trabajo y los permisos pueden ser diferentes.

## 8. Deploy actual de Forge

El script actualmente registrado para cada sitio realiza esencialmente:

```text
cd /home/forge/<dominio>
git pull origin $FORGE_SITE_BRANCH
$FORGE_COMPOSER install --no-dev --no-interaction --prefer-dist --optimize-autoloader
recargar PHP-FPM
```

Configuración observada:

- Desarrollo: push-to-deploy activado.
- Producción: push-to-deploy desactivado; deploy manual.
- Health checks de Forge: desactivados.
- GitHub deployments: desactivados.
- Migraciones productivas: comentadas.

La separación es adecuada para reducir el riesgo de publicar automáticamente en producción. No activar migraciones automáticas hasta contar con un proceso formal de cambios de estructura SQL.

## 9. Procedimiento de despliegue

### Desarrollo

1. Publicar cambios únicamente en `afip_desarrollo`.
2. Esperar o iniciar el deploy de Forge.
3. Revisar el resultado del proceso.
4. Verificar logs Laravel y PHP-FPM.
5. Ejecutar pruebas contra homologación.
6. Registrar commit y resultado.

### Producción

1. Confirmar que el commit fue validado en desarrollo.
2. Confirmar backup y ventana de cambio.
3. Publicar el merge aprobado en `afip_produccion`.
4. Ejecutar deploy manual desde Forge.
5. Revisar Composer, permisos y recarga PHP-FPM.
6. Ejecutar el checklist ARCA/AFIP.
7. Registrar commit, fecha, responsable y resultado.

## 10. Base de datos y migraciones

Antes de cualquier cambio:

1. Respaldar las bases.
2. Revisar cada migración contra la estructura productiva.
3. Probar la restauración del backup.
4. Ejecutar migraciones sólo con autorización.

Nunca ejecutar:

```text
php artisan migrate:fresh
php artisan db:wipe
```

No utilizar la ruta raíz de la API como health check automático: en este proyecto puede ejecutar tareas de limpieza y cache de Laravel, por lo que no es una prueba inocua.

## 11. Scheduler, colas y procesos auxiliares

En el sitio AFIP productivo no se observó un background process asociado. El repositorio contiene artefactos de scheduler para Windows, pero existe una diferencia entre la documentación y el comando que aparece en `scheduler-runner.js`.

Antes de reinstalar cualquier proceso, confirmar en el servidor actual:

- comando exacto;
- frecuencia;
- usuario;
- directorio de trabajo;
- variables disponibles;
- ubicación de logs;
- comportamiento ante error;
- si el proceso es realmente necesario para AFIP.

No instalar scheduler ni workers por intuición. La primera recuperación debe validar el flujo web y ARCA/AFIP; los procesos auxiliares se agregan sólo con evidencia.

## 12. Pruebas posteriores al deploy

1. HTTPS válido.
2. Laravel inicia sin `APP_DEBUG` expuesto.
3. Certificado y clave accesibles.
4. Ticket WSAA obtenido correctamente.
5. Ticket vigente dentro del período esperado.
6. Consulta de persona correcta.
7. Consulta de último comprobante correcta.
8. Operación de comprobante controlada.
9. WSDL y logs sin errores nuevos.
10. Ambiente de AFIP correcto.

No probar producción con datos reales hasta completar homologación y contar con autorización.

## 13. Backup y rollback

Antes del deploy conservar:

- backup de base;
- `.env` seguro;
- certificados;
- `storage`;
- commit actual;
- configuración de Forge;
- logs recientes.

Para rollback:

1. Detener deploy automático si estuviera activo.
2. Identificar el último commit funcional.
3. Volver a publicar ese commit desde Forge.
4. Recargar PHP-FPM.
5. Restaurar base sólo si hubo una modificación incompatible y existe un procedimiento aprobado.
6. Repetir las pruebas ARCA/AFIP.

Nunca improvisar una migración inversa.

## 14. Información que debe quedar documentada

Para cada ambiente:

- dominio;
- servidor Forge;
- ID del sitio;
- rama;
- commit actualmente publicado;
- document root;
- PHP y extensiones;
- script de deploy;
- estado de push-to-deploy;
- cron, workers y procesos;
- variables requeridas, sin sus valores secretos;
- ubicación de certificados;
- backup y restore;
- procedimiento de rollback.

## 15. Conclusión

PaqSuiteWeb1.0Backend sí corresponde a los dos sitios que actualmente intermedian con ARCA/AFIP. La reinstalación reproducible depende principalmente de recuperar correctamente `.env`, bases, certificados, extensiones PHP y la configuración de Forge. El despliegue actual es simple y no incluye migraciones automáticas; esa característica debe conservarse hasta definir formalmente la evolución de la estructura SQL.
