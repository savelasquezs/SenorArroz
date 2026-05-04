# SenorArroz API - Codex Rules

## Objetivo de este archivo

Este repositorio es el backend principal de Señor Arroz. Antes de buscar archivos de forma amplia, Codex debe leer este archivo y los mapas en `docs/` para ubicar rápido el módulo correcto.

## Lectura obligatoria antes de modificar

1. `docs/PROJECT_MAP.md` - mapa rápido de capas, módulos y rutas probables.
2. `docs/DATABASE_MAP.md` - entidades, DbContext, migraciones y reglas de datos.
3. `docs/BUSINESS_RULES.md` - reglas funcionales de pedidos, cocina, pagos, domicilios e impresión.
4. `docs/MULTITENANT_PLAN.md` - estrategia para convertir el sistema en SaaS multitenant.

## Arquitectura

El proyecto sigue Clean Architecture con cuatro capas:

1. `SenorArroz.API` - presentación, controllers, auth, middlewares y configuración HTTP.
2. `SenorArroz.Application` - CQRS, DTOs, mappings, validaciones, servicios de aplicación.
3. `SenorArroz.Domain` - entidades, enums, interfaces, excepciones y reglas del dominio.
4. `SenorArroz.Infrastructure` - EF Core, repositorios, DbContext, servicios externos, persistencia.

Flujo de dependencias esperado: `API -> Application -> Domain <- Infrastructure`.

## Patrones implementados

### CQRS con MediatR

- Commands para escritura.
- Queries para lectura.
- Handlers en `Application/Features/{Feature}/Commands` o `Queries`.

### Repository Pattern

- Interfaces en `Domain/Interfaces/Repositories`.
- Implementaciones en `Infrastructure/Repositories`.

### DTO Pattern

- Usar DTOs para comunicación API-cliente.
- No exponer entidades del dominio directamente.
- AutoMapper para mapeos cuando aplique.

## Convenciones

### Commands

```text
CreateOrderCommand.cs
CreateOrderHandler.cs
UpdateOrderCommand.cs
UpdateOrderHandler.cs
```

### Queries

```text
GetOrdersQuery.cs
GetOrdersHandler.cs
GetOrderByIdQuery.cs
GetOrderByIdHandler.cs
```

### DTOs

```text
CreateOrderDto.cs
UpdateOrderDto.cs
OrderDto.cs
OrderWithDetailsDto.cs
```

## Excepciones

Usar siempre excepciones de dominio o aplicación adecuadas:

- `BusinessException` para lógica de negocio.
- `NotFoundException` para no encontrado.
- `ValidationException` para validaciones.
- `UnauthorizedAccessException` para autorización.

No usar excepciones genéricas para reglas de negocio.

## Base de datos

- PostgreSQL usa `snake_case`.
- C# usa PascalCase/camelCase.
- Los enums se almacenan en `snake_case`.
- JSON expone enums en `camelCase`.
- DateTime siempre en UTC.
- Cambios de entidades deben actualizar configuración EF, migraciones, seeds y `docs/DATABASE_MAP.md`.
- Modificacion reciente relevante: `bank_payment` ahora puede vincularse con `reservation_deposit` mediante `source_reservation_deposit_id`. Si tocas ese flujo, revisa `SenorArroz.Infrastructure/Scripts/add_source_reservation_deposit_id_to_bank_payment.sql`, la configuracion EF de `BankPayment` y los tests asociados.

## Autenticación y autorización

Usar `ICurrentUser` en handlers cuando se necesite contexto del usuario actual.

```csharp
var userId = _currentUser.Id;
var role = _currentUser.Role;
var branchId = _currentUser.BranchId;
```

## Regla actual crítica: filtro por sucursal

Los usuarios que no sean `superadmin` solo deben ver o modificar datos de su sucursal.

```csharp
int? branchFilter = null;
if (_currentUser.Role != "superadmin")
{
    branchFilter = _currentUser.BranchId;
}
else if (request.BranchId.HasValue)
{
    branchFilter = request.BranchId;
}
```

## Regla futura crítica: multitenancy

El sistema será vendido como SaaS. Todo cambio nuevo debe prepararse para multitenancy:

- Toda tabla propia de un cliente debe tener `TenantId`.
- Toda query de datos del negocio debe quedar aislada por tenant.
- Nunca confiar en `TenantId` enviado desde el frontend.
- El tenant debe resolverse desde contexto autenticado, claim, subdominio o configuración segura del backend.
- Las tablas compartidas deben documentarse explícitamente.
- No mezclar datos entre tenants, ni siquiera para usuarios de sucursal.

## Paginación

Los endpoints de listado deben usar `PagedResult<T>`.

## Reglas de implementación

- Registrar dependencias en los `DependencyInjection.cs`.
- Configurar EF con Fluent API.
- Mantener interfaces en Domain e implementaciones en Infrastructure.
- Validar permisos, rol, sucursal y tenant antes de operaciones sensibles.
- Usar `ILogger<T>` cuando haga falta trazabilidad.
- Preferir cambios pequeños, revisables y orientados a PR.

## Para Codex

- Usa este archivo como reglas base del backend.
- Antes de buscar globalmente, revisa los mapas en `docs/`.
- Sigue los patrones CQRS ya existentes.
- Mantén el alcance de cada cambio ajustado a la feature tocada.
- Prioriza coherencia con handlers, DTOs, repositorios y excepciones ya presentes.
