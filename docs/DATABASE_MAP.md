# Señor Arroz Backend - Database Map

Este mapa existe para que Codex ubique rápido las entidades y entienda cómo prepararlas para SaaS multitenant.

## Estado de aislamiento v2

- Inventario EF: 109 entidades; `Tenant` global y 108 tenant-owned.
- Contrato común: `ITenantOwned` y `TenantOwnedEntity`.
- EF aplica centralmente `tenant_id` obligatorio, índice, concurrency token y query filter.
- `SaveChanges` asigna el tenant actual en altas sin tenant y rechaza altas, cambios o borrados cross-tenant.
- El esquema puntual se aplica con `SenorArroz.Infrastructure/Scripts/multitenant_isolation_v2.sql`, incluido al final de `local-init-completo.sql`.
- La matriz por dominio, estrategia de backfill y workers está en `docs/MULTITENANT_ISOLATION_AUDIT.md`.
- `tenant_scoped_unique_indexes_v3.sql` corrige claves funcionales cuyo namespace es empresarial.
- `enable_multitenant_rls_v3.sql` habilita y fuerza RLS; `verify_multitenant_runtime_role.sql` valida el rol de ejecución.

## Decisión confirmada

Todo el negocio será `tenant-owned`.

Eso significa que, salvo tablas puramente técnicas o de infraestructura global, cada tabla funcional debe pertenecer a un tenant.

```text
Una sola base de datos PostgreSQL
+ todas las tablas del negocio con TenantId
+ filtros globales en EF Core
+ índices compuestos por tenant
```

## Clientes globales por tenant (fase aditiva)

- `Customer` pertenece al tenant; `branch_id` conserva únicamente la sucursal de origen.
- `CustomerPhone` es la identidad telefónica, única por `(tenant_id, phone_normalized)` tras el merge.
- `Address` describe la ubicación física; `AddressBranch` contiene barrio, tarifa y cobertura por sucursal.
- `Order.customer_id`, `Order.address_id` y `Order.delivery_fee` siguen siendo referencias/snapshots históricos.
- `customer.phone1/phone2` y `address.neighborhood_id/delivery_fee` permanecen temporalmente para compatibilidad.
- Los scripts `customer_tenant_01` a `customer_tenant_08` se ejecutan en orden y separan precheck, merge y postcheck.

## Fuente principal

Desarrollo móvil local: `senorarroz-delivery-postgres` contiene `senor_arroz_delivery_dev`
en `127.0.0.1:15433`; el rol `delivery_local` no es propietario ni tiene bypass RLS.
`scripts/start-delivery-local.ps1` carga su conexión desde User Secrets.
El inicializador raíz incorpora `complete_local_development_prerequisites.sql`
antes del aislamiento y las ampliaciones recientes después. El ajuste puntual
`add_whatsapp_ai_context_columns.sql` agrega únicamente campos ya usados por el modelo,
sin ejecutar el script destructivo de reinicio de WhatsApp.

DbContext:

```text
SenorArroz.Infrastructure/Data/ApplicationDbContext.cs
```

Configuraciones EF Core:

```text
SenorArroz.Infrastructure/Data/Configurations
```

Repositorios:

```text
SenorArroz.Infrastructure/Repositories
```

## Inventario operativo

- `ExpenseUnitConversion`: presentaciones compradas y equivalencia en unidad base.
- `ProductExpenseRequirement`: receta vigente estimada o estricta del producto.
- `InventoryBalance`: saldo y costo promedio único por tenant, sucursal e insumo.
- `InventoryMovement`: ledger inmutable e idempotente de compras, reservas, consumos, ajustes, mermas, transferencias y reversos; expone el pedido, compra, transferencia o conteo que originÃ³ cada movimiento.
- `OrderInventoryAllocation`: snapshot histórico de receta por pedido.
- `InventoryCount` / `InventoryCountLine`: conteo físico y diferencia histórica.
- `InventoryTransfer` / `InventoryTransferLine`: despacho y recepción entre sucursales del mismo tenant.
- Script de despliegue: `SenorArroz.Infrastructure/Scripts/add_inventory_core.sql`, incluido en `local-init-completo.sql`.

## Entidad raíz actual

### Tenant

Es la raíz SaaS desde el Bloque 1 de multitenancy.

Relación esperada:

```text
Tenant 1 - N Branch
Tenant 1 - N User
Tenant 1 - N Customer
Tenant 1 - N Product
Tenant 1 - N Order
```

Campos sugeridos:

```text
Id
Name
Slug
IsActive
CreatedAt
UpdatedAt
```

Campos estructurales implementados adicionalmente:

```text
PublicId
Status (draft, active, suspended, cancelled)
StatusReason
AccessVersion
ContactName
ContactEmail
ContactPhone
LegalName
TaxId
BillingAddress
SuspendedAt
CancelledAt
```

`IsActive` permanece como compatibilidad legacy y debe mantenerse sincronizado con `Status`. La autenticación exige ambos habilitados. El tenant inicial `1` usa el `PublicId` estable `86ae3576-33db-483a-9a83-d9087a660651`, `Status = Active` y `AccessVersion = 1`.

## Clasificación de tablas

### Núcleo organizacional

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Branch | Sí | Tenant inicial `1` |
| BranchBusinessHour | Sí | Desde `Branch.TenantId` |
| BranchPrintSettings | Sí | Desde `Branch.TenantId` |
| BranchInformalLoan | Sí | Desde `Branch.TenantId` |
| BranchInformalLoanPayment | Sí | Desde `BranchInformalLoan.TenantId` |
| BranchInformalLoanExemptOrder | Sí | Desde Branch/Order según relación |
| User | Sí | Desde `Branch.TenantId` si tiene sucursal; si no, tenant inicial |
| RefreshToken | Sí | Desde `User.TenantId` |
| PasswordResetToken | Sí | Desde `User.TenantId` |
| UserDeviceToken | Sí | Desde `User.TenantId` |

Estado del Bloque 1:

- `branch.tenant_id` y `user.tenant_id` son obligatorios y referencian `tenant`.
- `(user.tenant_id, user.branch_id)` referencia `(branch.tenant_id, branch.id)` y evita cruces entre tenants.
- `refresh_token`, `password_reset_token` y `user_device_token` usan `(tenant_id, user_id)` contra `user`.
- Login y refresh cargan el tenant y rechazan tenants inactivos, suspendidos, cancelados o inconsistentes.
- El script idempotente `SenorArroz.Infrastructure/Scripts/multitenant_core_v2.sql` hace backfill, ejecuta prechecks y luego instala NOT NULL, índices y FKs.

- `Branch.IsActive` (`branch.is_active`) determina si la sucursal participa en el storefront público. Se instala con `SenorArroz.Infrastructure/Scripts/add_branch_active_storefront.sql` y su valor por defecto es `true`.
- `Branch.StorefrontTakenByUserId` (`branch.storefront_taken_by_user_id`) identifica el usuario técnico activo de la misma sede con el que se crean pedidos web directos.
- `BranchBusinessHour` se persiste en `branch_business_hour`, con una fila única por `(branch_id, day_of_week)`; el panel administrativo, el catálogo público y la validación previa a WhatsApp comparten esta fuente.
- El storefront público actual es una excepción single-tenant: no debe habilitarse para otro negocio hasta que productos, categorías, promociones y sucursales se filtren obligatoriamente por `tenant_id` resuelto en servidor.

Notas:

- `Branch` debe ser la primera tabla del negocio en recibir `TenantId`.
- `BranchInformalLoanPayment` conserva cada abono con saldo anterior/posterior, actor y vínculo opcional a `ExpenseHeader`; el historial no se sobrescribe al reducir el saldo del préstamo.
- `BranchInformalLoan.bank_id` es opcional y referencia un banco del mismo tenant; `null` significa efectivo. `CashClosureBankReconciliation.informal_loan_deduction` conserva la deducción acumulada absorbida por cada cierre.
- `Branch` contiene la configuración operativa del seguimiento de domiciliarios: hora local de cierre, frecuencias, permanencias, tolerancia geográfica y retenciones.
- `User` debe quedar asociado a tenant aunque sea superadmin. Si hay superadmin global, documentar excepción.
- `User.ActiveSessionId` contiene la sesión exclusiva vigente de un domiciliario.
- `User.WebAccessEnabled` (`user.web_access_enabled`) permite excepcionalmente a un domiciliario usar el portal web. Los domiciliarios quedan deshabilitados al crearse o al cambiar a ese rol; los demás roles conservan el valor activo.
- `RefreshToken.SessionId` vincula cada refresh token con la sesión que lo creó. Un token anterior puede conservarse para auditoría, pero deja de ser válido cuando no coincide con `User.ActiveSessionId`.
- El esquema de sesión exclusiva se instala con el script idempotente `SenorArroz.Infrastructure/Scripts/add_exclusive_delivery_sessions.sql`, que debe ejecutarse antes de desplegar el backend que usa estas columnas.
- En Railway, abrir Bash desde la raíz del backend, ejecutar `railway connect MainDatabase` y, dentro de `psql`, ejecutar `\i SenorArroz.Infrastructure/Scripts/add_exclusive_delivery_sessions.sql`.

### Clientes y ubicación

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Customer | Sí | Desde órdenes existentes, sucursal asociada o tenant inicial |
| Address | Sí | Desde `Customer.TenantId` o `Branch.TenantId` |
| Neighborhood | Sí | Tenant inicial o por sucursal si ya existe relación |

Notas:

- `Customer.Phone1` puede ser nulo cuando existe `WhatsAppUsername` o una identidad técnica de WhatsApp asociada.
- `Customer.WhatsAppUserId` almacena el BSUID estable y no se expone para edición; `WhatsAppUsername` es visible, normalizado en minúsculas y con `@`.
- La búsqueda de clientes combina nombre, teléfonos y username. El username no es único porque puede cambiar o reutilizarse.
- Barrios pueden repetirse entre tenants.
- `Address.Label` es una etiqueta libre opcional para identificar la dirección y `Address.NeighborhoodId` puede ser nulo en direcciones creadas desde el storefront.
- Una dirección guardada conserva su `DeliveryFee`; una dirección nueva almacena la tarifa validada al confirmar el pedido.
- `StorefrontCustomerAuthChallenge` es tenant-owned y conserva HMAC del OTP, expiración, intentos, consumo, hash de sesión e IP seudonimizada; nunca persiste el código ni el token en texto plano.

### Productos y fidelización

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| ProductCategory | Sí | Tenant inicial |
| Product | Sí | Desde `ProductCategory.TenantId` o tenant inicial |
| LoyaltyCycleStep | Sí | Desde `Product.TenantId` o tenant inicial |
| DailyPromotion | Sí | Desde `Branch.TenantId` |
| DailyPromotionProduct | Sí | Desde `DailyPromotion.TenantId` |

Notas:

- Productos y categorías NO deben compartirse entre restaurantes.
- `ProductCategory.StorefrontRole` se almacena en `product_category.storefront_role` y admite `rice`, `combo`, `beverage`, `addition` o `hidden`; el valor seguro por defecto es `hidden`.
- `Product.StorefrontVariantLabel` y `Product.StorefrontSortOrder` se almacenan en `storefront_variant_label` y `storefront_sort_order`. La ficha `CommercialProfile` agrupa productos web; si no existe, cada producto se publica como grupo individual.
- El esquema y el backfill explícito del catálogo guiado se instalan con `SenorArroz.Infrastructure/Scripts/add_storefront_guided_catalog.sql` antes de desplegar el contrato agrupado.
- Nombres únicos deben ser por tenant, no globales.
- `DailyPromotion.CreatedByUserId` registra quién creó la promoción para limitar las modificaciones del cajero a sus propios registros. La columna y la eliminación del índice histórico que impedía promociones activas no superpuestas se instalan con `SenorArroz.Infrastructure/Scripts/add_daily_promotion_cashier_permissions.sql` antes de desplegar el backend.

### Pedidos

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Order | Sí | Desde `Branch.TenantId` |
| OrderDetail | Sí | Desde `Order.TenantId` |
| ReservationDeposit | Sí | Desde `Order.TenantId` o `Branch.TenantId` |

Notas:

- `Order` es una de las tablas más sensibles.
- Cualquier query de pedidos debe filtrar por tenant y luego por branch cuando aplique.
- `OrderDetail` debe copiar tenant desde su pedido, no desde frontend.
- `Order.ManualBenefitGrantedAt` se almacena como `timestamp with time zone` porque representa un instante UTC; el ajuste idempotente para instalaciones existentes está en `SenorArroz.Infrastructure/Scripts/fix_manual_benefit_granted_at_utc.sql`.
- `Order.StorefrontIdempotencyKey` evita pedidos duplicados por reintentos del checkout y nunca se acepta como sustituto de la sesión verificada.
- El esquema de OTP, direcciones web y pedidos directos se instala con `SenorArroz.Infrastructure/Scripts/add_storefront_customer_otp.sql` antes de desplegar el API.
- `wompi_payment_attempt.app_payment_id` usa `ON DELETE SET NULL`; el ajuste para instalaciones existentes está en `SenorArroz.Infrastructure/Scripts/fix_wompi_order_deletion.sql`.

### Meta Conversions API: atribución y entrega de Purchase

El mapping reside exclusivamente en `WompiPaymentConfigurations.cs` mediante Fluent API. El script idempotente `SenorArroz.Infrastructure/Scripts/add_meta_conversions_api.sql` debe ejecutarse antes de desplegar el backend.

| Tabla | Campos Meta | Tipo y propósito |
|---|---|---|
| `storefront_checkout` y `payment_notification_outbox` | `meta_consent_granted` | Booleano no nulo, inicialmente `false`; autorización de medición |
| Ambas | `meta_client_user_agent`, `meta_client_ip_address` | Texto opcional de hasta 512 y 64 caracteres |
| Ambas | `meta_fbp`, `meta_fbc` | Identificadores opcionales de hasta 255 caracteres |
| `payment_notification_outbox` | `meta_customer_phone` | Teléfono opcional de hasta 20 caracteres; se transforma con SHA-256 antes de enviarlo a Meta |
| `payment_notification_outbox` | `meta_status` | Estado independiente del envío a cocina: `pending`, `processed`, `failed`, `ignored` |
| `payment_notification_outbox` | `meta_attempt_count` | Número de fallos, inicialmente cero |
| `payment_notification_outbox` | `meta_next_attempt_at`, `meta_processed_at` | Fechas UTC opcionales de reintento y procesamiento |
| `payment_notification_outbox` | `meta_last_error` | Último error, máximo 1000 caracteres |

- Los registros históricos reciben `meta_status = ignored` mediante el valor inicial SQL; los nuevos mensajes creados por la aplicación comienzan en `pending`. No se reenvían compras históricas al activar CAPI.
- `pending -> processed`: Meta acepta Purchase. Se registra la fecha y se limpian error y próximo intento.
- `pending -> ignored`: falta consentimiento, el origen del pedido no es `web` o el tipo de evento no es `order_created_web_cash` / `order_payment_approved`.
- Un fallo transitorio mantiene `pending`, incrementa el contador y programa espera exponencial de 30 a 480 segundos; el décimo fallo cambia a `failed`. Un rechazo permanente cambia directamente a `failed` y registra fecha/error. No hay reintento automático de estados terminales.
- Sin configuración válida de CAPI, el worker conserva los mensajes pendientes. El estado y los reintentos de cocina permanecen independientes.
- El pago aprobado recupera consentimiento y atribución del checkout del mismo tenant. Los pedidos de WhatsApp conservan su origen y quedan excluidos de Purchase web.
- Se conserva el aislamiento mediante `tenant_id` del storefront resuelto en servidor y el filtro de sucursal en diagnósticos. El índice `ix_payment_notification_outbox_meta_pending` cubre estado y fecha; la unicidad existente `(order_id, event_type)` y el identificador estable del evento permiten deduplicación.

### Bancos, apps y pagos

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Bank | Sí | Tenant inicial |
| App | Sí | Tenant inicial |
| AppPayment | Sí | Desde `Order.TenantId`, `App.TenantId` o `Branch.TenantId` |
| BankPayment | Sí | Desde `Order.TenantId`, `Bank.TenantId` o `Branch.TenantId`; `is_app_settlement` marca ingresos bancarios creados al liquidar apps para no duplicar el global de caja |
| BankTransfer | Sí | Desde banco origen/destino o tenant inicial |
| CashRegisterClosure | Sí | Desde `Branch.TenantId` |
| CashClosureBankReconciliation | Sí | Desde cierre de caja |
| CashClosureInformalLoan | Sí | Desde cierre de caja o préstamo |
| CashVaultMovement | Sí | Desde `Branch.TenantId` |

Notas:

- Bancos y apps serán propios de cada tenant.
- No compartir bancos entre clientes, aunque tengan nombres iguales.
- Índices únicos de bancos/apps deben ser por tenant.

### Gastos y proveedores

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Expense | Sí | Desde `Branch.TenantId` o encabezado |
| ExpenseHeader | Sí | Desde `Branch.TenantId` |
| ExpenseDetail | Sí | Desde `ExpenseHeader.TenantId` o `Expense.TenantId` |
| ExpenseCategory | Sí | Tenant inicial |
| ExpenseBankPayment | Sí | Desde `Expense.TenantId` o `Bank.TenantId` |
| ExpenseMenuTarget | Sí | Tenant inicial |
| Supplier | Sí | Tenant inicial |
| SupplierExpense | Sí | Desde `Supplier.TenantId` o `Expense.TenantId` |

Notas:

- `ExpenseDetail.IncludeVat` conserva qué líneas integran la base gravable del IVA 19 % del comprobante. El esquema y el backfill de comprobantes antiguos se instalan con `SenorArroz.Infrastructure/Scripts/add_expense_detail_individual_vat.sql` antes de desplegar el backend correspondiente.

- Categorías de gasto y proveedores pertenecen a cada restaurante.
- Facturas/headers deben ser aislados por tenant.

### Domiciliarios y rutas

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| DeliverymanAdvance | Sí | Desde `User.TenantId` o `Branch.TenantId` |
| DeliverymanDayState | Sí | Desde `User.TenantId` o `Branch.TenantId` |
| DeliverymanLocation | Sí | Desde `User.TenantId` |
| DeliveryWorkSession | Sí | Desde `User.TenantId` o `Branch.TenantId` |
| DeliveryRoute | Sí | Desde `Branch.TenantId` |
| DeliveryRouteStop | Sí | Desde `DeliveryRoute.TenantId` |
| DeliveryRoutingPlan | Sí | Desde `Branch.TenantId` |
| DeliveryRouteProposal | Sí | Desde `DeliveryRoutingPlan` |
| DeliveryRouteProposalStop | Sí | Desde `DeliveryRoutingPlan` y `Order` |
| DeliveryTrackingAlert | Sí | Desde `Branch.TenantId` |

Notas:

- `DeliveryRoutingPlan` conserva una versión incremental y solo admite un plan `active` por sucursal mediante índice parcial.
- P3–P5 agrega `wait_deadline_at_utc` en `delivery_route_proposal` y snapshots `conservative_ready_at_utc`, `preparation_confidence`, `ready_within_window_probability`, `preparation_sample_size` en sus paradas. Script idempotente: `add_routing_prediction_snapshots.sql`; no realiza backfill ni cambia pedidos. `../local-init-completo.sql` crea primero tablas V1, aplica aislamiento y luego los cambios aditivos P2/P3.
- `DeliveryRoute.WorkSessionId` vincula la ruta mediante FK compuesta de tenant, jornada, domiciliario y sucursal. `DepartedAtUtc` y `DepartureSource` conservan salida física observada o confirmada sin reemplazar relojes históricos. El script aditivo `add_routing_operational_session.sql` está incluido en `../local-init-completo.sql`.
- Las propuestas y sus paradas son snapshots operativos; el claim valida nuevamente estado, sucursal, asignación y versión dentro de una transacción serializable.
- El esquema V1 se instala con `SenorArroz.Infrastructure/Scripts/add_delivery_routing_v1.sql` antes de desplegar el API que consulta estas tablas.
- `Branch` configura la autoentrega GPS mediante activacion, radio de llegada, radio de salida y permanencia minima. Los valores iniciales son `true`, 50 m, 120 m y 15 s.
- `DeliveryRouteStop` persiste candidato y confirmacion de llegada, evidencias, distancia minima y la ubicacion de salida que produjo una autoentrega. El esquema se instala con `SenorArroz.Infrastructure/Scripts/add_delivery_auto_completion.sql` antes del backend que consulta estas columnas.

- La app móvil no debe poder operar sobre tenant distinto al del domiciliario autenticado.
- Cada ubicación nueva debe pertenecer a una sesión laboral activa del mismo domiciliario y dispositivo.
- El JWT de un domiciliario incluye `session_id` y, para la app actualizada, `device_id`. El backend valida `session_id` en cada solicitud y compara `device_id` al iniciar, cerrar o reportar datos de una jornada.
- `DeliveryTrackingAlert` conserva evidencia durable de cortes de GPS, permisos retirados y permanencias: inicio, fin o recuperación, duración y coordenadas inicial/final. Las coordenadas se copian a la alerta para que el registro sobreviva a la retención corta de `DeliverymanLocation`.
- El esquema de evidencia de alertas se instala con el script idempotente `SenorArroz.Infrastructure/Scripts/add_delivery_tracking_alert_location_evidence.sql` antes de desplegar el backend que usa esas columnas.
- `DeliveryTrackingIncident` admite el tipo `location_disabled` y usa `SourceDeviceEventId` como vínculo único con el evento `gps_disabled`. Sus coordenadas centrales son opcionales porque puede confirmarse el apagado aun cuando no exista un punto GPS utilizable.
- `DeliveryTrackingIncident` admite `tracking_interruption` y usa `AlertId` para vincular de forma única el silencio revisable con su alerta, ruta, eventos del dispositivo y puntos anterior/posterior.
- Las interrupciones conservan `InterruptionCause` y `InterruptionCertainty`; los eventos de recuperación guardan conteo y rango temporal tipados de las ubicaciones offline.
- La reproducción administrativa consulta `DeliverymanLocation` por `(deliveryman_id, recorded_at, id)`; el índice se instala con `SenorArroz.Infrastructure/Scripts/add_delivery_tracking_playback_index.sql`.
- El esquema para casos de revisión por ubicación apagada se instala con `SenorArroz.Infrastructure/Scripts/add_gps_disabled_review_incidents.sql` antes de desplegar el backend correspondiente.

### WhatsApp

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| WhatsAppBranchSetting | Sí | Desde `Branch.TenantId` |
| WhatsAppConversation | Sí | Desde `Branch.TenantId` |
| WhatsAppChannelSetting | Sí | Tenant inicial `1` y copia no destructiva de Santander |
| TenantAiSetting | Sí | Tenant inicial `1` y copia no destructiva de Santander |
| WhatsAppCommerceSession | Sí | Creación nueva por conversación central |
| WhatsAppCommerceSessionToken | Sí | Backfill del hash principal de cada sesión |
| WhatsAppFlowExchange | Sí, por sesión | Creación nueva |
| WhatsAppCommerceOutboxMessage | Sí | Creación nueva |
| WhatsAppCommerceEvent | Sí | Creación nueva, sin PII |
| WhatsAppMessage | Sí | Desde la conversación y sucursal |

Notas:

- `WhatsAppBranchSetting` guarda por sucursal la activación y plantilla del mensaje de ausencia.
- El esquema se amplía con `SenorArroz.Infrastructure/Scripts/add_whatsapp_away_message.sql`; debe instalarse antes del backend que consulta esas columnas.
- Los avisos usan `WhatsAppMessage.AgentDispatchKey` para garantizar como máximo un envío por conversación y periodo de cierre.
- `WhatsAppConversation` se identifica primero por `(BranchId, WhatsAppUserId)` y luego por `(BranchId, PhoneNumber)`; ambos campos son opcionales individualmente, pero al menos uno debe existir.
- Los índices únicos parciales permiten conversaciones sin teléfono y protegen tanto el teléfono como el BSUID dentro de una sucursal. `WhatsAppUsername` solo tiene índice de búsqueda.
- El esquema BSUID se instala con `SenorArroz.Infrastructure/Scripts/add_whatsapp_user_identity.sql` antes del backend y no realiza backfill especulativo; los historiales antiguos se enriquecen al recibir el siguiente webhook.
- Si BSUID y teléfono resuelven a historiales diferentes no se fusionan mensajes automáticamente; se registra el conflicto y solo se comparte la asociación al cliente cuando es seguro.
- El esquema central y su backfill se instalan con `SenorArroz.Infrastructure/Scripts/add_whatsapp_tenant_flow.sql` antes del backend que consulta estas tablas.
- `WhatsAppConversation.ChannelSettingId` identifica conversaciones tenant-wide y `OperationalBranchId` la sede asignada, que puede permanecer nula.
- Los índices únicos por sucursal solo abarcan conversaciones históricas (`channel_setting_id IS NULL`); las centrales son únicas por canal y teléfono/BSUID, sin fusionar historiales existentes.
- `WhatsAppCommerceSession.FlowTokenHash` y `WhatsAppCommerceSessionToken.TokenHash` nunca contienen el token original; varias invitaciones pueden resolver a la misma sesión e idempotencia. El script idempotente `SenorArroz.Infrastructure/Scripts/add_whatsapp_commerce_session_tokens.sql` debe ejecutarse antes del backend V2.
- `WhatsAppFlowExchange` deduplica por sesión y huella canónica; `WhatsAppCommerceOutboxMessage.EventKey` y `WhatsAppCommerceEvent.EventKey` son únicos globalmente.

### Impresión

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| BranchPrintSettings | Sí | Desde `Branch.TenantId` |
| PrintJob | Sí | Desde `Branch.TenantId` |

Notas:

- El print agent debe autenticarse contra una sucursal y tenant concretos.
- `BranchPrintSettings.KitchenAutoPrintTrigger` se almacena en `branch_print_settings.kitchen_auto_print_trigger` como `varchar(32) NOT NULL DEFAULT 'whenMarkedReady'`; solo admite `whenMarkedReady` y `whenOrderCreated`. El script idempotente de producción es `SenorArroz.Infrastructure/Scripts/fix_kitchen_auto_print_trigger.sql`.
- Nunca debe leer trabajos de otra sucursal/tenant.
- La recuperación de pendientes usa el índice parcial
  `ix_print_job_pending_branch_kind_created (branch_id, kind, created_at, id)
  WHERE status = 'pending'`. Los scripts de alta y rollback se ejecutan fuera
  de una transacción porque usan `CONCURRENTLY`.

### Documentos corporativos

| Entidad | Alcance actual | Tenant-owned futuro |
|---|---|---:|
| BusinessDocument | Biblioteca única del negocio, compartida entre sucursales | Sí |

Notas:

- `business_document` conserva el nombre visible, URL de descarga Firebase, nombre interno del objeto y metadatos del PDF.
- `public_id` es un UUID no enumerable usado por el enlace público estable de los códigos QR.
- La lectura del catálogo exige autenticación; solo Superadmin administra registros. El enlace individual de descarga es público para permitir QR en cocina.
- Esta tabla es una excepción corporativa temporal mientras no exista la entidad `Tenant`. Al introducir tenants debe recibir `tenant_id`, backfill al tenant inicial y filtro garantizado por tenant.
- El esquema se instala con `SenorArroz.Infrastructure/Scripts/add_business_documents.sql` antes de desplegar el backend que lo consulta.

## Tablas técnicas

Incluso las tablas técnicas relacionadas con usuarios o sucursales deben tener tenant cuando puedan contener datos operativos o credenciales por cliente.

| Entidad | Recomendación |
|---|---|
| RefreshToken | Tenant-owned por usuario |
| PasswordResetToken | Tenant-owned por usuario |
| UserDeviceToken | Tenant-owned por usuario |

## Orden recomendado de migración

No hacer Big Bang. Migrar por fases:

1. Crear `Tenant`.
2. Crear tenant inicial para datos actuales.
3. Agregar `TenantId` nullable a `Branch`.
4. Backfill de `Branch`.
5. Convertir `Branch.TenantId` a NOT NULL.
6. Agregar `TenantId` nullable a tablas con `BranchId`.
7. Backfill desde `Branch`.
8. Agregar `TenantId` nullable a tablas dependientes de `Order`, `User`, `Expense`, etc.
9. Backfill desde entidad padre.
10. Crear índices compuestos.
11. Activar filtros globales EF Core por entidad.
12. Convertir columnas a NOT NULL por grupos.
13. Agregar pruebas de aislamiento.

## Patrón de backfill

Desde sucursal:

```sql
UPDATE target_table t
SET tenant_id = b.tenant_id
FROM branches b
WHERE t.branch_id = b.id
  AND t.tenant_id IS NULL;
```

Desde pedido:

```sql
UPDATE order_details od
SET tenant_id = o.tenant_id
FROM orders o
WHERE od.order_id = o.id
  AND od.tenant_id IS NULL;
```

Desde usuario:

```sql
UPDATE user_device_tokens udt
SET tenant_id = u.tenant_id
FROM users u
WHERE udt.user_id = u.id
  AND udt.tenant_id IS NULL;
```

Desde cierre/header:

```sql
UPDATE expense_details ed
SET tenant_id = eh.tenant_id
FROM expense_headers eh
WHERE ed.expense_header_id = eh.id
  AND ed.tenant_id IS NULL;
```

## Índices recomendados

Revisar todos los índices únicos actuales.

Regla:

```text
Toda unicidad funcional debe ser por tenant.
```

Ejemplos:

```text
UNIQUE (tenant_id, name)
UNIQUE (tenant_id, phone)
UNIQUE (tenant_id, branch_id, name)
UNIQUE (tenant_id, order_number)
```

Índices de búsqueda frecuentes:

```text
orders: (tenant_id, branch_id, created_at)
orders: (tenant_id, status)
customers: (tenant_id, branch_id, phone)
customers: (tenant_id, branch_id, whatsapp_user_id)
customers: (tenant_id, branch_id, whatsapp_username)
products: (tenant_id, category_id, name)
print_jobs: (tenant_id, branch_id, status, created_at)
expenses: (tenant_id, branch_id, date)
```

## Reglas para Codex

Cuando modifiques una entidad:

1. Confirmar si es tenant-owned. En este proyecto, asumir que sí salvo excepción documentada.
2. No tomar `TenantId` desde requests públicos.
3. Resolver tenant desde backend.
4. Actualizar configuración EF.
5. Actualizar migraciones.
6. Actualizar índices únicos.
7. Actualizar queries/repositorios.
8. Actualizar este mapa si cambia la relación.

## Riesgos principales

- Agregar `TenantId` NOT NULL sin backfill previo.
- Dejar queries manuales sin filtro de tenant.
- Mantener índices únicos globales que bloqueen datos repetidos entre restaurantes.
- Permitir que frontend mande `TenantId` como autoridad.
- No asociar tokens, dispositivos y print agents al tenant correcto.
## Integración Rappi API v2

Tablas:

- `delivery_app_connection`: configuración global de Rappi por sucursal; referencia cliente, app financiera y actor técnico.
- `delivery_app_store`: tiendas externas, relación padre/hija, `store_integration_id`, PING, conectividad y habilitación manual de Listo.
- `delivery_app_webhook_subscription`: secreto cifrado y estado por tipo de evento.
- `delivery_app_product_mapping`: selección uno a uno de `product`, overrides y último snapshot publicado.
- `external_delivery_order`: inbox funcional de órdenes, validación, totales, descuentos, PII y vínculo con el pedido interno.
- `integration_webhook_event`: inbox idempotente y outbox de estados externos.
- `rappi_menu_publication`: payload, hash y estado de cada publicación al padre.
- `rappi_availability_state`: estado deseado y último estado sincronizado por tienda y producto.
- `app_payment`: comisión estimada, neto esperado, valor liquidado, diferencia y reversión.
- `order`: referencia externa y snapshot operativo/financiero visible sin crear clientes individuales.

Unicidades:

- conexión: `(branch_id, provider, environment)` y `public_id`;
- tienda: `(connection_id, rappi_store_id)`;
- webhook: `(connection_id, event_type)`;
- producto: `(connection_id, product_id)` y `(connection_id, sku)`;
- orden externa: `(connection_id, external_order_id)`;
- evento: `(connection_id, event_key)`;
- disponibilidad: `(store_id, product_mapping_id)`.

El esquema inicial se aplica con `SenorArroz.Infrastructure/Scripts/upgrade_rappi_v2_sandbox.sql`; la separación por ambiente usa `rappi_connections_by_environment.sql`, antes de desplegar el código correspondiente. No se usan migraciones EF. `cleanup_rappi_test_orders.sql` es una operación acotada para datos DEV explícitamente identificados, no una migración ni un borrado global.

Preparación interna 2026-10-21: `rappi_menu_publication.status` es token de
concurrencia EF para no sobrescribir una revisión de aprobación con una respuesta
tardía. Es metadata del modelo sobre la columna existente, sin DDL. Los estados
`approval_review_required` y `integration_webhook_event.cancel_effects_pending` /
`cancel_effects_leased` caben en los `varchar(40)` actuales, que no tienen CHECK
de valores. La fase de efectos de cancelación reutiliza el JSON del inbox; no
crea tabla ni columna. Ver `RAPPI_INTERNAL_READINESS_2026-10-21.md` para rollout.

## Pago en línea Wompi

Tablas:

- `wompi_payment_integration`: configuración por `(tenant_id, branch_id)`, App financiera, ambiente y secretos cifrados separados.
- `storefront_checkout`: snapshot idempotente y temporal de cliente, dirección, líneas, totales y beneficio elegido antes de materializar un pedido pagado.
- `wompi_payment_attempt`: referencia única, snapshot público/firma, monto, expiración y revisión manual; pertenece a un pedido heredado o a un checkout público.
- `wompi_provider_transaction`: observaciones de transacciones externas, únicas por `provider_transaction_id`.
- `wompi_webhook_event`: inbox idempotente por huella criptográfica del evento.
- `payment_notification_outbox`: entrega reintentable a cocina, única por pedido y tipo de evento.
- `wompi_integration_audit`: altas, cambios y pruebas de configuración con actor administrativo.
- `app_payment`: movimiento bruto creado únicamente al aprobar o validar manualmente un pago.

El esquema base se aplica con `SenorArroz.Infrastructure/Scripts/add_wompi_payments.sql`. La actualización puntual para checkout previo al pedido es `SenorArroz.Infrastructure/Scripts/add_storefront_checkout.sql`; ambas forman parte de `local-init-completo.sql`.

## Gestión de pedidos originados para otra sucursal

`OriginBranchId` registra en el servidor la sucursal de trabajo al crear el pedido; no cambia al trasladar al empleado. `BranchId` conserva preparación, inventario, rutas, impresión, venta y cobro. No se duplica el pedido ni se cambia el usuario/tenant/sucursal global. Pedidos antiguos con origen desconocido permanecen NULL y no aparecen como remotos; no reconstruir el origen usando la sucursal actual de TakenBy.

Admin, Cashier y Superadmin del origen pueden gestionar únicamente esos pedidos, manteniendo las restricciones normales de rol/fecha/estado/proveedor. Kitchen y Deliveryman no reciben acceso remoto. La autorización es por recurso y tenant, antes de escribir; no se amplía BranchContext ni el acceso general a usuarios, bancos, caja, liquidaciones o GPS.

- `GET /api/orders/remote-branches`: destinos con pedidos originados aquí, con sampleOrderId autorizado.
- `POST /api/orders/search` con `remoteOnly: true, branchId: destino`: origen derivado del servidor, filtro anterior a conteo/paginación.
- `GET /api/orders/{id}/operation-options`: bancos normales/apps y domiciliarios activos del destino, sin saldos/perfiles.
- `GET /api/orders/{id}/tracking`: solo ruta y jornada actuales del pedido en camino. No devuelve historial ni usa un punto anterior de otra ruta; identifica GPS sin señal reciente.
- `POST /api/orders/{id}/print`: encola exclusivamente en destino; estado consultable por pedido/jobId.

Mutaciones reutilizan endpoints existentes y reciben `X-Order-Id` + `X-Order-Version` del formulario. Son obligatorios al gestionar desde origen. `409 ORDER_VERSION_CONFLICT` exige cerrar y recargar; nunca reintentar con una versión nueva silenciosamente. Transacción y bloqueo ordenado del pedido en PostgreSQL protegen cambios y pagos; ManagementVersion aumenta también en cambios a hijos. La respuesta expone cabeceras de versión. Se registra actor/comando/sucursal de trabajo/destino/versión en entity_audit_log, además de la auditoría monetaria existente. SignalR invalida únicamente grupos administrativos de origen y destino, aislados por tenant. El frontend refresca la lista visible y conserva el filtro/pestaña; no carga GPS hasta abrir seguimiento.

**Despliegue manual posterior a revisión:** aplicar primero `SenorArroz.Infrastructure/Scripts/add_remote_order_management.sql` con cuenta de mantenimiento; después backend y frontend. SQL aditivo/idempotente, sin backfill, sin migraciones EF. No se ejecuta desde CI ni se modifica producción en esta rama. Cobrar desde origen no traslada dinero entre cajas: el registro pertenece al destino; transferencias físicas entre sedes continúan por los procesos existentes.

## POS: creación atómica e idempotente (incidente 15-oct-2026)

La tabla `public."order"` incorpora columnas opcionales `pos_creation_key` (UUID del
borrador creado por el POS) y `pos_creation_fingerprint` (hash SHA-256 calculado
en backend). Se conserva `storefront_idempotency_key` **separado**, sin alterar
las integraciones Next.js, Wompi, WhatsApp o Rappi.

Índice parcial único: `ux_order_pos_creation_tenant` sobre
`(tenant_id, pos_creation_key)` cuando la clave no es nula. Los pedidos
históricos sin clave permanecen válidos. El backend controla que una repetición
del UUID pertenezca al mismo usuario y sucursal y tenga el mismo fingerprint.
Un intento concurrente con el mismo UUID no inserta un segundo pedido.

**Migración previa al despliegue:** `SenorArroz.Infrastructure/Scripts/add_pos_creation_idempotency.sql`.
Es idempotente, aditiva y no realiza backfill. **No se ejecuta automáticamente
mediante PR.** Para rollback, primero revertir las aplicaciones; las columnas
pueden conservarse sin afectar las versiones anteriores. Su eliminación opcional
se detalla en los comentarios del script.

La creación por POS confirma una sola transacción PostgreSQL para pedido, líneas,
reservas de inventario, pagos bancarios/apps y efectivo en tienda. Los triggers
de totales actualizan `management_version`, por lo que el backend recarga la
versión desde PostgreSQL antes de actualizar el efectivo. Las notificaciones y
comandas se emiten **después del COMMIT**: no pueden convertirse en rollback de
mensajes ya enviados; si fallan se registran para diagnóstico y no presentan el
pedido como no creado. Los clientes POS antiguos sin `posCreationKey` siguen
siendo aceptados, pero no disponen de la protección entre reintentos.

## Facturación electrónica por tenant

electronic_invoice_integration conserva proveedor, ambiente, credenciales cifradas y numeraciones. billing_recipient_profile conserva destinatarios reutilizables por tenant. customer_billing_recipient conserva asociaciones y predeterminados por cliente mediante profile_id con FK compuesta por tenant. Sus columnas fiscales anteriores se conservan para compatibilidad de despliegue y revisión de duplicados; las lecturas nuevas usan el perfil compartido. electronic_invoice_document conserva snapshots, trabajos recuperables, validación y archivos privados. Todas son tenant-owned con RLS forzado y FKs compuestas. Scripts: add_electronic_invoicing.sql y add_billing_recipient_directory.sql; operación: docs/ELECTRONIC_INVOICING.md.
## Almuerzos

EmployeeLunch (employee_lunch) es tenant-owned, con sucursal histórica, empleado, salida/regreso UTC y actores. FK compuestas por tenant, RLS forzado, índice de intervalo abierto único y trigger de no solapamiento. Las asignaciones operativas y los almuerzos escriben la misma fila de usuario para serializar y detectar snapshots serializables obsoletos; no se cambia la identidad ni sus asociaciones. UpdatedAt es la versión de corrección administrativa. Script: add_employee_lunch.sql. El inicializador raíz incorpora el mismo SQL sin depender de facturación electrónica.

## Entrega fiscal propia
add_invoice_delivery.sql amplía electronic_invoice_document con provider_pdf, pdf_version, email_channel, email_error y email_sent_at. EmailOutboxMessage incorpora deduplication_key única por tenant, first_attempted_at, lease_until y provider_message_id. Los adjuntos se referencian por documento y hashes SHA-256 en metadata_json; no se duplican los bytes. RLS y trigger impiden correos fiscales que referencien documentos de otro tenant.

La identidad visual se versiona en electronic_invoice_branding (RLS, logo privado, trigger inmutable) y electronic_invoice_document.branding_id utiliza FK compuesta por tenant. Un logo se almacena una sola vez por versión, compartida entre documentos. Script: add_invoice_branding.sql.

El directorio es único por (tenant_id,document_type,identification), normalizando NIT sin DV y otros documentos en mayúsculas. updated_at se genera en PostgreSQL y sirve como versión de edición. El script puntual es idempotente: agrega la tabla, relaciona perfiles anteriores sin borrar filas ni tocar documentos y marca needs_review si existen contactos distintos. Reaplicarlo tras el despliegue recoge registros creados por una versión anterior durante la transición. No se sobreescriben perfiles existentes al reaplicar.

add_email_delivery_tracking.sql incorpora delivery_status, delivery_event_at, delivery_event_id y delivery_error al outbox existente e indexa provider_message_id. Conserva RLS, asociación tenant-owned y estados de envío separados; no cambia archivos ni metadatos congelados. Aplicar antes del backend; incluido en el inicializador raíz.

## DELIVERY-2026-10
User.delivery_context_version/last_delivery_take_id ordenan el contexto. DeliveryRoute.closure_source distingue cierre operativo. Order.delivery_completion_source distingue entregas observadas/inferidas. Los recibos viven en entity_audit_log (delivery_operation, usuario, UUID) bajo índice único tenant-owned, sin caducidad automática. Scripts add_delivery_operations.sql y enforce_delivery_route_uniqueness.sql; preflight obligatorio, sin borrado de historia.
