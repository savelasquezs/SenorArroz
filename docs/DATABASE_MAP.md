# Señor Arroz Backend - Database Map

Este mapa existe para que Codex ubique rápido las entidades y entienda cómo prepararlas para SaaS multitenant.

## Decisión confirmada

Todo el negocio será `tenant-owned`.

Eso significa que, salvo tablas puramente técnicas o de infraestructura global, cada tabla funcional debe pertenecer a un tenant.

```text
Una sola base de datos PostgreSQL
+ todas las tablas del negocio con TenantId
+ filtros globales en EF Core
+ índices compuestos por tenant
```

## Fuente principal

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

## Entidad raíz futura

### Tenant

Debe crearse como raíz SaaS.

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

## Clasificación de tablas

### Núcleo organizacional

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Branch | Sí | Tenant inicial `1` |
| BranchPrintSettings | Sí | Desde `Branch.TenantId` |
| BranchInformalLoan | Sí | Desde `Branch.TenantId` |
| BranchInformalLoanExemptOrder | Sí | Desde Branch/Order según relación |
| User | Sí | Desde `Branch.TenantId` si tiene sucursal; si no, tenant inicial |
| RefreshToken | Sí | Desde `User.TenantId` |
| PasswordResetToken | Sí | Desde `User.TenantId` |
| UserDeviceToken | Sí | Desde `User.TenantId` |

Notas:

- `Branch` debe ser la primera tabla del negocio en recibir `TenantId`.
- `Branch` contiene la configuración operativa del seguimiento de domiciliarios: hora local de cierre, frecuencias, permanencias, tolerancia geográfica y retenciones.
- `User` debe quedar asociado a tenant aunque sea superadmin. Si hay superadmin global, documentar excepción.
- `User.ActiveSessionId` contiene la sesión exclusiva vigente de un domiciliario.
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

### Productos y fidelización

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| ProductCategory | Sí | Tenant inicial |
| Product | Sí | Desde `ProductCategory.TenantId` o tenant inicial |
| LoyaltyCycleStep | Sí | Desde `Product.TenantId` o tenant inicial |

Notas:

- Productos y categorías NO deben compartirse entre restaurantes.
- Nombres únicos deben ser por tenant, no globales.

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
| DeliveryTrackingAlert | Sí | Desde `Branch.TenantId` |

Notas:

- La app móvil no debe poder operar sobre tenant distinto al del domiciliario autenticado.
- Cada ubicación nueva debe pertenecer a una sesión laboral activa del mismo domiciliario y dispositivo.
- El JWT de un domiciliario incluye `session_id` y, para la app actualizada, `device_id`. El backend valida `session_id` en cada solicitud y compara `device_id` al iniciar, cerrar o reportar datos de una jornada.
- `DeliveryTrackingAlert` conserva evidencia durable de cortes de GPS, permisos retirados y permanencias: inicio, fin o recuperación, duración y coordenadas inicial/final. Las coordenadas se copian a la alerta para que el registro sobreviva a la retención corta de `DeliverymanLocation`.
- El esquema de evidencia de alertas se instala con el script idempotente `SenorArroz.Infrastructure/Scripts/add_delivery_tracking_alert_location_evidence.sql` antes de desplegar el backend que usa esas columnas.
- `DeliveryTrackingIncident` admite el tipo `location_disabled` y usa `SourceDeviceEventId` como vínculo único con el evento `gps_disabled`. Sus coordenadas centrales son opcionales porque puede confirmarse el apagado aun cuando no exista un punto GPS utilizable.
- La reproducción administrativa consulta `DeliverymanLocation` por `(deliveryman_id, recorded_at, id)`; el índice se instala con `SenorArroz.Infrastructure/Scripts/add_delivery_tracking_playback_index.sql`.
- El esquema para casos de revisión por ubicación apagada se instala con `SenorArroz.Infrastructure/Scripts/add_gps_disabled_review_incidents.sql` antes de desplegar el backend correspondiente.

### WhatsApp

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| WhatsAppBranchSetting | Sí | Desde `Branch.TenantId` |
| WhatsAppConversation | Sí | Desde `Branch.TenantId` |
| WhatsAppMessage | Sí | Desde la conversación y sucursal |

Notas:

- `WhatsAppBranchSetting` guarda por sucursal la activación y plantilla del mensaje de ausencia.
- El esquema se amplía con `SenorArroz.Infrastructure/Scripts/add_whatsapp_away_message.sql`; debe instalarse antes del backend que consulta esas columnas.
- Los avisos usan `WhatsAppMessage.AgentDispatchKey` para garantizar como máximo un envío por conversación y periodo de cierre.
- `WhatsAppConversation` se identifica primero por `(BranchId, WhatsAppUserId)` y luego por `(BranchId, PhoneNumber)`; ambos campos son opcionales individualmente, pero al menos uno debe existir.
- Los índices únicos parciales permiten conversaciones sin teléfono y protegen tanto el teléfono como el BSUID dentro de una sucursal. `WhatsAppUsername` solo tiene índice de búsqueda.
- El esquema BSUID se instala con `SenorArroz.Infrastructure/Scripts/add_whatsapp_user_identity.sql` antes del backend y no realiza backfill especulativo; los historiales antiguos se enriquecen al recibir el siguiente webhook.
- Si BSUID y teléfono resuelven a historiales diferentes no se fusionan mensajes automáticamente; se registra el conflicto y solo se comparte la asociación al cliente cuando es seguro.

### Impresión

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| BranchPrintSettings | Sí | Desde `Branch.TenantId` |
| PrintJob | Sí | Desde `Branch.TenantId` |

Notas:

- El print agent debe autenticarse contra una sucursal y tenant concretos.
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

- conexión: `(branch_id, provider)` y `public_id`;
- tienda: `(connection_id, rappi_store_id)`;
- webhook: `(connection_id, event_type)`;
- producto: `(connection_id, product_id)` y `(connection_id, sku)`;
- orden externa: `(connection_id, external_order_id)`;
- evento: `(connection_id, event_key)`;
- disponibilidad: `(store_id, product_mapping_id)`.

El esquema se aplica con `SenorArroz.Infrastructure/Scripts/upgrade_rappi_v2_sandbox.sql`; no se usan migraciones EF para esta integración.
