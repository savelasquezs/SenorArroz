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

### Preparación productiva — 4 de noviembre de 2026

La conexión productiva creada es `2`, tenant `1`, Santander `1`, tienda `900403973`. Las ocho variables productivas se guardaron en Railway y la barrera de activación coordinada quedó desplegada mediante PR #99. Los siete webhooks respondieron HTTP 201, se confirmaron `ENABLE` con la URL de producción y sus secretos quedaron cifrados en la conexión nueva. El receptor local sigue inactivo hasta completar la carta agrupada.

El usuario pidió conservar la presentación de Partners: Especialidades con siete arroces y cinco tamaños obligatorios por arroz; Combos con dos productos y siete adiciones opcionales (máximo quince en total y cinco por opción); Acompanamientos con chicharrón y yuca corregida a doce unidades. Agregó siete gaseosas: Coca-Cola 1,5 L 10.000; Quatro 1,5 L 9.000; Premio, Manzana, Colombiana y Uva 1,5 L 8.000 cada una; Coca-Cola 3 L 15.000. Los precios son overrides del canal; los precios internos se conservan. Ranchero Familiar conserva 55.000 y el chicharrón 15.000 independiente/12.000 en combos.

El catálogo simple de cincuenta y un productos internos no se envió. Se preparó un borrador agrupado con dieciocho productos visibles y cincuenta y un productos internos referenciados, preservando nombres, descripciones, fotos y posiciones de la carta. El borrador `3` quedó persistido sin publicar. La validación del helper sobre el manifiesto real devolvió cero errores.

Antes de publicar se amplía la resolución de tamaños/adiciones y disponibilidad; la aprobación de la carta agrupada requiere además que `menu/rappi/{storeId}` coincida con el snapshot. El estado general `AVAILABLE` observado antes del primer envío no demuestra aprobación de una publicación nueva.

Nicolás entregó las credenciales COL y confirmó la tienda Santander `900403973` por correo del 3 de noviembre, 23:30 UTC. Solicitó webhooks y menú antes de coordinar la activación. Las credenciales respondieron HTTP 200 y `stores-pa` devolvió exclusivamente `rappiId=900403973`, `integrationId=900403973`. `GET menu/rappi/900403973` respondió HTTP 200 con `items=[]`; esto describe la respuesta del recurso de integración, sin afirmar ausencia de un catálogo comercial en la app.

- La preparación mantiene `Rappi__ProductionStoreActivationApproved=false`. Este permiso es independiente de `Rappi__ProductionEnabled`, que permite autenticar y configurar producción. El worker consulta tiendas/menú pero no escribe `integrated=true` sin aprobación; el proveedor también bloquea ese intento antes de cualquier solicitud HTTP.
- `Rappi__ProductionReadyForPickupApproved=false` hasta autorización y validación específicas de READY.
- La conexión sandbox y sus historiales se conservan. La conexión productiva debe crearse separada para tenant 1, Santander, sucursal 1, y únicamente la tienda confirmada.
- El catálogo sandbox solo contiene cinco tamaños de Paisa con precios internos. No copiarlo ni publicarlo automáticamente como menú productivo completo; confirmar selección y precios de Rappi.
- Pendientes de esta sesión: configuración productiva y webhooks, selección/publicación de menú, aprobación de Rappi y activación coordinada. No se envían mensajes a Nicolás sin instrucción explícita del usuario.

- Confirmar lista de capacidades obligatorias del Sprint 1 y criterio del homologador; horarios y self-onboarding no se añadieron al alcance acordado.
- Entregar integración, tiendas padre/hijas, identificadores y credenciales productivas y confirmar permisos de token.
- Aprobar READY_FOR_PICKUP y revisar el HTTP 424 histórico si continúa en la matriz real.
- Revisar HTTP 500 de `stores-pa/{storeId}/status?integrated=true`, suscripciones y pérdida del resultado PING en el homologador.
- Confirmar aceptación administrativa de T&C y programar validación final.

En servidor: `Rappi__Environment=production`, `Rappi__ProductionEnabled=true`, credenciales productivas, AuthUrl `https://api.rappi.com.co/restaurants/auth/v1/token/login/integrations` y ApiBaseUrl `https://services.rappi.com/api/v2/restaurants-integrations-public-api`. READY exige además `Rappi__ProductionReadyForPickupApproved=true` tras autorización de Rappi. Las tiendas se configuran y verifican para ese ambiente antes de habilitar recepción. La aplicación rechaza cruces de ambiente y la firma heredada del tester no se acepta en producción.

## Mejora posterior sugerida

La sincronización actual reafirma periódicamente el estado integrado. Antes de reducir esas llamadas, confirmar con Rappi el contrato y los permisos de consulta de estado: `GET stores-pa` no devuelve ese flag y no permite inferirlo. Mantener esta optimización fuera del cierre del Sprint 1 hasta acordarla.

## Corrección detectada en la prueba real

Pedido DEV `1348715163`, interno `8954`: recibido y aceptado en aproximadamente 540 ms, una sola creación y efectos duraderos procesados. Productos y total del restaurante: 18.000; cargos del cliente: 8.900, conservados separados. El menú nuevo fue aprobado el 27 de octubre a las 15:12 UTC. Frontend y backend principales desplegados; CI del backend: 1.554 aprobadas y una prueba de mapa omitida; frontend: 960 aprobadas.

El endpoint heredado READY devolvió HTTP 424, mientras el portal DEV sí pasó el pedido a READY FOR PICKUP. La documentación actual del sandbox remite al recurso moderno de pedidos, que incluye la tienda en la URL. Se corrige ese contrato y el presupuesto: Rappi advierte que más de tres solicitudes constituye uso indebido y que repetir READY puede volver a notificar al courier. La cola anterior permitía diez y reiniciaba el contador manualmente. Se pausó expresamente la fila de esta prueba, que alcanzó cuatro intentos, y no se reenviará ese ID. La validación del contrato corregido se hará con un pedido DEV nuevo.

Fuentes: [Order Sandbox](https://dev-portal.rappi.com/en/api-reference/sandbox/), [Orders moderno](https://dev-portal.rappi.com/en/api-reference/rests-api-orders/), [Orders v2](https://dev-portal.rappi.com/en/api-reference/orders/).

Pedido DEV hijo `439999955`, interno `8956`: Growth por producto del 20 %, compartido 50/50, Rappi Pay. Productos 31.000, descuento total 6.200, aliado 3.100, Rappi 3.100, total del restaurante 27.900 y cargos 8.900. Aceptación en unos 280 ms. El pago por app conserva 27.900 y neto estimado 20.925 según la comisión configurada del 25 %; no se liquida ni se crea movimiento bancario.

La comprobación persistida descubrió que los triggers de líneas sustituían el total y descuento del pedido interno por 31.000 y cero. `fix_rappi_authoritative_totals.sql` conserva el contrato autoritativo de pedidos vinculados a Rappi y mantiene el cálculo local. Las 22 pruebas de negocio PostgreSQL pasan con los triggers de producción instalados, incluida conservación del saldo al pasar a Listo y cálculo de un pedido manual. No se hace backfill general; solo se corregirán los IDs de este manifiesto antes de eliminarlos.

READY moderno respondió HTTP 401 con las credenciales actuales. Con un token recién obtenido, `GET /restaurants/orders/v1/stores/900173117/orders/439999955/handoff` respondió 200; el scope READY está presente y el POST documentado sigue devolviendo 401. Debe revisarse con Rappi la habilitación de ese recurso. La sonda con autenticación fallida por DNS se descartó como evidencia; los contadores se conservaron de forma conservadora y no se reenviarán estos IDs. Producción y READY productivo siguen deshabilitados.
