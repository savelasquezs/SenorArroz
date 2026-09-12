# Meta Conversions API — despliegue y rollback

## Objetivo

Enviar `Purchase` a Meta desde el servidor sin hacer que la creación del pedido ni la aprobación de Wompi dependan de la disponibilidad de Meta.

El navegador y el servidor usan el mismo identificador:

```text
purchase-{orderId}
```

Esto permite la deduplicación Pixel + CAPI.

## Orden de despliegue

1. Aplicar `SenorArroz.Infrastructure/Scripts/add_meta_conversions_api.sql` en PostgreSQL.
2. Configurar en el backend de Railway:
   - `META_PIXEL_ID=1941546679814779`
   - `META_CAPI_ACCESS_TOKEN=<secreto>`
   - `META_GRAPH_API_VERSION=v25.0`
   - `META_EVENT_SOURCE_URL=https://senorarroz.com`
   - `META_CAPI_TEST_EVENT_CODE=<código actual de Probar eventos del dataset 1941546679814779>` únicamente durante la validación. No reutilizar automáticamente un código generado para el Pixel anterior.
3. Configurar en el storefront:
   - `NEXT_PUBLIC_META_PIXEL_ID=1941546679814779`
4. Desplegar el storefront con el consentimiento de medición.
5. Desplegar el backend con CAPI.
6. Hacer una compra de prueba aceptando medición.
7. Confirmar en Meta Test Events que `Purchase` llega por Browser y Server con el mismo `event_id` y queda deduplicado.
8. Probar también el flujo Wompi: `Purchase` de servidor solo debe aparecer después de `APPROVED`.
9. Eliminar `META_CAPI_TEST_EVENT_CODE` y redeployar el backend para producción real.

## Qué debe ocurrir

### Efectivo

- Pedido web creado correctamente.
- Cocina recibe su notificación por el outbox existente.
- CAPI procesa su propia parte de forma independiente.
- Un fallo de Meta nunca revierte ni bloquea el pedido.

### Wompi

- `Pending`, `Declined`, `Expired` y `ReviewRequired` no generan `Purchase` de CAPI.
- Solo `APPROVED` que libera el pedido genera `Purchase`.
- Webhook y retorno del navegador pueden observar la misma transacción sin duplicar la conversión, porque el `event_id` depende del `orderId`.

### Sin consentimiento

- GA4 y Meta Pixel no se cargan.
- El backend conserva `meta_consent_granted=false`.
- CAPI marca esa conversión como `ignored` y no envía teléfono, IP, user-agent, `_fbp` ni `_fbc` a Meta.

## Diagnóstico

Un administrador puede consultar:

```text
GET /api/integrations/meta/conversions/status
```

La respuesta no expone el access token. Muestra si CAPI está configurado, si está activo el modo de prueba, conteos de los últimos 7 días (`processed`, `pending`, `failed`, `ignored`) y el último procesamiento/fallo.

## Rollback seguro

Si CAPI presenta problemas después del despliegue:

1. Quitar temporalmente `META_CAPI_ACCESS_TOKEN` del backend y redeployar.
2. No revertir la migración de base de datos; las columnas nuevas son aditivas y no afectan pedidos ni Wompi.
3. El checkout, cocina y pagos continúan funcionando sin CAPI.
4. Investigar `meta_last_error` y el endpoint de diagnóstico antes de volver a habilitar el token.

Si se necesita detener también la medición del navegador, revertir el storefront o elegir `Solo esenciales` durante las pruebas. No reutilizar el Pixel anterior como fallback accidental.

## Seguridad

- El access token existe solo en el backend/Railway.
- Nunca se usa una variable `NEXT_PUBLIC_` para el token.
- El teléfono se normaliza y se envía a Meta únicamente como SHA-256.
- No se envían a Meta nombre, dirección de entrega ni coordenadas.
- Los errores persistidos están truncados y no incluyen el access token.
