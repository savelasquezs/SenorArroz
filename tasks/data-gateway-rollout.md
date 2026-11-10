# Data Gateway: activación controlada

## Estado

Implementación integrada en la API existente; flag desactivado por defecto. SQL preparado, sin aplicación automática en producción. Actions permanece operativo. No se cambiaron contraseñas ni se crearon servicios Railway.

## Pasos pendientes que requieren operador

1. Autorizar expresamente la aplicación del archivo importado y su SHA-256 mediante el procedimiento de `senorarroz-db-assistant`; no emitir el comentario de aprobación sin autorización del propietario.
2. Instalar la misma credencial `senorarroz_db_reader` en `ConnectionStrings__DataGatewayReader` de Railway. No sustituirla por la cuenta de mantenimiento.
3. Configurar un proveedor OAuth con PKCE S256, cliente ChatGPT, audience Resource, scope gateway:read, tenant_id=1 y sujetos autorizados. No hay proveedor configurado actualmente en Railway.
4. Después de aplicar SQL: pruebas con el lector real en producción, activar el flag y realizar consentimiento/instalación del conector desde la cuenta ChatGPT. Mantener el flag apagado hasta completar estas comprobaciones.

## Respaldo

Objeto Railway existente `backup-20261110-080417.dump`, 2026-11-10 08:04:21 UTC, 13 522 480 bytes. SHA-256 `617c67c1ffcb8dc5c65d90207ba0edaae94c2eaf552a421080ca3ced8c09dee2`.

Restaurado íntegramente con PostgreSQL 17 en base local aislada (exit 0, 129 tablas). Se recreó únicamente en el servidor de pruebas el nombre del rol requerido por las políticas del dump; el respaldo usa --no-owner/--no-acl, por lo que no restaura contraseñas ni roles de cluster. Migración aplicada dos veces sobre el clon: 37 guards y 38 vistas, sin error; SELECT de columnas privadas y acceso cross-tenant denegados. 30 consultas de resumen sobre los datos restaurados: promedio 14.19 ms, máximo 16.76 ms (medición local; no predice latencia Railway). Clon eliminado tras verificar. Huella normalizada de migración: `82e6255386cb296fa7f6c6728d97b06175d32d4bba125b0d6071ed044d4ad9eb`.

Un resultado SUCCESS del cron por sí solo no acredita restaurabilidad. La autorización debe revisar antigüedad del backup en el momento de aplicar. La prueba de restauración no constituye autorización de modificación de producción.
