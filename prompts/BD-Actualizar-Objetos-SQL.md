ya que está definido cómo será el proceso de instalación y actualización de agentes que se va a refactorizar, documentado en `docs/02-producto/circuito-actualizacion-agente-funcional.md`, y `docs/02-producto/agente-gateway/insumo-spec-agw-002-update-agente.md`, antes de avanzar con su plan y desarrollo, me gustaría definir , para resolver todo junto, cómo integrar allí la instalación y actualización de los objetos SQL.

toma de referencia base, la documentación de los GEN-18 y GEN-24 del proyecto FRAMEWORK.
Consideraciones a tener en cuenta:
- MULTI : 
	al instalar un cliente nuevo : considerar actualizar en diccionario y en una empresa inicial en PQ_EMPRESA
	al actualizar versiones del host : actualizar en diccionario y en todas las empresas habilitadas en PQ_EMPRESA
	al dar de alta una empresa en el host : actualizar en diccionario y en dicha empresa
- MONO : 
	al instalar un cliente nuevo : Actualizar (insertar) en la base de datos tanto lo que corresponde a seguridad y elementos de configuración (grillas, excels, pivots, tareas programadas, etc) como de la regla de negocio en sí
 	al actualizar el host : actualizar (insert/updates) en la base de datos, elementos generales como del negocio en sí
	no hay alta de empresas en este caso
- Objetos SQL a considerar : 
	Inicialización 
		generar Tablas Seguridad , pq_menus y pq_parametros_gral
		generar Tablas del framework (excels, grillas, reportes, tableros,tareas programadas, etc etc)
		Usuarios admin y PQ
		Rol Supervisor
		1 empresa (tanto en mono como en multi)
		permiso (empresa, rol supervisor, ambos usuarios)
		Diccionario : insertar -> stored procedures, registros pq_menus, registros pq_parametros_gral
	Inicialización 
		update/generar Tablas Seguridad , pq_menus y pq_parametros_gral
		update/generar Tablas del framework (excels, grillas, reportes, tableros,tareas programadas, etc etc)
		Diccionario : update/insertar -> stored procedures, registros pq_menus, registros pq_parametros_gral
- Módulos por cliente : 
    se desagrega el proyecto en módulos.
    cada módulo especifica qué procesos abarca.
    se definir por cada cliente los módulos que ha contratado
    en la instalación/actualización, sólo implementar lo concerniente a los módulos contratados
    considerar un módulo genérico, que se implementa siempre (login, selección empresa, seguridad, parámetros, objetos comunes, etc).
    
