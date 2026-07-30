# Integración Rappi API v2

## Estado

La implementación usa el ambiente sandbox y no contiene credenciales. Los secretos de OAuth se leen exclusivamente desde variables de entorno. Los secretos entregados al registrar webhooks se cifran antes de persistirse.

Autenticación sandbox validada:

- `POST https://api.dev.rappi.com/restaurants/auth/v1/token/login/integrations`.
- Cuerpo JSON únicamente con `client_id` y `client_secret`.
- El token se envía a API v2 mediante `x-authorization: bearer {access_token}`.

Tiendas de prueba:

- Padre: `900173116`, Señor Arroz Dev1.
- Hija: `900173117`, Señor Arroz Dev2.
- Ambas pertenecen a Santander.
- La padre usa `store_integration_id=900173116` y la hija `store_integration_id=900173117`, según la respuesta real de `stores-pa`.
- En POS Tester usar `POS=SeñorArrozDevV2` e `INTEGRACIÓN=SENORARROZDEVV2`.
- El menú se publica únicamente a la tienda padre.

Valor pendiente:

- Confirmación de Rappi para habilitar manualmente `READY_FOR_PICKUP`.

## Fase 0 — Base de datos y Railway

1. Crear un respaldo de la base de datos de Railway.
2. Abrir Bash en la raíz del repositorio.
3. Conectarse con `railway connect MainDatabase`.
4. Ejecutar:

   ```sql
   \i senorArrozAPI/SenorArroz.Infrastructure/Scripts/upgrade_rappi_v2_sandbox.sql
   ```

5. Confirmar que la transacción terminó con `COMMIT`.
6. Configurar en el servicio API de Railway:

   ```text
   Rappi__ClientId
   Rappi__ClientSecret
   ApiPublic__BaseUrl=https://senorarrozapi.up.railway.app
   Integrations__EncryptionKey
   ```

7. Desplegar el backend.
8. En Santander, abrir la configuración de Rappi.
9. Seleccionar por ID el cliente interno Rappi y la app financiera existentes.
10. Conservar la comisión inicial en `25%`, escoger el tiempo de cocción y guardar las dos tiendas.

No continuar si una credencial aparece en la base de datos, respuesta del API, frontend o logs.

## Fase 1 — Autenticación y tiendas

1. Pulsar **Probar conexión**.
2. Verificar que el panel muestre exactamente `900173116` y `900173117`.
3. Confirmar `integrationId=900173116` para la padre y `integrationId=900173117` para la hija.

Detener el proceso si falta una tienda o si Rappi no entrega los identificadores necesarios para disponibilidad.

## Fase 2 — Webhooks y conectividad

1. Pulsar **Configurar webhooks** una sola vez.
2. Confirmar en el panel los eventos:
   `NEW_ORDER`, `ORDER_EVENT_CANCEL`, `ORDER_OTHER_EVENT`,
   `MENU_APPROVED`, `MENU_REJECTED`, `PING` y `STORE_CONNECTIVITY`.
3. Consultar las suscripciones en Integrations Manager.
4. Enviar un `PING` válido a ambas tiendas.
5. Probar una firma inválida y confirmar HTTP 401.
6. Repetir un payload válido y confirmar que solo se procese una vez.
7. Enviar conectividad activa e inactiva y verificar que solo cambie el estado operativo.

Las rutas públicas tienen el formato:

```text
/api/integrations/rappi/webhooks/{publicId}/{event}
```

Solo estas rutas son anónimas y todas exigen una firma válida.

El registro es recuperable: si el webhook ya existe en Rappi pero el secreto local no fue persistido, el sistema usa `PUT webhook/{event}/reset-secret`, cifra el nuevo secreto y continúa sin crear un webhook duplicado.

El Sandbox Tester envía `STORE_CONNECTIVITY` con `store_id`, `online` y `checked_at`; el backend también conserva compatibilidad con `external_store_id`, `enabled` y `message`, que es el formato documentado.

## Fase 3 — Menú automático

1. Seleccionar un catálogo pequeño de productos simples.
2. Completar nombre, descripción, imagen o precio únicamente cuando se requiera un override.
3. Abrir la vista previa y corregir todas las validaciones.
4. Pulsar **Publicar menú**.
5. En Integrations Manager abrir **POS Tester > Menú**.
6. Ingresar los valores POS e INTEGRACIÓN entregados por Rappi.
7. Buscar la tienda padre `900173116`.
8. Ignorar “Recurso no encontrado” únicamente antes de la primera publicación.
9. Esperar `MENU_APPROVED`.
10. Confirmar que `900173117` heredó el mismo menú.

Los SKU son inmutables: `product-{ProductId}`. Las categorías usan `category-{ProductCategoryId}`. El Sprint 1 no publica toppings ni modificadores.

## Fase 4 — Disponibilidad

1. Apagar un producto seleccionado desde Señor Arroz.
2. Verificar el stockout en padre e hija.
3. Encenderlo nuevamente.
4. Pulsar **Reconciliar disponibilidad**.
5. Confirmar que ambas tiendas convergen al estado interno.

No usar el rechazo de órdenes para deshabilitar productos.

## Fase 5 — Órdenes

1. Enviar una orden delivery con courier Rappi desde cada tienda.
2. Verificar impresión de cocina, detalle del pedido, cliente Rappi y tienda origen.
3. Probar un descuento financiado por Rappi.
4. Probar un descuento financiado por Señor Arroz.
5. Probar SKU inválido, precio distinto, agotado, modificador y payload repetido.
6. Revalidar y aceptar una incidencia corregida.
7. Rechazar otra incidencia.
8. Confirmar recuperación automática de una orden `SENT` cuyo webhook fue omitido.

Una orden inconsistente se retiene y nunca se sustituye silenciosamente. El total bruto cubre el pago del pedido; comisión y neto esperado se registran por separado.

## Fase 6 — Estados, cancelaciones y liquidación

1. Solicitar a Rappi la activación manual de `READY_FOR_PICKUP`.
2. Habilitar la opción individualmente solo después de la confirmación.
3. Probar `Listo`, entrega al courier, cierre y cancelación.
4. Cancelar una orden antes de liquidarla y confirmar que el pago por app quede revertido.
5. Cancelar otra después de liquidarla y confirmar la incidencia financiera sin borrar movimientos.
6. Liquidar un lote ingresando una consignación real distinta al neto esperado.
7. Confirmar el prorrateo, residuo en la última orden y diferencia individual.

## Fase 7 — Aceptación

Ejecutar en ambas tiendas:

- token expirado;
- webhook omitido y repetido;
- firma incorrecta;
- reinicio del despliegue;
- indisponibilidad temporal de Rappi;
- menú aprobado y rechazado;
- stockout y reconciliación;
- orden válida, retenida, rechazada, cancelada y cerrada;
- anonimización de información personal con datos de prueba.

No configurar producción hasta recibir credenciales, tiendas, dominio y autorización explícita de Rappi.

## Diagnóstico

El panel administrativo muestra conexión, tiendas, webhooks, PING, conectividad, publicación de menú, disponibilidad y último error sanitizado. La bandeja Rappi contiene incidencias, reintentos y trazabilidad; no contiene un catálogo simulado.
