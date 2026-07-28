# Señor Arroz Backend - Project Map

Este mapa existe para que Codex encuentre rápido dónde modificar sin escanear todo el repositorio.

## Tipo de repo

Backend principal de Señor Arroz.

Stack esperado:

- C# / ASP.NET Core
- Clean Architecture
- MediatR / CQRS
- Entity Framework Core
- PostgreSQL

## Capas principales

### `SenorArroz.API`

Responsable de:

- Controllers HTTP
- Middlewares
- Autenticación/autorización
- Configuración de servicios
- Entrada/salida hacia clientes web, móvil y agente de impresión

Buscar aquí cuando el cambio sea de:

- Endpoint nuevo
- Ruta HTTP
- Validación de request a nivel API
- Configuración de Swagger, CORS, JWT, SignalR o DI de API

### `SenorArroz.Application`

Responsable de:

- Commands
- Queries
- Handlers
- DTOs
- Interfaces de aplicación
- Reglas de caso de uso
- Mappings

Buscar aquí cuando el cambio sea de:

- Flujo de negocio
- Crear/actualizar/listar entidades
- Validaciones de aplicación
- Respuesta que consume frontend o app móvil

Convención esperada:

```text
SenorArroz.Application/Features/{Feature}/Commands
SenorArroz.Application/Features/{Feature}/Queries
SenorArroz.Application/Features/{Feature}/DTOs
```

### `SenorArroz.Domain`

Responsable de:

- Entidades
- Enums
- Interfaces de repositorios
- Excepciones de dominio
- Reglas puras del negocio

Buscar aquí cuando el cambio sea de:

- Nueva entidad
- Nuevo enum
- Relación de dominio
- Contrato de repositorio

### `SenorArroz.Infrastructure`

Responsable de:

- `ApplicationDbContext`
- Configuraciones EF Core
- Repositorios
- Servicios externos
- Migraciones
- Persistencia PostgreSQL

Buscar aquí cuando el cambio sea de:

- Query SQL/EF
- Include/proyección/filtros
- Migración
- Configuración de tabla/índices
- Implementación de repositorio

## Puntos de entrada frecuentes

### Base de datos

- DbContext: `SenorArroz.Infrastructure/Data/ApplicationDbContext.cs`
- Configuraciones EF: `SenorArroz.Infrastructure/Data/Configurations`
- Repositorios: `SenorArroz.Infrastructure/Repositories`
- Interfaces de repositorio: `SenorArroz.Domain/Interfaces/Repositories`

### Pedidos

Buscar primero:

- `SenorArroz.Application/Features/Orders`
- `SenorArroz.Domain/Entities/Order.cs`
- `SenorArroz.Domain/Entities/OrderDetail.cs`
- `SenorArroz.Infrastructure/Repositories/OrderRepository.cs`
- Controllers relacionados con orders en `SenorArroz.API`

Cambios típicos:

- Estados de pedido
- Filtros de búsqueda
- Cocina
- Domicilios
- Historial
- Reservas
- Impresión de comandas/facturas

### Clientes y direcciones

Buscar primero:

- `SenorArroz.Application/Features/Customers`
- `SenorArroz.Domain/Entities/Customer.cs`
- `SenorArroz.Domain/Entities/Address.cs`
- `SenorArroz.Domain/Entities/Neighborhood.cs`
- `SenorArroz.Infrastructure/Repositories/CustomerRepository.cs`

### Sucursales

Buscar primero:

- `Branch`
- `BranchPrintSettings`
- `BranchInformalLoan`
- `BranchInformalLoanExemptOrder`

La sucursal hoy es el aislamiento operativo principal. En la migración SaaS, la sucursal debe quedar debajo de un tenant.

Contexto operativo actual de sucursal:

- `IBranchContext` resuelve la sucursal efectiva sin cambiar `ICurrentUser.BranchId`, que sigue representando el claim del JWT.
- Superadmin selecciona la sucursal mediante `X-Branch-Id`; los demás roles siempre quedan limitados al claim.
- `GET /api/Branches/options` alimenta el selector global sin paginación ni estadísticas.
- `BranchScopeActionFilter` rechaza valores explícitos de ruta, query o body que contradigan el contexto.
- Productos, categorías y documentos corporativos permanecen compartidos en esta fase.
- El dashboard puede omitir el header exclusivamente para lecturas agregadas.

### Usuarios y autenticación

Buscar primero:

- `User`
- `RefreshToken`
- `PasswordResetToken`
- `UserDeviceToken`
- Servicios JWT/Auth en API/Application/Infrastructure

Regla importante:

- El usuario autenticado debe aportar contexto de rol, sucursal y, en el futuro, tenant.

### Productos y categorías

Buscar primero:

- `Product`
- `ProductCategory`
- `LoyaltyCycleStep`

### Pagos y bancos

Buscar primero:

- `Bank`
- `BankPayment`
- `BankTransfer`
- `App`
- `AppPayment`
- `ReservationDeposit`
- `CashRegisterClosure`
- `CashClosureBankReconciliation`
- `CashClosureInformalLoan`
- `CashVaultMovement`

Regla importante:

- Los pagos por apps pueden quedar retenidos antes de liberarse hacia bancos.

### Gastos y proveedores

Buscar primero:

- `Expense`
- `ExpenseHeader`
- `ExpenseDetail`
- `ExpenseCategory`
- `ExpenseBankPayment`
- `ExpenseMenuTarget`
- `Supplier`
- `SupplierExpense`

### Domiciliarios y rutas

Buscar primero:

- `DeliverymanAdvance`
- `DeliverymanDayState`
- `DeliverymanLocation`
- `DeliveryRoute`
- `DeliveryRouteStop`

### Impresión POS

Buscar primero:

- `BranchPrintSettings`
- `PrintJob`
- Endpoints `print-agent` / `print-jobs`
- Contrato con `senorArrozPrintAgent`

## Reglas para Codex

Antes de modificar:

1. Leer `AGENTS.md`.
2. Leer este mapa.
3. Leer `docs/DATABASE_MAP.md` si toca entidades, EF o migraciones.
4. Leer `docs/BUSINESS_RULES.md` si toca pedidos, pagos, cocina, domicilios o impresión.
5. Leer `docs/MULTITENANT_PLAN.md` si toca queries, autenticación, sucursales o datos compartidos.

Evitar:

- Búsqueda global sin contexto.
- Cambios amplios en varias features a la vez.
- Mezclar refactor con cambio funcional.
- Cambiar esquema sin documentarlo.
### Integración Rappi API v2

Buscar primero:

- `SenorArroz.API/Controllers/RappiIntegrationsController.cs`
- `SenorArroz.Infrastructure/Integrations/RappiDeliveryProvider.cs`
- `SenorArroz.Infrastructure/Integrations/RappiOrderProcessor.cs`
- `SenorArroz.Infrastructure/Integrations/RappiIntegrationWorker.cs`
- `SenorArroz.Infrastructure/Integrations/ExternalDeliveryStatusSyncService.cs`
- `SenorArroz.Domain/Entities/IntegrationEntities.cs`
- `SenorArroz.Infrastructure/Data/Configurations/IntegrationConfigurations.cs`
- `SenorArroz.Infrastructure/Scripts/upgrade_rappi_v2_sandbox.sql`
- `docs/RAPPI_INTEGRATION.md`

Frontend:

- `src/components/branches/BranchDeliveryAppsSection.vue`
- `src/views/RappiOrdersView.vue`
- `src/services/MainAPI/integrationApi.ts`
- `src/types/integrations.ts`
