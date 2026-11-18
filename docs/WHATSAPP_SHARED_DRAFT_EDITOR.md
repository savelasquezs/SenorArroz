# WhatsApp: borrador compartido y editor integrado

Implementación local de las etapas 4 y 5. No habilita Coexistence ni cambia despliegues o credenciales de Meta.

## Persistencia y confirmación

`whatsapp_commerce_session` es el intento comercial compartido por IA, Flow y asesor. Su `correlation_id` identifica el intento y su `idempotency_key` también identifica los pedidos o checkouts de Storefront. Un índice permite un solo intento activo por conversación y tenant. Vencer el token de Flow no descarta el borrador.

`draft_json` conserva el contrato comercial de `CreateOrderDto`; `editor_json` conserva los detalles de edición del POS, incluidos pagos, beneficios, programación y facturación. `version` rechaza guardados obsoletos. `order_id`, `checkout_id` y `commercial_status` permiten distinguir el pedido confirmado de su mensaje pendiente. Las notas internas se guardan en `internal_notes_json` y nunca entran a la cola de Meta.

Las mutaciones comprueban acceso al chat, responsable y revisión de atención. La operación toma un bloqueo transaccional por tenant/conversación y bloquea la fila del chat. La confirmación vuelve a revisar catálogo, stock, cobertura `AddressBranch`, domicilio, pagos y elegibilidad de beneficios, y exige la huella de la revisión presentada al asesor. Usa `CreateOrderHandler`; no crea un motor de pedidos alternativo. Las confirmaciones simultáneas recuperan el pedido ganador. Los conflictos de edición requieren revisión explícita.

La línea, el responsable y la sucursal de atención permanecen independientes del destino. `OriginBranchId` procede del contexto de trabajo del servidor; `BranchId` determina preparación, inventario, impresión, rutas y cobros. No se modifica `BranchContext` ni se trasladan pagos entre bancos. El detalle confirmado reutiliza `OrderDetailContent` y sus controles de gestión remota/versiones.

IA puede leer el borrador compartido y Flow conserva su carrito en el mismo intento. Un borrador editado por asesor exige continuar su edición con un asesor: las herramientas de IA y las pantallas de Flow no pueden reemplazar pagos, beneficios o programación que sus contratos no representan. No existe una confirmación independiente de IA que eluda el intento compartido.

Un nuevo pedido requiere una acción explícita y una clave nueva. Los intentos anteriores permanecen en el historial. Un checkout vigente o sujeto a revisión bloquea otro intento; un pago vencido puede dar paso a un pedido nuevo. Los resultados inciertos de Meta requieren revisión y no se reenvían automáticamente como si fueran rechazos seguros.

## Contrato

- `GET /api/whatsapp/conversations/{id}/commercial-draft`: intento actual; abrir una consulta no inicia una venta.
- `POST /api/whatsapp/conversations/{id}/commercial-draft`: iniciar o recuperar el intento activo; crear otro después del cierre exige la versión del intento anterior.
- `PUT /api/whatsapp/conversations/{id}/commercial-draft`: autoguardado con `sessionId`, `expectedVersion`, `attentionVersion`, `order`, `editor` y estado comercial.
- `POST /api/whatsapp/conversations/{id}/commercial-draft/{operation}`: `quote`, `confirm`, `cancel`, `note` o `retry-message`. `confirm` exige `reviewedFingerprint`.
- `GET /api/whatsapp/conversations/{id}/commercial-history`: últimos 30 intentos de la conversación autorizada.

## Componentes reutilizados

`OrderSidebar` recibe el contexto proporcionado por `useOrderEditor`, con identificador de borrador explícito. El store del POS sigue usando sus pestañas; cada conversación tiene un contexto propio sin su límite de cinco pestañas. El autoguardado captura conversación, versión y revisión de atención antes de cada solicitud y conserva cambios locales ante conflictos.

Se reutilizan `CustomerSection`, selectores de cliente/dirección/sucursal, `OrderItemList`, `AddProductModal`, `PaymentSelector`, beneficios, programación, facturación, `OrderNoteDialog`, `BaseDialog` y `OrderDetailContent`. No se duplica el editor ni se cambia la pestaña activa del POS. `createOrderDraft` comparte la construcción inicial del borrador.

El panel muestra línea, responsable, atención, destino, guardado, total, requisitos y pedido tomado. El estado del mensaje se consulta mientras esté pendiente. Las bandejas agrupan acciones, espera y gestionados; una nueva consulta vuelve a requerir atención sin abrir otro intento. El historial conserva posición de scroll y muestra mensajes nuevos cuando el asesor está arriba.

## Instalación y límites de validación

Aplicar `add_whatsapp_shared_draft.sql` después del esquema de comercio de WhatsApp y de `add_whatsapp_coexistence_attention.sql`, antes de desplegar código que requiera sus columnas. El script es idempotente y se detiene si encuentra varios intentos activos que deben reconciliarse. No se ejecutó contra laboratorio ni producción.

Las pruebas usan PostgreSQL desechable en loopback y mocks de Meta, almacenamiento y notificaciones. No se enviaron mensajes reales. La aprobación de Meta, el piloto con la línea real y la comprobación visual en dispositivos siguen siendo comprobaciones operativas posteriores.
