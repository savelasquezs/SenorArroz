# Permisos laborales: despliegue coordinado

PR coordinados: SenorArroz #48, senorArrozFront #29 y delivery_app #12. No cambiar nómina, jornadas, rutas ni contraseñas durante el despliegue.

## Antes de desplegar

1. Respaldar la base y revisar todos los usuarios Kitchen (activos e inactivos). Una persona conserva su ID, gastos, nómina e historial. Una terminal debe ser una cuenta aparte, sin nómina ni app de domicilios.
2. Ejecutar `SenorArroz.Infrastructure/Scripts/add_employee_leave_requests.sql` con el rol de migración. Es aditivo e idempotente; no activa empleados ni habilita web.
3. Para el tenant Señor Arroz (1), ejecutar `SenorArroz.Infrastructure/Scripts/configure_senor_arroz_kitchen_terminals.sql`: clasifica únicamente las terminales confirmadas `juan@juan.com` (usuario 5, Santander) y `pao.villorina@hotmail.com` (usuario 10, Manrique). Valida IDs, correos exactos normalizados, sucursales, rol, accesos y ausencia de historial personal. Si no coinciden, revierte toda la clasificación. No modifica `active`, contraseñas, sesiones, nómina ni acceso web. Para otros inventarios revisados sigue disponible `configure_employee_accounts.sql` con parámetros explícitos.
4. Configurar `EmployeeLeaves__PrivateBucket` con un bucket dedicado de Google Cloud Storage, prevención de acceso público `enforced`, sin permisos públicos ni tokens de descarga Firebase. No reutilizar un bucket público de productos. La identidad de servicio del backend necesita leer metadatos del bucket y crear/leer/eliminar esos objetos; usar credenciales del entorno, nunca del repositorio.
5. Configurar `EmployeeLeaves__WebBaseUrl` con el origen HTTPS real del panel. Comprobar el proveedor de correo/EmailOutbox existente y los tokens FCM. No se envían justificaciones ni soportes en notificaciones: solo un aviso y acceso autenticado al detalle.
6. Desplegar primero la API y comprobar sus nuevas autorizaciones; después la web. Solo entonces habilitar acceso web de los cocineros personales activos revisados (usuarios 3 y 11), sin cambiar `active`. No habilitarlos antes de la API: la versión antigua confunde acceso web con acceso operativo a cocina.
7. Integrar Flutter al final. Mantener la actualización manual acordada y no elevar el mínimo de versión antes de que la compilación compatible esté disponible. Revisar los disparadores del workflow de Google Play antes del merge para no lanzar una publicación imprevista.

## Verificación posterior autorizada

Con cuentas de prueba: crear permiso desde web y Flutter, aprobar/denegar/aplazar con motivo y contraseña actual, abrir aviso tras iniciar sesión, subir/descargar soporte privado, registrar y verificar regreso. Rechazar contraseña errónea sin cerrar sesión; un segundo administrador con versión anterior obtiene 409. Comprobar terminal de cocina, almuerzos independientes, cuenta personal limitada y aislamiento de sucursal/tenant.

Las pruebas automatizadas validan contratos, autorizaciones, reintentos, firma/tamaño de archivos, auditoría y SQL/RLS en PostgreSQL temporal. No sustituyen comprobar en el entorno real la entrega de correo/FCM, las credenciales del bucket y el selector/exportación de archivos Android. No ejecutar solicitudes reales de empleados como smoke.

## Recuperación

No borrar tablas, soportes ni historial para revertir un cliente. Ante una incidencia, restringir temporalmente el acceso a la nueva funcionalidad y conservar datos para diagnóstico. Restaurar una versión de API solo junto con un plan explícito para las cuentas de terminal; una reversión aislada puede confundir cuentas personales y compartidas.
