# WhatsApp Ordering Flow V2

## Implementado

- [x] Navegación `CATEGORY -> PRODUCT_GROUP -> PRODUCT_VARIANT -> CART -> FULFILLMENT`.
- [x] Variantes con precio, disponibilidad y número de personas desde `PublicCatalogDto`.
- [x] Carrito editable y recomendaciones reutilizables, máximo tres.
- [x] Dirección guardada, dirección nueva, confirmación normalizada y recogida en modos separados.
- [x] Beneficios omitidos cuando la cotización no devuelve opciones.
- [x] Cotización estructurada, huella de caché y revalidación obligatoria al confirmar.
- [x] Sesión multiinvitación con hashes y una sola clave de idempotencia.
- [x] `INIT`, `data_exchange`, `BACK`, replay, expiración y concurrencia con recuperación cifrada.
- [x] Campos completos `address_summary_text`, `cart_subtotal_text`, `order_summary_text` y `error_message`.
- [x] Eventos sin PII y diagnóstico reciente por versión, pantalla, categoría y correlación.
- [x] Una conexión SignalR compartida por hub y configuración explícita del hub de WhatsApp.
- [x] JSON parseable, referencias `${data.campo}` declaradas y 10 pantallas V2 verificadas localmente.
- [x] Backend compilado; 645 pruebas aprobadas y 3 pruebas PostgreSQL omitidas por falta de conexión externa.
- [x] Frontend compilado; 389 pruebas aprobadas, incluidas 6 de conexión SignalR compartida.

## Pendiente de operación

- [ ] Ejecutar `add_whatsapp_commerce_session_tokens.sql` en PostgreSQL antes de desplegar el backend.
- [ ] Desplegar backend y panel con la allowlist actual y `FlowEnabled` controlado.
- [ ] Crear un Flow nuevo en Meta, cargar y validar `storefront-flow.json` sin alterar el Flow V1 publicado.
- [ ] Validar endpoint cifrado, preview y navegación `BACK` en Meta.
- [ ] Ejecutar 30 recorridos técnicos consecutivos y pruebas Android/iOS.
- [ ] Ejecutar las dos pruebas de usabilidad con adultos mayores.
- [ ] Cambiar el Flow ID, reiniciar únicamente el contexto de prueba y lanzar al grupo controlado.
- [ ] Vigilar métricas durante 48 horas antes de retirar la allowlist.
