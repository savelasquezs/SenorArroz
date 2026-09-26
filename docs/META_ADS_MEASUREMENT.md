# Meta Ads — medición del storefront

## Problema resuelto

El Pixel y GA4 requieren consentimiento. Por eso Ads Manager puede mostrar muchos clics outbound y muy pocos Landing Page Views aunque la web sí haya cargado.

La solución separa dos capas:

1. **Analítica operativa propia**: siempre disponible, agregada y sin PII.
2. **Meta Pixel + CAPI**: únicamente cuando el visitante autoriza medición.

## Analítica operativa

El storefront envía al BFF eventos como:

- `page_view`
- `view_item`
- `add_to_cart`
- `begin_checkout`
- `coverage_success`
- `address_validated`
- `otp_requested`
- `otp_verified`
- `delivery_quote_success`
- `order_submit_success`
- `purchase`

El backend **no guarda eventos individuales**. Hace UPSERT sobre `storefront_analytics_daily` y conserva solo contadores agregados por:

- día;
- evento;
- path;
- UTM source / medium / campaign / content / term;
- sucursal;
- tipo de entrega;
- método de pago;
- estado técnico.

No se persisten IP, teléfono, `_fbp`, `_fbc`, user-agent, order id ni session id en esta tabla.

## Consentimiento y Meta

Sin consentimiento:

- no se carga Meta Pixel;
- no se carga GA4;
- no se envían datos de coincidencia a Meta;
- sí se incrementan los contadores operativos anónimos.

Con consentimiento:

- Pixel funciona en navegador;
- el BFF reenvía temporalmente user-agent, IP, `_fbp` y `_fbc`;
- CAPI replica `ViewContent`, `AddToCart`, `InitiateCheckout` y `AddPaymentInfo`;
- Browser y Server comparten el mismo `event_id`, para deduplicación;
- `Purchase` sigue usando el outbox existente.

## Despliegue

1. Ejecutar:
   `SenorArroz.Infrastructure/Scripts/add_storefront_operational_analytics.sql`
2. Desplegar la API.
3. Desplegar el storefront.
4. Configurar los anuncios con destino HTTPS y UTMs.
5. Hacer una prueba sin aceptar medición: el funnel interno debe aumentar, Meta no.
6. Hacer una prueba aceptando medición: el funnel interno debe aumentar y Meta Test Events debe mostrar Browser + Server deduplicados.
7. Confirmar compra en efectivo y Wompi para validar `Purchase`.

## Diagnóstico

Con JWT de Admin o Superadmin:

`GET /api/integrations/meta/analytics/funnel?days=7`

El reporte devuelve contadores agregados por fecha, evento, campaña, contenido, sucursal, fulfillment y pago.

El estado del CAPI de compras continúa disponible en:

`GET /api/integrations/meta/conversions/status`

## UTMs recomendadas

Santander:

`utm_source=meta&utm_medium=paid_social&utm_campaign=ventas_web_sep_2026&utm_content=santander_anuncio_01`

Manrique:

`utm_source=meta&utm_medium=paid_social&utm_campaign=ventas_web_sep_2026&utm_content=manrique_anuncio_01`

## Rollback

- El canal operativo es no bloqueante: si falla, el pedido sigue funcionando.
- Para detener CAPI basta retirar temporalmente `META_CAPI_ACCESS_TOKEN`.
- La tabla agregada puede permanecer creada aunque se revierta el código.
- No eliminar el banner de consentimiento como mecanismo de corrección de medición.
