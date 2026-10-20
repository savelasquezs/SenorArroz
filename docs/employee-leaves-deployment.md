# Permisos laborales: despliegue coordinado

PR coordinados: SenorArroz #48, senorArrozFront #29 y delivery_app #12. La implementación no cambia nómina, jornadas, rutas ni cuentas de producción por sí sola.

## Antes de desplegar

1. Respaldar la base y revisar todos los usuarios Kitchen (activos e inactivos). Una persona conserva su ID, gastos, nómina e historial. Una terminal debe ser una cuenta aparte, sin nómina ni app de domicilios. No convertir automáticamente una persona que antes se usaba para abrir la pantalla.
2. Ejecutar `SenorArroz.Infrastructure/Scripts/add_employee_leave_requests.sql` con el rol de migración. Es aditivo e idempotente; no activa empleados ni habilita web. Aplicar después `configure_employee_accounts.sql` con la clasificación explícita y revisada. Mantener una terminal operativa por pantalla antes del cambio de API.
3. Configurar `EmployeeLeaves__PrivateBucket` con un bucket dedicado de Google Cloud Storage, prevención de acceso público `enforced`, sin permisos públicos ni tokens de descarga Firebase. No reutilizar un bucket público de productos. La identidad de servicio del backend necesita leer metadatos del bucket y crear/leer/eliminar esos objetos; usar credenciales del entorno, nunca del repositorio.
4. Configurar `EmployeeLeaves__WebBaseUrl` con el origen HTTPS real del panel. Comprobar el proveedor de correo/EmailOutbox existente y los tokens FCM. No se envían justificaciones ni soportes en notificaciones: solo un aviso y acceso autenticado al detalle.
5. Desplegar API y web coordinadamente; después publicar Flutter mediante el flujo existente. No aumentar el mínimo de versión hasta que Google Play distribuya la compilación compatible.

## Verificación posterior autorizada

Con cuentas de prueba: crear permiso desde web y Flutter, aprobar/denegar/aplazar con motivo y contraseña actual, abrir aviso tras iniciar sesión, subir/descargar soporte privado, registrar y verificar regreso. Rechazar contraseña errónea sin cerrar sesión; un segundo administrador con versión anterior obtiene 409. Comprobar terminal de cocina, almuerzos independientes, cuenta personal limitada y aislamiento de sucursal/tenant.

Las pruebas automatizadas validan contratos, autorizaciones, reintentos, firma/tamaño de archivos, auditoría y SQL/RLS en PostgreSQL temporal. No sustituyen comprobar en el entorno real la entrega de correo/FCM, las credenciales del bucket y el selector/exportación de archivos Android. No ejecutar solicitudes reales de empleados como smoke.

## Recuperación

No borrar tablas, soportes ni historial para revertir un cliente. Ante una incidencia, restringir temporalmente el acceso a la nueva funcionalidad y conservar datos para diagnóstico. Restaurar una versión de API solo junto con un plan explícito para las cuentas de terminal; una reversión aislada puede confundir cuentas personales y compartidas.
