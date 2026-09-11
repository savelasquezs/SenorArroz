# WhatsApp Ordering Flow V2 / V3

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
- [x] Contrato corregido tras validación de Meta: grafo de rutas acíclico y `quantity` numérico.
- [x] Flow V2 creado como borrador en Meta sin modificar el Flow V1.
- [x] Las 10 pantallas renderizadas en el preview de Meta con 0 errores de esquema.
- [x] URI, número, clave pública y app de Meta conectados; comprobación de estado aprobada.
- [x] Migración de tokens aplicada en PostgreSQL productivo.
- [x] Backend y panel desplegados; backend V2 activo en Railway.
- [x] Flow V2 publicado y configurado para el número de prueba manteniendo V1 intacto.
- [x] Navegación real verificada hasta pago con la sede Santander disponible por horario.

## Correcciones focalizadas de V3 del 10 de septiembre

- [x] Conservar la ruta V3 existente y registrar el historial real de pantallas visitadas.
- [x] Corregir `BACK` desde `FULFILLMENT` y desde cada paso previo sin introducir ciclos en el JSON de Meta.
- [x] Abrir cada nueva invitación en `HOME` y mantener la sesión activa disponible mediante `Continuar pedido`.
- [x] Geocodificar direcciones nuevas con ciudad, departamento y país antes de validar que pertenezcan a Medellín, Bello o Copacabana.
- [x] Alinear el cross-selling de WhatsApp con storefront: Coca-Cola y papas por porciones, solo para los roles faltantes.
- [x] Omitir papas en carritos compuestos únicamente por combos y omitir cualquier rol que el cliente ya haya agregado.
- [x] Validar regresión completa: 658 pruebas aprobadas, 3 pruebas PostgreSQL omitidas y 0 fallos.
- [ ] Desplegar el backend corregido y validar la experiencia en el Flow V3 publicado sin confirmar un pedido real.

## Correcciones de la prueba real del 10 de septiembre

- [ ] Eliminar los errores de transición en Combos, Bebidas y recetas como Ropa vieja.
- [ ] Permitir agregar, editar y quitar arroces, bebidas y adiciones sin salir del carrito ni crear ciclos inválidos en Meta.
- [ ] Permitir volver o cancelar la selección de productos opcionales sin perder el carrito.
- [ ] Corregir la validación y el envío de dirección nueva.
- [ ] Verificar efectivo y pago en línea hasta el resumen, sin crear un pedido durante la prueba.
- [ ] Añadir la acción visible `Empezar de cero` cuando exista un pedido en curso.
- [ ] Añadir un menú inicial conversacional con `Hacer pedido`, `Continuar pedido`, `Ver carta` y `Hablar con un asesor`.
- [ ] Reutilizar la carta ya cargada en el sistema y conservar la atención por chat fuera del Flow.
- [ ] Probar todas las categorías y los productos disponibles del catálogo real.

## Pendiente de operación

- [ ] Publicar la revisión del Flow V2 manteniendo la allowlist actual.
- [ ] Validar navegación real completa, incluidos `INIT`, `BACK`, reapertura, reinicio y recuperación.
- [ ] Ejecutar 30 recorridos técnicos consecutivos y pruebas Android/iOS.
- [ ] Ejecutar las dos pruebas de usabilidad con adultos mayores.
- [ ] Reiniciar únicamente el contexto de prueba y lanzar al grupo controlado.
- [ ] Vigilar métricas durante 48 horas antes de retirar la allowlist.
