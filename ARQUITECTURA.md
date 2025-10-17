# Arquitectura del Proyecto SenorArroz API

## Visión General

SenorArroz API es un sistema de gestión para restaurantes implementado siguiendo los principios de **Clean Architecture** y el patrón **CQRS** (Command Query Responsibility Segregation).

## Estructura de Capas

```
┌─────────────────────────────────────────┐
│         SenorArroz.API                  │  ← Presentation Layer
│  (Controllers, Middleware, Program.cs)  │
└────────────────┬────────────────────────┘
                 │
                 ↓
┌─────────────────────────────────────────┐
│      SenorArroz.Application             │  ← Application Layer
│  (CQRS, DTOs, Mappings, Services)       │
└────────────────┬────────────────────────┘
                 │
                 ↓
┌─────────────────────────────────────────┐
│        SenorArroz.Domain                │  ← Domain Layer
│  (Entities, Enums, Interfaces)          │
└─────────────────────────────────────────┘
                 ↑
                 │
┌─────────────────────────────────────────┐
│    SenorArroz.Infrastructure            │  ← Infrastructure Layer
│  (Repositories, DbContext, Services)    │
└─────────────────────────────────────────┘
```

## 1. SenorArroz.API (Presentation Layer)

### Responsabilidades
- Recibir requests HTTP
- Autenticación y autorización
- Validación de entrada (ModelState)
- Enrutar a Application Layer (via MediatR)
- Formatear respuestas HTTP

### Componentes Principales

#### Controllers
```
Controllers/
├── AuthController.cs        → Login, logout, refresh token
├── OrdersController.cs      → CRUD de pedidos
├── UsersController.cs       → Gestión de usuarios
├── CustomersController.cs   → Gestión de clientes
├── BankPaymentsController.cs
└── AppPaymentsController.cs
```

#### Middleware
```
Middleware/
├── GlobalExceptionMiddleware.cs  → Manejo centralizado de errores
└── JwtMiddleware.cs              → Extracción y validación de JWT
```

#### Program.cs
- Configuración de servicios (DI)
- Configuración de JWT Authentication
- Configuración de Swagger
- Configuración de CORS
- Pipeline de middleware

### Características Clave
- **Autorización por roles**: `[Authorize(Roles = "Admin,Superadmin")]`
- **Serialización JSON**: camelCase con enums como strings
- **Swagger UI**: Documentación interactiva en `/`

---

## 2. SenorArroz.Application (Application Layer)

### Responsabilidades
- Lógica de aplicación (orquestación)
- CQRS (Commands y Queries)
- Validaciones de negocio
- Mapeo de DTOs
- Servicios de aplicación

### Componentes Principales

#### Features (CQRS)
```
Features/
├── Orders/
│   ├── Commands/
│   │   ├── CreateOrderCommand.cs
│   │   ├── CreateOrderHandler.cs
│   │   ├── UpdateOrderCommand.cs
│   │   └── UpdateOrderHandler.cs
│   ├── Queries/
│   │   ├── GetOrdersQuery.cs
│   │   ├── GetOrdersHandler.cs
│   │   ├── GetOrderByIdQuery.cs
│   │   └── GetOrderByIdHandler.cs
│   └── DTOs/
│       ├── CreateOrderDto.cs
│       ├── UpdateOrderDto.cs
│       ├── OrderDto.cs
│       └── OrderWithDetailsDto.cs
├── Users/
├── Customers/
├── BankPayments/
└── AppPayments/
```

#### Mappings (AutoMapper)
```
Mappings/
├── OrderMappingProfile.cs
├── UserMappingProfile.cs
├── CustomerMappingProfile.cs
└── ...
```

#### Common
```
Common/
├── Interfaces/
│   ├── ICurrentUser.cs
│   ├── IOrderBusinessRulesService.cs
│   └── IApplicationDbContext.cs
└── Services/
    └── OrderBusinessRulesService.cs
```

### Patrón CQRS

#### Commands (Escritura)
```csharp
// Command
public class CreateOrderCommand : IRequest<OrderDto>
{
    public CreateOrderDto Order { get; set; } = null!;
}

// Handler
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        // 1. Validar
        // 2. Mapear DTO → Entity
        // 3. Guardar en repositorio
        // 4. Retornar DTO
    }
}
```

#### Queries (Lectura)
```csharp
// Query
public class GetOrdersQuery : IRequest<PagedResult<OrderDto>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

// Handler
public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        // 1. Filtrar por sucursal
        // 2. Obtener datos del repositorio
        // 3. Aplicar filtros adicionales
        // 4. Paginar
        // 5. Mapear a DTOs
        // 6. Retornar PagedResult
    }
}
```

---

## 3. SenorArroz.Domain (Domain Layer)

### Responsabilidades
- Definir entidades del negocio
- Definir interfaces (contratos)
- Definir enums y value objects
- Excepciones del dominio
- **NO tiene dependencias de otras capas**

### Componentes Principales

#### Entities
```
Entities/
├── Common/
│   └── BaseEntity.cs          → Id, CreatedAt, UpdatedAt
├── Order.cs
├── OrderDetail.cs
├── Customer.cs
├── User.cs
├── Branch.cs
└── ...
```

**BaseEntity** (todas las entidades heredan):
```csharp
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }  // UTC, generado por DB
    public DateTime UpdatedAt { get; set; }  // UTC, generado por DB
}
```

#### Enums
```csharp
// Enums.cs
public enum OrderStatus
{
    Taken,          // → "taken" en DB
    InPreparation,  // → "in_preparation" en DB
    Ready,          // → "ready" en DB
    OnTheWay,       // → "on_the_way" en DB
    Delivered,      // → "delivered" en DB
    Cancelled       // → "cancelled" en DB
}

public enum OrderType
{
    Onsite,         // → "onsite" en DB
    Delivery,       // → "delivery" en DB
    Reservation     // → "reservation" en DB
}

public enum UserRole
{
    Superadmin,     // → "superadmin" en DB
    Admin,          // → "admin" en DB
    Cashier,        // → "cashier" en DB
    Kitchen,        // → "kitchen" en DB
    Deliveryman     // → "deliveryman" en DB
}
```

#### Exceptions
```csharp
BusinessException       // 400 Bad Request
NotFoundException       // 404 Not Found
ValidationException     // 400 Bad Request (con detalles)
```

#### Interfaces

**Repositories**:
```
Interfaces/Repositories/
├── IOrderRepository.cs
├── IUserRepository.cs
├── ICustomerRepository.cs
└── ...
```

**Services**:
```
Interfaces/Services/
├── IJwtService.cs
├── IPasswordService.cs
└── IEmailService.cs
```

---

## 4. SenorArroz.Infrastructure (Infrastructure Layer)

### Responsabilidades
- Implementar repositorios
- Configuración de Entity Framework
- Servicios externos (email, etc.)
- Acceso a base de datos PostgreSQL

### Componentes Principales

#### Data
```
Data/
├── ApplicationDbContext.cs
├── Configurations/
│   ├── OrderConfiguration.cs
│   ├── UserConfiguration.cs
│   ├── CustomerConfiguration.cs
│   └── ...
└── Migrations/
```

#### Repositories
```
Repositories/
├── OrderRepository.cs
├── UserRepository.cs
├── CustomerRepository.cs
└── ...
```

#### Services
```
Services/
├── CurrentUserService.cs              → Extrae info del usuario del token
├── JwtService.cs                      → Genera y valida tokens
├── PasswordService.cs                 → Hash y validación de contraseñas
├── EmailService.cs                    → Envío de emails
├── TokenCleanupService.cs             → Background service
└── PasswordResetCleanupService.cs     → Background service
```

#### Entity Framework Configurations

**Ejemplo** (`OrderConfiguration.cs`):
```csharp
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("order");
        builder.HasKey(o => o.Id);
        
        // Mapeo de columnas
        builder.Property(o => o.BranchId).HasColumnName("branch_id");
        builder.Property(o => o.GuestName).HasColumnName("guestname");
        
        // Conversión de enums
        builder.Property(o => o.Status).HasConversion(
            v => ToSnakeCase(v.ToString()),           // C# → DB
            v => Enum.Parse<OrderStatus>(ToPascalCase(v), true) // DB → C#
        );
        
        // Relaciones
        builder.HasOne(o => o.Branch)
            .WithMany(b => b.Orders)
            .HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

---

## Tecnologías Principales

| Tecnología | Versión | Uso |
|------------|---------|-----|
| .NET | 8.0 | Framework base |
| ASP.NET Core | 8.0 | Web API |
| Entity Framework Core | 8.0 | ORM |
| PostgreSQL | Latest | Base de datos |
| Npgsql | Latest | Driver PostgreSQL |
| MediatR | Latest | Patrón Mediator (CQRS) |
| AutoMapper | Latest | Mapeo DTO ↔ Entity |
| JWT | Latest | Autenticación |
| Swagger/OpenAPI | Latest | Documentación API |

---

## Flujo de Dependencias

```
API → Application → Domain
               ↑
               │
         Infrastructure
```

### Reglas de Dependencia
- ✅ API puede referenciar Application
- ✅ Application puede referenciar Domain
- ✅ Infrastructure puede referenciar Domain
- ❌ Domain NO puede referenciar a nadie (puro)
- ❌ Infrastructure NO puede referenciar Application
- ❌ Application NO puede referenciar Infrastructure

---

## Configuración

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=senorarroz;..."
  },
  "JwtSettings": {
    "SecretKey": "...",
    "Issuer": "SenorArrozAPI",
    "Audience": "SenorArrozClient",
    "ExpirationMinutes": 60
  },
  "EmailSettings": {
    "SmtpServer": "smtp.gmail.com",
    "SmtpPort": 587,
    // ...
  }
}
```

---

## Principios de Diseño

### Single Responsibility
Cada handler hace **una sola cosa**:
- CreateOrderHandler → Solo crea pedidos
- GetOrdersHandler → Solo obtiene listado de pedidos

### Separation of Concerns
- Controllers: Solo routing y autorización
- Handlers: Lógica de negocio
- Repositories: Acceso a datos
- Services: Servicios compartidos

### Dependency Inversion
- Dependencias mediante interfaces
- Inyección de dependencias
- Fácil testeo y mantenimiento

---

## Próximos Pasos para Nuevos Desarrolladores

1. Leer esta documentación completa
2. Explorar un Feature completo (ej: Orders)
3. Ver cómo funciona CQRS en la práctica
4. Revisar manejo de errores y autenticación
5. Crear un nuevo Feature siguiendo los patrones establecidos

