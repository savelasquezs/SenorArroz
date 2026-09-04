# WhatsApp Storefront Flow

## Alcance v1

El canal central de WhatsApp atiende exclusivamente al tenant `1` y reutiliza el motor comercial del storefront. El número puede conservar temporalmente su configuración histórica por sucursal, pero una conversación recibida por `whatsapp_channel_setting` es tenant-wide y su `OperationalBranchId` permanece nulo hasta cotizar domicilio o seleccionar recogida.

El recorrido publicado en Meta, habilitado para pruebas internas y todavía pendiente de homologación completa, es:

```text
FULFILLMENT -> ADDRESS_PICKUP -> CATEGORY -> PRODUCTS -> CART -> BENEFITS -> PAYMENT -> SUMMARY -> SUCCESS
```

Efectivo crea el pedido de forma idempotente. Wompi crea un checkout de 15 minutos y el pedido solo se materializa al procesar una aprobación válida. Los mensajes posteriores salen por `whatsapp_commerce_outbox`.

## Inicio sin IA

Un saludo simple o un comando de compra (`pedido`, `pedir`, `comprar`, `hacer pedido`, `ver menú`) ofrece el botón interactivo sin consultar la IA. Funciona en atención humana, esperando asesor y con la IA pausada, conservando la asignación y el modo actuales. La bienvenida y el botón forman un mismo mensaje de WhatsApp.

Un saludo repetido durante una sesión activa no vuelve a enviar el botón ni reemplaza el token. El comando `pedido` permite reenviar la invitación conservando las selecciones; el nuevo token reemplaza al anterior y los importes se recotizan. Las invitaciones y el estado «Menú interactivo disponible» se notifican también a la bandeja.

La configuración se encuentra en `/whatsapp/settings` (menú «Canal central WhatsApp», Admin/Superadmin). «IA activa» solo controla el asistente conversacional; no es requisito de Flow. La atención de una conversación se cambia desde la bandeja, independientemente del menú de compra.

## Componentes

- `StorefrontCommerceService`: motor comercial compartido, sin contexto HTTP ni llamadas al propio backend; el controlador público conserva sus contratos.
- `WhatsAppCommerceFlowService`: sesión, catálogo paginado, carrito, recotización y confirmación.
- `WhatsAppFlowsController`: endpoint cifrado `POST /api/whatsapp/flows/{channelPublicId}/data-exchange`.
- `WhatsAppFlowCrypto`: RSA-OAEP/SHA-256 y AES-GCM con IV de 16 bytes y respuesta usando el IV invertido.
- `WhatsAppCommerceOutboxWorker`: entrega reintentable de enlaces y confirmaciones.
- `TenantWhatsAppSettingsController`: configuración central de canal, Flow e IA para Admin/Superadmin.
- `WhatsAppFlows/storefront-flow.json`: definición que se carga y publica en Meta.

El webhook habitual conserva `X-Hub-Signature-256`. El endpoint de datos identifica el canal por `channelPublicId`, descifra el sobre de Meta, valida el hash del `flow_token`, limita tamaño/frecuencia y deduplica por huella canónica.

## Persistencia y privacidad

- `whatsapp_channel_setting`: credenciales tenant-wide y activación independiente de Flow.
- `tenant_ai_setting`: configuración de IA central.
- `whatsapp_commerce_session`: estado JSON versionado, hash del token, expiración, idempotencia y correlación.
- `whatsapp_flow_exchange`: replay seguro de cada solicitud.
- `whatsapp_commerce_outbox`: mensajes posteriores idempotentes.
- `whatsapp_commerce_event`: embudo sin PII por correlación, pantalla y resultado.

Los eventos no almacenan nombres, teléfonos, direcciones, tokens, texto de mensajes ni contenido del carrito. Las respuestas de replay y los webhooks eliminan `flow_token` antes de persistir; el token de cierre solo se reconstruye desde la solicitud cifrada actual. Precios y totales del estado del Flow nunca son autoridad: catálogo, stock, promoción, cobertura, sucursal y total se resuelven nuevamente en el backend.

La creación del pedido participa en la transacción externa del Flow: pedido, sesión y outbox se confirman juntos. Los índices únicos separan conversaciones históricas por sede de conversaciones por canal.

Meta no ofrece una clave de idempotencia de cliente para `/messages`. La outbox deduplica el evento local y reintenta rechazos explícitos transitorios. Un timeout o un envío interrumpido queda en `failed` con revisión requerida, sin repetir ciegamente un mensaje posiblemente entregado. La conciliación automática de estos casos sigue pendiente.

## Despliegue seguro

1. Ejecutar `SenorArroz.Infrastructure/Scripts/add_whatsapp_tenant_flow.sql` en PostgreSQL antes del backend.
2. Desplegar API, panel y storefront con `WhatsAppFlow:Enabled=false` y `flow_enabled=false`.
3. Generar una clave RSA dedicada. Guardar clave privada y passphrase únicamente como `WHATSAPP_FLOW_PRIVATE_KEY` y `WHATSAPP_FLOW_PRIVATE_KEY_PASSPHRASE` en Railway.
4. Registrar solo la clave pública en Meta y configurar el endpoint con el `public_id` del canal.
5. Cargar `storefront-flow.json`, validar el health check cifrado y publicar el Flow en Meta.
6. Guardar el `FlowId`, verificar el canal y probar con la allowlist interna. `WhatsAppFlow:RestrictToAllowlist` es `true` por defecto; `AllowedPhoneHashes` contiene SHA-256 hexadecimal del celular colombiano normalizado de diez dígitos. Una lista vacía impide enviar Flows.
7. Activar primero `WhatsAppFlow:Enabled` en Railway y después `flow_enabled` desde el panel, sin nuevo despliegue.

Rollback del Flow: apagar `flow_enabled`; el clasificador deja de enviarlo. Para revertir también el canal central, apagar `is_active` y conservar habilitada la configuración histórica por sucursal.

## Métricas

`whatsapp_commerce_event` registra `flow_started`, `screen_reached`, `validation_error`, `human_transfer`, `checkout_created`, `payment_approved`, resultados fallidos y `order_created`. La duración y el abandono se calculan con `created_at`, `completed_at`, `expires_at` y `status` de la sesión.

## Límites

- Sesión: dos horas y recotización al reanudar.
- Carrito: máximo 30 productos distintos y cantidades entre 1 y 50.
- Debe existir al menos un arroz o combo.
- Una identidad telefónica ambigua nunca expone direcciones guardadas.
- Fuera de cobertura no se crea pedido; se permite cambiar dirección, elegir recogida o solicitar asesor.
- Consultas autónomas de pedidos activos, horarios, cobertura y fidelización quedan fuera de v1.
- Imágenes: JPEG/PNG de Firebase de hasta 100 KB, convertidas a base64 y cacheadas; otras imágenes se omiten. Falta preparar miniaturas para las imágenes que superen ese límite.

## Validaciones pendientes antes de producción

- Homologación de la navegación atrás y del recorrido real en Meta, domicilio y pagos; la validación estática del JSON ya pasó.
- Homologar el reintento de Wompi desde el chat y conciliar envíos con resultado incierto.
- Completar pruebas de enrutamiento entre sedes sobre PostgreSQL.
- Completar la prueba real en atención humana e IA apagada después de desplegar el inicio independiente.
- Corregir y verificar la conexión en vivo de la bandeja desde una sesión administrativa autenticada.

## Validación del 3 de septiembre de 2026

- PostgreSQL local: inicializador completo desde cero, script puntual aplicado dos veces, rollback atómico y doble confirmación concurrente sin duplicar pedido ni outbox.
- Meta: borrador `Señor Arroz - Compra central v1`, ID `1639196854522768`, cuenta `111353392041880`; JSON guardado con cero errores tras corregir la cantidad inicial numérica y retirar `max-chars` de `TextArea`.
- Producción: SQL aplicado y backend desplegado por el usuario; canal central y Flow permanecen desactivados.
- App vinculada: `634937629322620`. Clave pública registrada con firma `VALID` y correspondencia comprobada con la privada de Railway.
- Endpoint registrado en Meta: `/api/whatsapp/flows/0e5b8c3c-bc5e-4922-8c3f-6da7a9a3a454/data-exchange`. La primera prueba HTTP detectó un `400` por nombres del sobre JSON; se corrigió el DTO para aceptar `encrypted_aes_key`, `encrypted_flow_data` e `initial_vector`, con prueba de regresión.
- El borrador continúa sin publicar; la comprobación cifrada en producción debe repetirse tras desplegar esta corrección.
- `reintentar pago` reutiliza o crea un intento dentro de los quince minutos originales; no amplía el vencimiento del checkout de WhatsApp.

## Actualización del 4 de septiembre de 2026

- Flow `1639196854522768` publicado en Meta y habilitado con allowlist interna. La publicación no significa que la homologación completa haya terminado.
- Health check cifrado de producción: HTTP 200 con estado `active`. Campo `flows` suscrito en la app correcta.
- App Secret corregido tanto en Santander como en el canal central; Meta aceptó la autenticación de la app y los mensajes entrantes reales quedaron persistidos con webhooks HTTP 200.
- La dependencia accidental entre atención Humana y envío del Flow se elimina mediante las reglas de «Inicio sin IA». No requiere modificar el Flow publicado ni el esquema de PostgreSQL.
