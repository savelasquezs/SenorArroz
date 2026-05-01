# SenorArroz API - Codex Rules

## Arquitectura

El proyecto sigue Clean Architecture con cuatro capas:

1. `SenorArroz.API` - presentacion
2. `SenorArroz.Application` - CQRS, DTOs, mappings, servicios
3. `SenorArroz.Domain` - entidades, enums, interfaces, excepciones
4. `SenorArroz.Infrastructure` - repositorios, DbContext, servicios externos

Flujo de dependencias: `API -> Application -> Domain <- Infrastructure`

## Patrones implementados

### CQRS con MediatR

- Commands para escritura
- Queries para lectura
- Handlers en `Application/Features/{Feature}/Commands` o `Queries`

### Repository Pattern

- Interfaces en `Domain/Interfaces/Repositories`
- Implementaciones en `Infrastructure/Repositories`

### DTO Pattern

- Usar DTOs para comunicacion API-cliente
- No exponer entidades del dominio directamente
- AutoMapper para mapeos

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

Usar siempre excepciones de dominio o aplicacion adecuadas:

- `BusinessException` para logica de negocio
- `NotFoundException` para no encontrado
- `ValidationException` para validaciones
- `UnauthorizedAccessException` para autorizacion

No usar excepciones genericas para reglas de negocio.

## Base de datos

- PostgreSQL usa `snake_case`
- C# usa PascalCase/camelCase
- Los enums se almacenan en `snake_case`
- JSON expone enums en `camelCase`
- DateTime siempre en UTC

## Autenticacion y autorizacion

Usar `ICurrentUser` en handlers cuando se necesite contexto del usuario actual.

```csharp
var userId = _currentUser.Id;
var role = _currentUser.Role;
var branchId = _currentUser.BranchId;
```

## Regla critica: filtro por sucursal

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

## Paginacion

Los endpoints de listado deben usar `PagedResult<T>`.

## Reglas de implementacion

- Registrar dependencias en los `DependencyInjection.cs`
- Configurar EF con Fluent API
- Mantener interfaces en Domain e implementaciones en Infrastructure
- Validar permisos, rol y sucursal antes de operaciones sensibles
- Usar `ILogger<T>` cuando haga falta trazabilidad

## Para Codex

- Usa este archivo como reglas base del backend.
- Sigue los patrones CQRS ya existentes.
- Mantén el alcance de cada cambio ajustado a la feature tocada.
- Prioriza coherencia con handlers, DTOs, repositorios y excepciones ya presentes.
