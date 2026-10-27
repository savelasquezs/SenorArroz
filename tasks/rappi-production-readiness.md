# Preparación Rappi para producción

Base API: `1842a0966428dfe76d58028db14e56af9b1dfccc`. Base frontend: `b615cc54f99bd0b3af44c05c2fe0a46cb8455a89`.

- [x] Persistir intento de aceptación antes del efecto remoto y conservar serialización con cancelación.
- [x] Evitar reenvíos automáticos de escrituras con resultado remoto incierto.
- [x] Recuperar avisos e impresión tras aceptación y eventos recibidos antes del pedido.
- [x] Configurar ambiente y tiendas productivas desde contexto seguro, sin valores sandbox implícitos.
- [x] Completar diagnóstico, reintentos y paginación en frontend.
- [x] Preparar limpieza acotada por IDs; validada contra PostgreSQL desechable con otro pedido que debe conservarse.
- [x] Aplicar y verificar índice por sucursal/proveedor/ambiente en MainDatabase, conservando la conexión sandbox existente.
- [ ] Validar regresiones PostgreSQL, HTTP, tenant/sucursal, frontend y CI.
- [ ] Ejecutar matriz DEV: órdenes, Growth, menú/herencia, stockout, cancelación, READY y cierre.
- [ ] Preparar evidencia y borrador para Nicolás con pendientes externos explícitos.

No activar credenciales ni tiendas productivas sin habilitación de Rappi. El correo se prepara para revisión; no se envía.

## Evidencia inicial del 27 de octubre

Correos revisados: seis recibidos de Nicolás Ballesteros y cinco enviados. El alcance del Sprint 1 es inyección de pedidos, descuentos Growth, productos simples y mapeo automático, stockout, health check y webhooks. Nicolás confirmó avances DEV y pidió una matriz OK/pendiente; indicó que algunas funciones solo se habilitan en producción. TRATO registra firmas completadas, pero Rappi debe confirmar el cierre administrativo.

Integrations Manager: `SeñorArrozDevV2 (DEV)` / `SENORARROZDEVV2`, padre `900173116`, hija `900173117`. Siete suscripciones activas por tienda. PING real respondió HTTP 200 en ambas; firma ausente o alterada respondió HTTP 401. HMAC figura adoptado. El homologador conserva 4/15 capacidades y pierde el reconocimiento de PING al volver a la pantalla, aunque su prueba directa obtiene HTTP 200. La falta de llamadas en 30 días no prueba ausencia de implementación.

En PROD solo aparece `DEFAULT (NO USAR)`. No se han activado credenciales ni tiendas de producción. La aprobación definitiva requiere Rappi.

## Despliegue y reversión

1. Índice idempotente `SenorArroz.Infrastructure/Scripts/rappi_connections_by_environment.sql`: aplicado y confirmado en MainDatabase el 27 de octubre. Se añadió al inicializador raíz mediante `update-rappi-production-readiness-2026-10-27.sql`.
2. Esperar CI de API, desplegar backend y comprobar salud, paginación, permisos y recepción firmada.
3. Esperar CI de frontend, desplegar y verificar pantalla Rappi, diagnóstico, cocina y consola.
4. Ejecutar matriz DEV y registrar IDs exactos, tenant, conexión, comienzo y fin UTC. Después limpiar únicamente esos IDs con `cleanup_rappi_test_orders.sql` y verificar inventario, pagos, impresión y marcador terminal.
5. Preparar borrador para Nicolás con evidencia y pendientes externos.

El índice no cambia datos ni activa ambientes. La reversión del backend anterior es compatible mientras exista solo una conexión por sucursal/proveedor. Tras añadir producción, conservar esta versión y revertir mediante un cambio correctivo: las versiones anteriores no filtran por ambiente. No borrar ni fusionar historiales de conexiones.

La limpieza exige las variables de sesión `app.rappi_test_tenant_id`, `app.rappi_test_connection_id`, `app.rappi_test_started_at`, `app.rappi_test_finished_at`, `app.rappi_test_external_ids` y `app.rappi_test_cleanup_approved=RAPPI_TEST_CLEANUP`. El script aborta ante manifiesto incompleto, conexiones no sandbox, aceptación pendiente, pagos liquidados, bancos, rutas internas o impresión en proceso. Conserva historia técnica mínima para impedir que un replay recree el pedido. No incluye pedidos históricos ajenos a esta sesión.

## Activación productiva pendiente de Rappi

- Confirmar lista de capacidades obligatorias del Sprint 1 y criterio del homologador; horarios y self-onboarding no se añadieron al alcance acordado.
- Entregar integración, tiendas padre/hijas, identificadores y credenciales productivas y confirmar permisos de token.
- Aprobar READY_FOR_PICKUP y revisar el HTTP 424 histórico si continúa en la matriz real.
- Revisar HTTP 500 de `stores-pa/{storeId}/status?integrated=true`, suscripciones y pérdida del resultado PING en el homologador.
- Confirmar aceptación administrativa de T&C y programar validación final.

En servidor: `Rappi__Environment=production`, `Rappi__ProductionEnabled=true`, credenciales productivas, AuthUrl `https://api.rappi.com.co/restaurants/auth/v1/token/login/integrations` y ApiBaseUrl `https://services.rappi.com/api/v2/restaurants-integrations-public-api`. READY exige además `Rappi__ProductionReadyForPickupApproved=true` tras autorización de Rappi. Las tiendas se configuran y verifican para ese ambiente antes de habilitar recepción. La aplicación rechaza cruces de ambiente y la firma heredada del tester no se acepta en producción.

## Mejora posterior sugerida

La sincronización actual reafirma periódicamente el estado integrado. Antes de reducir esas llamadas, confirmar con Rappi el contrato y los permisos de consulta de estado: `GET stores-pa` no devuelve ese flag y no permite inferirlo. Mantener esta optimización fuera del cierre del Sprint 1 hasta acordarla.
