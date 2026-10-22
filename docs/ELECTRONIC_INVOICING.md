# Facturación electrónica

## Propiedad y permisos

Cada integración, destinatario y documento pertenece a un tenant. Superadmin administra las integraciones compartidas entre sucursales. Admin y Cashier operan únicamente sobre pedidos autorizados por `OrderAccessRules`; solo Admin y Superadmin autorizan notas crédito. Los destinatarios pertenecen al cliente global del tenant, no a su sucursal histórica.

## Instalación

Aplicar `SenorArroz.Infrastructure/Scripts/add_electronic_invoicing.sql` con el propietario del esquema antes de desplegar. El script es aditivo e idempotente y está incluido en `local-init-completo.sql` raíz. Las tres tablas nuevas necesitan los mismos permisos de tablas y secuencias del rol runtime existente, que debe continuar siendo `NOSUPERUSER NOBYPASSRLS`.

Las credenciales utilizan `Integrations__EncryptionKey`, el protector existente de integraciones. El token se proporciona desde Configuración → Factura electrónica; nunca se devuelve al navegador. El estado inicial es inactivo y el ambiente inicial es pruebas.

Completar en Plemsi/DIAN el TestID de habilitación, firma digital y asociación de la resolución. Solicitar a Plemsi un token para el NIT emisor. Guardar NIT sin DV, razón social y correo empresarial, probar el token y verificar que corresponde al mismo NIT. Consultar resoluciones y registrar prefijo, consecutivo inicial disponible, límite y vencimiento; usar un rango dedicado a este sistema. Registrar también prefijo y consecutivo de notas crédito.

La declaración de preparación para producción confirma la habilitación del emisor, firma digital y resolución en Plemsi/DIAN. No activa automáticamente la integración. La primera emisión real requiere una venta legítima y confirmación del usuario. El tratamiento fiscal implementado es sin IVA ni INC: no habilitar negocios con otro tratamiento hasta parametrizar y validar sus impuestos.

La API pública no incluye ejemplos de las respuestas de emisión. El adaptador exige `IsValid`/`is_valid` explícito y CUFE/CUDE para considerar emitido el documento. Una respuesta HTTP exitosa sin esos datos queda por verificar, nunca emitida. La validación de contratos reales requiere las credenciales del tenant; las pruebas automatizadas del proveedor usan respuestas controladas.

## Operación

Los accesos a Datos de facturación comparten el mismo modal desde el cliente, el sidebar y el pedido. Guardar datos no emite documentos. Un cliente puede conservar varios destinatarios y uno predeterminado. El correo es opcional; sin él se descarga/imprime el PDF. No se exigen RUT, dirección ni responsabilidades fiscales del comprador.

El POS envía `electronicInvoice` solo tras confirmación explícita. El campo nulo se omite al serializar en backend para preservar fingerprints legacy. Pedido y solicitud se confirman en una misma transacción; el worker realiza las llamadas externas después del commit. Reservas y pedidos programados esperan a `Delivered`. No se factura automáticamente a consumidor final.

`electronic_invoice_document` conserva el trabajo persistente: lease con token de concurrencia, próxima consulta, intentos y respuesta. El worker procesa tenants activos en scopes independientes. Un lease vencido se recupera; un documento que ya comenzó transmisión solo se consulta por el mismo prefijo/consecutivo. Un 404 posterior a un timeout no autoriza retransmisión ni otro consecutivo. Los rechazos explícitos pueden corregirse y reenviarse con el mismo número. El resultado desconocido requiere reconciliación con Plemsi.

Los consecutivos se asignan bajo bloqueo transaccional. El emisor, ambiente, resolución, prefijos y contadores de una integración utilizada no se cambian desde la configuración. Para otra numeración se registra una nueva integración. Las solicitudes existentes conservan su integración original aunque se active otra.

Los triggers protegen cambios monetarios de pedidos pendientes, transmitidos o emitidos, incluso por canales distintos del POS. Los documentos fiscales impiden eliminar definitivamente el pedido. Cancelarlo operativamente sigue permitido: el worker deja la factura pendiente de autorización de nota crédito. La nota crédito total validada libera la corrección del pedido; después puede solicitarse una factura nueva. No modifica ni borra el documento original.

Los archivos PDF/XML se conservan como bytes privados en la fila tenant-owned; se descargan exclusivamente por endpoints autorizados. El estado fiscal es independiente del correo. El envío propio usa la cola EmailOutboxMessage y Resend después de validar el documento. Plemsi recibe siempre send_email=false y no se llama a su envío de correo. accepted se conserva solo para entregas históricas de Plemsi; sent indica aceptación de Resend, no entrega al buzón. Un error de archivos/correo no vuelve a emitir. El correo del tenant queda como Reply-To y el remitente usa una dirección verificada.

La descarga XML de Plemsi devuelve `data.AttachedDocumentB64` y `data.Filename`; se decodifica el AttachedDocument completo, con la factura y validación DIAN. PDF también admite `data` como cadena base64. Un archivo pendiente no cambia una factura validada a rechazada ni autoriza otra emisión.

## Endpoints

Base: `/api/electronic-invoicing`.

- `GET availability`; `GET/POST integrations`; `POST integrations/{id}/test|resolutions|email|activation`.
- `GET/POST customers/{customerId}/recipients`; `GET recipient-lookup?type=...&identification=...`.
- `GET/POST orders/{orderId}` para historial y solicitud.
- `POST documents/{id}/retry|email|credit`; `GET documents/{id}/pdf|xml`.

## Validación

`dotnet test --filter FullyQualifiedName~ElectronicInvoicing` ejecuta reglas, contratos controlados y aislamiento EF. Las pruebas PostgreSQL crean y eliminan bases/roles exclusivos; habilitarlas con `INVOICE_TEST_POSTGRES` apuntando a un servidor local de pruebas cuyo usuario pueda crear bases y roles. Nunca usar producción. Sin esa variable las pruebas PostgreSQL se reportan omitidas.

Comprobar script dos veces, RLS con rol no propietario, FKs compuestas, concurrencia, timeout sin retransmisión, reserva entregada, nota crédito y bloqueo de importes. Frontend: `npm run test:run` y `npm run build`.

## Revisión funcional y de interfaz

La configuración empresarial se abre en `/settings/electronic-invoicing`, exclusiva de Superadmin. La entrada antigua de sucursal enlaza a ella. Conexión, emisor, numeración y activación están agrupados con indicadores de preparación; los cambios sin guardar deshabilitan consultas y activación.

Si la consulta de disponibilidad falla al crear el pedido, se ofrece reintentar, volver o continuar con Solo pedido. Esta última acción omite la solicitud fiscal explícitamente. No se transmite un documento por un fallo de consulta. Los datos obligatorios se validan antes de sugerir emisión.

`ElectronicInvoicing:ProductionEnabled` es false por defecto en appsettings. Bloquea la activación de producción, nuevas solicitudes, la asignación de números pendientes y las mutaciones HTTP de Plemsi en producción. Las consultas de documentos previamente transmitidos siguen disponibles para reconciliación. No establecerlo a true hasta validar los contratos reales y recibir autorización de activación.

La activación consulta de nuevo resoluciones y folios. `InvoiceNumbering` normaliza las respuestas reconocidas y comprueba que la configuración pertenece a un único rango coincidente y vigente. El siguiente número se obtiene de `next_number` o de un consecutivo actual explícito; no se deduce del inicio del rango ni de un conteo genérico de folios. Si el contador es nulo, requiere confirmación manual de Superadmin descrita abajo; un rango desconocido sigue bloqueando la activación. La numeración reservada para este sistema sigue siendo obligatoria.

El detalle muestra el snapshot del destinatario del documento, fecha de validación y estado independiente de correo. Sin correo no aparece reenvío. CUFE, XML y nota crédito están en detalles secundarios. La solicitud de nota crédito no autoriza corregir importes hasta su validación.

BaseDialog administra el foco, Escape, Tab, diálogos anidados y restauración del foco. DialogHost reutiliza este comportamiento. Los datos tributarios y disponibilidad comparten consultas por sesión mediante Pinia; las respuestas de una sesión anterior se descartan. La edición actualiza los accesos abiertos sin otra consulta. La consulta periódica del detalle se pausa con la pantalla oculta.

Pruebas locales con Docker: PostgreSQL 17, puerto publicado solo en 127.0.0.1, contenedor sin volúmenes persistentes. Establecer `INVOICE_TEST_POSTGRES` con la conexión de ese contenedor antes de `dotnet test --filter FullyQualifiedName~ElectronicInvoicing`. Las pruebas crean bases y roles temporales, comprueban RLS con rol NOBYPASSRLS y eliminan únicamente sus bases y roles. No usar credenciales Plemsi reales para estas pruebas automatizadas.

## Publicación autorizada 2026-10-22

Producción reconoce el NIT en `data.dni.number` y los folios en `lastNumberUsed` o `data[].invoice.number`. Un contador nulo permanece desconocido; no se transforma automáticamente en 1. Superadmin confirma en el modal el número disponible verificado en DIAN y en su proveedor anterior y el uso exclusivo de la serie. La activación registra la confirmación en `resolutions_json`, junto con actor, fecha y respuesta consultada. Si Plemsi informa un consecutivo, se respeta su piso incluso con confirmación manual.

Aplicar primero el SQL idempotente, incluidos permisos de las tres tablas y secuencias para `senorarroz_runtime`. Habilitar `ElectronicInvoicing__ProductionEnabled=true` en Railway permite configurar y activar producción; esto no activa ninguna integración ni solicita documentos por sí solo. La emisión sigue requiriendo integración activa y confirmación del usuario. El valor de appsettings permanece false para instalaciones nuevas.

## PDF personalizado y correo propio

Aplicar `SenorArroz.Infrastructure/Scripts/add_invoice_delivery.sql` antes de publicar este cambio. Está incluido al final del inicializador SQL raíz. El script añade metadatos de PDF y entrega, deduplicación por tenant y lease del outbox. Fuerza RLS del outbox y comprueba que su referencia fiscal pertenezca al mismo tenant.

El PDF se genera con PDFsharp y fuente Vera embebida (licencia incluida), compatible con Linux sin tipografías del host. Lee exclusivamente el XML validado y comprueba número, CUFE/CUDE, emisor y total. Incluye QR original y CUFE/CUDE en cada página, numeración, vigencia, pagos separados y totales. Un PDF anterior se conserva en provider_pdf. Descargar una copia histórica regenera su representación sin modificar el XML ni enviar un mensaje.

Los descuentos comerciales de nuevas facturas se aplican al producto no regalado de mayor valor bruto, continuando con los siguientes si fuera necesario. No se altera el pedido. Los precios netos evitan porcentajes fraccionarios; se conserva descuento 100% para regalos/líneas totalmente descontadas. Si dividir por cantidad pierde centavos, price_amount y base_quantity conservan la proporción exacta. Se guardan fiscalLines y discountPolicy en el snapshot. Las facturas aceptadas conservan siempre su distribución original.

ResendSettings__ApiKey y ResendSettings__FromEmail deben estar configurados; la dirección debe pertenecer a un dominio verificado en Resend. El nombre es el emisor fiscal, Reply-To es BillingEmail del snapshot. No se suplanta un dominio del tenant que no esté verificado. Cada mensaje adjunta el PDF propio y el AttachedDocument XML original, comprobados por hashes SHA-256 congelados en metadata_json. Una configuración incompleta deja el correo pendiente/fallido y la factura sigue emitida.

La cola confirma una sola solicitud inicial bajo bloqueo de fila. El worker reclama cada mensaje de forma atómica y envía con clave estable `email-outbox-{tenant}-{id}`. Guarda el id de Resend y estados queued/sent/failed/verifying independientes del estado DIAN. Los reintentos de transporte conservan archivos, destinatario y clave; después de 23 horas desde el primer intento o de agotar resultados ambiguos se detienen en verifying. No reactivar ni crear otro mensaje hasta consultar el envío en Resend: sus claves caducan a las 24 horas. Un 4xx definitivo permite corregir configuración y solicitar reenvío explícito. Los correos históricos accepted de Plemsi no se encolan automáticamente.

El reenvío requiere confirmación en la interfaz, reutiliza una solicitud en curso y consolida dobles clics y peticiones dentro de un minuto tras el envío. Nunca crea otra factura. No existe reenvío de correo por Plemsi. La aceptación del proveedor de correo no afirma entrega al buzón; no se simulan estados de entrega sin evidencia de Resend.

Para revisión visual opcional de pruebas, INVOICE_PDF_SAMPLE indica la carpeta de salida e INVOICE_PDF_SOURCE_XML un AttachedDocument local ya validado. No se transmite nada al generar esas copias. Los tests de concurrencia y RLS requieren INVOICE_TEST_POSTGRES con una base Docker aislada.
## Identidad del restaurante
Aplicar add_invoice_branding.sql antes de publicar. Superadmin configura nombre comercial y logo (PNG/JPG, máximo 500 KB y 3000 × 3000) desde la pantalla empresarial, independientemente del proveedor activo. Las versiones son inmutables, con RLS y FK compuesta hacia el documento. Nuevas solicitudes requieren identidad. PDF versión 2 muestra logo y nombre comercial en cada página, manteniendo razón social/NIT del XML. El correo lleva el mismo nombre y logo CID; se comprueba su hash junto a PDF/XML. No se regenera un PDF referenciado por un outbox propio, para conservar sus adjuntos. Las copias históricas sin entrega propia pueden actualizar presentación sin emitir ni reenviar. Las pruebas visuales opcionales admiten INVOICE_PDF_SOURCE_LOGO e INVOICE_PDF_DISPLAY_NAME.
