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

### Clientes y ubicación

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| Customer | Sí | Desde órdenes existentes, sucursal asociada o tenant inicial |
| Address | Sí | Desde `Customer.TenantId` o `Branch.TenantId` |
| Neighborhood | Sí | Tenant inicial o por sucursal si ya existe relación |

Notas:

- Si un teléfono de cliente es único, debe pasar de `UNIQUE(phone)` a `UNIQUE(tenant_id, phone)`.
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

Notas:

- La app móvil no debe poder operar sobre tenant distinto al del domiciliario autenticado.
- Cada ubicación nueva debe pertenecer a una sesión laboral activa del mismo domiciliario y dispositivo.

### Impresión

| Entidad | Tenant-owned | Backfill sugerido |
|---|---:|---|
| BranchPrintSettings | Sí | Desde `Branch.TenantId` |
| PrintJob | Sí | Desde `Branch.TenantId` |

Notas:

- El print agent debe autenticarse contra una sucursal y tenant concretos.
- Nunca debe leer trabajos de otra sucursal/tenant.

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
customers: (tenant_id, phone)
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
