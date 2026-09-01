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
- `Branch.IsActive` controla si una sede participa en la web pública. La columna se instala con `SenorArroz.Infrastructure/Scripts/add_branch_active_storefront.sql`.

### Web pública, OTP y pedidos directos

Buscar primero:

- `SenorArroz.API/Controllers/PublicStorefrontController.cs`
- `SenorArroz.API/Controllers/PublicCustomerAuthController.cs`
- `SenorArroz.API/Services/StorefrontCustomerAuthService.cs`
- `SenorArroz.Infrastructure/Services/GoogleRoutesDrivingMetricsService.cs`
- `SenorArroz.Infrastructure/Services/AddressResolutionServices.cs`

Endpoints protegidos con credenciales exclusivas del BFF (`X-Storefront-Key-Id` y `X-Storefront-Key`):

- `GET /api/public/storefront/catalog`: catálogo compartido agrupado en `riceGroups`, `comboGroups`, `beverageGroups` y `additionGroups`. Cada grupo usa la ficha comercial y contiene opciones ordenadas con el `ProductId` real; expone las sucursales activas que tengan coordenadas y teléfono principal, incluyendo dirección, ubicación, horario semanal y enlace de contacto por WhatsApp, pero no inventario exacto ni promociones sin sucursal.
- `POST /api/public/storefront/address-preview`: geocodifica una dirección o lugar conocido dentro de las ciudades habilitadas para mostrar un mapa de confirmación sin exponer credenciales de Google al navegador.
- `POST /api/public/storefront/coverage-preview`: valida una ubicación confirmada y devuelve distancia, tiempo, cobertura dual y tarifa estimada desde cada sucursal sin exigir carrito ni datos del cliente.
- `POST /api/public/customer-auth/request-code`: crea un desafío temporal y envía `customers_web_authentication` por el WhatsApp autenticador configurado.
- `POST /api/public/customer-auth/verify-code`: consume el OTP y crea una sesión opaca; solo entonces devuelve nombre y direcciones cuando existe una coincidencia única.
- `GET /api/public/customer-auth/session`: recupera el estado mínimo del cliente desde el token de sesión enviado exclusivamente por el BFF.
- `POST /api/public/storefront/delivery-quote`: cotiza domicilio o recogida y revalida carrito, horario y promoción. Una dirección guardada exige sesión verificada y conserva su tarifa histórica.
- `POST /api/public/storefront/orders`: vuelve a cotizar y crea un pedido idempotente con cliente, dirección, sucursal y totales resueltos en servidor.

Seguridad y operación:

- `SenorArroz.API/Security/StorefrontApiKeyAuthentication.cs` valida identificador y hash de clave en tiempo constante.
- Las cotizaciones admiten cuerpos de hasta 32 KB, 60 solicitudes por minuto y ocho ejecuciones concurrentes por instancia, con valores configurables.
- Geocodificación y rutas válidas se cachean cinco minutos. Precio, estado, disponibilidad y promociones siempre se consultan nuevamente.
- Los contactos manuales de la landing se dirigen a `Branch.Phone1`, con respaldo en `Branch.Phone2`. Los OTP usan exclusivamente la configuración de WhatsApp de la sucursal autenticadora.
- El storefront permanece single-tenant hasta completar el aislamiento real de las consultas globales.
- `product_category.storefront_role` decide explícitamente qué puede publicarse. `hidden`, categorías desconocidas, productos inactivos y productos agotados no pueden incorporarse a una cotización.
- `product.storefront_variant_label` y `product.storefront_sort_order` controlan la presentación y el orden web sin inferir información desde el nombre del producto.

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
- `DeliveryRoutingPlan`
- `DeliveryRouteProposal`
- `DeliveryRouteProposalStop`

Enrutador dinámico V1:

- API: `SenorArroz.API/Controllers/DeliveryRoutingController.cs`
- Orquestación: `SenorArroz.Application/Features/DeliveryRouting`
- Matriz local y solver: `SenorArroz.Infrastructure/Services/ApproximateRoutingCostMatrixProvider.cs` y `OrToolsDeliveryRouteOptimizer.cs`
- Esquema: `SenorArroz.Infrastructure/Scripts/add_delivery_routing_v1.sql`

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
