# Convenciones del Proyecto

## Naming Conventions

### C# Code

| Elemento | Convención | Ejemplo |
|----------|------------|---------|
| Classes | PascalCase | `OrderRepository`, `CreateOrderHandler` |
| Interfaces | IPascalCase | `IOrderRepository`, `ICurrentUser` |
| Methods | PascalCase | `GetByIdAsync`, `CreateAsync` |
| Properties | PascalCase | `BranchId`, `CustomerName` |
| Parameters | camelCase | `orderId`, `branchFilter` |
| Private fields | _camelCase | `_orderRepository`, `_mapper` |
| Constants | PascalCase | `MaxActiveOrders` |
| Enums | PascalCase | `OrderStatus`, `UserRole` |
| Enum values | PascalCase | `InPreparation`, `OnTheWay` |

### Database (PostgreSQL)

| Elemento | Convención | Ejemplo |
|----------|------------|---------|
| Tables | snake_case | `order`, `order_detail`, `bank_payment` |
| Columns | snake_case | `branch_id`, `delivery_man_id`, `guest_name` |
| Enum values | snake_case | `in_preparation`, `on_the_way` |
| Constraints | table_column_constraint | `order_status_check` |
| Indexes | idx_table_column | `idx_order_status`, `idx_order_branch` |

### JSON API

| Elemento | Convención | Ejemplo |
|----------|------------|---------|
| Properties | camelCase | `branchId`, `customerName` |
| Enum values | camelCase | `inPreparation`, `onTheWay` |
| Endpoints | kebab-case (opcional) | `/api/orders`, `/api/bank-payments` |

---

## Estructura de Features (CQRS)

### Organización Estándar

```
Application/Features/{FeatureName}/
├── Commands/
│   ├── Create{Feature}Command.cs
│   ├── Create{Feature}Handler.cs
│   ├── Update{Feature}Command.cs
│   ├── Update{Feature}Handler.cs
│   ├── Delete{Feature}Command.cs
│   └── Delete{Feature}Handler.cs
├── Queries/
│   ├── Get{Feature}sQuery.cs
│   ├── Get{Feature}sHandler.cs
│   ├── Get{Feature}ByIdQuery.cs
│   └── Get{Feature}ByIdHandler.cs
└── DTOs/
    ├── Create{Feature}Dto.cs
    ├── Update{Feature}Dto.cs
    ├── {Feature}Dto.cs
    └── {Feature}WithDetailsDto.cs (opcional)
```

### Ejemplo: Orders

```
Features/Orders/
├── Commands/
│   ├── CreateOrderCommand.cs
│   ├── CreateOrderHandler.cs
│   ├── UpdateOrderCommand.cs
│   ├── UpdateOrderHandler.cs
│   ├── ChangeOrderStatusCommand.cs
│   ├── ChangeOrderStatusHandler.cs
│   ├── AssignDeliveryManCommand.cs
│   ├── AssignDeliveryManHandler.cs
│   └── CancelOrderCommand.cs
├── Queries/
│   ├── GetOrdersQuery.cs
│   ├── GetOrdersHandler.cs
│   ├── GetOrderByIdQuery.cs
│   ├── GetOrderByIdHandler.cs
│   ├── GetOrderWithDetailsQuery.cs
│   ├── GetOrderWithDetailsHandler.cs
│   └── SearchOrdersQuery.cs
└── DTOs/
    ├── CreateOrderDto.cs
    ├── UpdateOrderDto.cs
    ├── OrderDto.cs
    ├── OrderWithDetailsDto.cs
    ├── ChangeOrderStatusDto.cs
    └── OrderSearchDto.cs
```

---

## DTOs vs Entities

### Cuándo Usar Cada Uno

| Capa | Usar | NO Usar |
|------|------|---------|
| Controllers | DTOs | Entities |
| Handlers | DTOs y Entities | - |
| Repositories | Entities | DTOs |
| Base de Datos | Entities | DTOs |

### DTOs (Data Transfer Objects)

**Propósito**: Contratos de la API (entrada/salida)

```csharp
// Para crear recursos
public class CreateOrderDto
{
    public int BranchId { get; set; }
    public string? GuestName { get; set; }
    public OrderType Type { get; set; }
    // ... solo campos necesarios para crear
}

// Para respuestas
public class OrderDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; }  // ← Campo calculado/navegado
    public string? GuestName { get; set; }
    // ... todos los campos para mostrar
}

// Para actualizar
public class UpdateOrderDto
{
    public string? GuestName { get; set; }
    public string? Notes { get; set; }
    // ... solo campos actualizables (todos opcionales)
}
```

### Entities

**Propósito**: Modelo del dominio y persistencia

```csharp
public class Order : BaseEntity
{
    // Propiedades del dominio
    public int BranchId { get; set; }
    public string? GuestName { get; set; }
    public OrderStatus Status { get; set; }
    
    // Navegaciones
    public virtual Branch Branch { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    
    // Métodos de dominio
    public void AddStatusTime(OrderStatus status, DateTime timestamp) { ... }
}
```

---

## Commands vs Queries

### Commands (Modifican Datos)

**Naming**: `{Action}{Resource}Command`

```csharp
CreateOrderCommand
UpdateOrderCommand
DeleteOrderCommand
ChangeOrderStatusCommand
AssignDeliveryManCommand
CancelOrderCommand
```

**Estructura típica**:
```csharp
public class CreateOrderCommand : IRequest<OrderDto>
{
    public CreateOrderDto Order { get; set; } = null!;
}
```

**Handler típico**:
```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CreateOrderCommand request, ...)
    {
        // 1. Validar
        // 2. Mapear DTO → Entity
        // 3. Aplicar lógica de negocio
        // 4. Guardar en repositorio
        // 5. Retornar DTO
    }
}
```

### Queries (Solo Lectura)

**Naming**: `Get{Resource}s{Filter?}Query`

```csharp
GetOrdersQuery
GetOrderByIdQuery
GetOrdersByStatusQuery
SearchOrdersQuery
GetAvailableOrdersForDeliveryQuery
```

**Estructura típica con paginación**:
```csharp
public class GetOrdersQuery : IRequest<PagedResult<OrderDto>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SortBy { get; set; }
    public string SortOrder { get; set; } = "asc";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
```

**Handler típico**:
```csharp
public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderDto>>
{
    public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, ...)
    {
        // 1. Determinar filtros (sucursal, fechas, etc.)
        // 2. Obtener datos del repositorio
        // 3. Aplicar filtros adicionales
        // 4. Paginar
        // 5. Mapear a DTOs
        // 6. Retornar PagedResult
    }
}
```

---

## Conversión de Enums

### Tres Formatos en el Sistema

```
C# (PascalCase)     JSON API (camelCase)    PostgreSQL (snake_case)
─────────────────   ────────────────────    ───────────────────────
OrderStatus.Taken   "taken"                 'taken'
InPreparation       "inPreparation"         'in_preparation'
Ready               "ready"                 'ready'
OnTheWay            "onTheWay"              'on_the_way'
Delivered           "delivered"             'delivered'
Cancelled           "cancelled"             'cancelled'
```

### Configuración

#### JSON (camelCase)
```csharp
// Program.cs
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
    );
});
```

#### PostgreSQL (snake_case)
```csharp
// OrderConfiguration.cs
builder.Property(o => o.Status).HasConversion(
    v => ToSnakeCase(v.ToString()),              // Write
    v => Enum.Parse<OrderStatus>(ToPascalCase(v), true)  // Read
);
```

---

## Paginación

### SIEMPRE Usar PagedResult

Para endpoints de listado, **siempre** retornar `PagedResult<T>`:

```csharp
// ✅ Correcto
public record GetItemsQuery(...) : IRequest<PagedResult<ItemDto>>;

// ❌ Incorrecto
public record GetItemsQuery(...) : IRequest<List<ItemDto>>;
public record GetItemsQuery(...) : IRequest<IEnumerable<ItemDto>>;
```

### Parámetros Estándar

```csharp
[FromQuery] int page = 1
[FromQuery] int pageSize = 10
[FromQuery] string? sortBy = null
[FromQuery] string sortOrder = "asc"
```

---

## Filtros por Defecto

### Filtro Automático por Sucursal

**Regla**: Los usuarios (excepto superadmin) solo ven datos de su sucursal

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

### Filtro Automático por Fecha Actual

Para pedidos, por defecto mostrar solo del día actual:

```csharp
var fromDate = request.FromDate ?? DateTime.UtcNow.Date;
var toDate = request.ToDate ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
```

---

## Navegaciones en Repositorios

### Incluir SIEMPRE Navegaciones Necesarias

```csharp
// ❌ Mal - AddressDescription llegará null
var query = _context.Orders
    .Include(o => o.Branch)
    .AsQueryable();

// ✅ Bien - Todas las navegaciones necesarias
var query = _context.Orders
    .Include(o => o.Branch)
    .Include(o => o.TakenBy)
    .Include(o => o.Customer)
    .Include(o => o.Address)        // ← Para AddressDescription
    .Include(o => o.LoyaltyRule)    // ← Para LoyaltyRuleName
    .Include(o => o.DeliveryMan)
    .AsQueryable();
```

### Navegaciones Profundas

```csharp
.Include(o => o.OrderDetails)
    .ThenInclude(od => od.Product)
    
.Include(o => o.BankPayments)
    .ThenInclude(bp => bp.Bank)
    
.Include(o => o.AppPayments)
    .ThenInclude(ap => ap.App)
        .ThenInclude(a => a.Bank)
```

---

## Repositorios

### Métodos Estándar

Todos los repositorios deben implementar:

```csharp
public interface IEntityRepository
{
    Task<Entity?> GetByIdAsync(int id);
    Task<PagedResult<Entity>> GetAllAsync(int page, int pageSize, ...);
    Task<Entity> CreateAsync(Entity entity);
    Task<Entity> UpdateAsync(Entity entity);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
}
```

### Métodos Específicos del Dominio

```csharp
// Ejemplo: OrderRepository
Task<Order> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? reason);
Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId);
Task<List<Order>> GetOrdersInPreparationAsync(int? branchId = null);
Task<bool> CanAssignDeliveryManAsync(int orderId, int deliveryManId);
```

---

## AutoMapper Profiles

### Un Profile por Feature

```
Mappings/
├── OrderMappingProfile.cs
├── UserMappingProfile.cs
├── CustomerMappingProfile.cs
├── BankPaymentMappingProfile.cs
└── AppPaymentMappingProfile.cs
```

### Mapeos Comunes

```csharp
public class OrderMappingProfile : Profile
{
    public OrderMappingProfile()
    {
        // Entity → DTO (respuestas)
        CreateMap<Order, OrderDto>()
            .ForMember(dest => dest.BranchName, 
                opt => opt.MapFrom(src => src.Branch.Name))
            .ForMember(dest => dest.StatusTimes, 
                opt => opt.MapFrom(src => src.GetStatusTimes()));

        // DTO → Entity (crear)
        CreateMap<CreateOrderDto, Order>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.Branch, opt => opt.Ignore());

        // DTO → Entity (actualizar)
        CreateMap<UpdateOrderDto, Order>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
    }
}
```

---

## Entity Framework Configurations

### Cada Entidad Tiene su Configuration

```
Infrastructure/Data/Configurations/
├── OrderConfiguration.cs
├── UserConfiguration.cs
├── CustomerConfiguration.cs
└── ...
```

### Estructura Típica

```csharp
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        // 1. Tabla
        builder.ToTable("order");
        
        // 2. Primary Key
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        
        // 3. Propiedades
        builder.Property(o => o.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(o => o.GuestName).HasColumnName("guestname").HasMaxLength(100);
        
        // 4. Conversiones (enums)
        builder.Property(o => o.Status).HasConversion(
            v => ToSnakeCase(v.ToString()),
            v => Enum.Parse<OrderStatus>(ToPascalCase(v), true)
        ).IsRequired();
        
        // 5. Timestamps
        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        
        // 6. Relaciones
        builder.HasOne(o => o.Branch)
            .WithMany(b => b.Orders)
            .HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
        
        // 7. Índices
        builder.HasIndex(o => o.BranchId).HasDatabaseName("idx_order_branch");
        builder.HasIndex(o => o.Status).HasDatabaseName("idx_order_status");
    }
}
```

---

## Campos Opcionales vs Requeridos

### En DTOs

#### CreateDto
```csharp
// Campos requeridos (no nullable)
public int BranchId { get; set; }
public int TakenById { get; set; }
public OrderType Type { get; set; }

// Campos opcionales (nullable)
public int? CustomerId { get; set; }
public int? AddressId { get; set; }
public string? GuestName { get; set; }
public string? Notes { get; set; }

// Campos opcionales que el backend establece
public int? Subtotal { get; set; }  // Se calcula automáticamente en BD
public int? Total { get; set; }     // Se calcula automáticamente en BD
```

#### UpdateDto
```csharp
// TODOS los campos opcionales (solo enviar lo que cambia)
public int? CustomerId { get; set; }
public string? GuestName { get; set; }
public string? Notes { get; set; }
```

### En Entities

```csharp
// Campos requeridos
public int BranchId { get; set; }
public int TakenById { get; set; }
public OrderStatus Status { get; set; }

// Campos opcionales
public int? CustomerId { get; set; }
public int? AddressId { get; set; }
public string? GuestName { get; set; }
```

---

## Controllers

### Estructura Estándar

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]  // ← Requiere autenticación por defecto
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Descripción del endpoint
    /// </summary>
    /// <param name="paramName">Descripción del parámetro</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new GetOrdersQuery { Page = page, PageSize = pageSize };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] CreateOrderDto dto)
    {
        var command = new CreateOrderCommand { Order = dto };
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetOrder), new { id = result.Id }, result);
    }
}
```

### Verbos HTTP

| Operación | Verbo | Ejemplo |
|-----------|-------|---------|
| Listar | GET | `GET /api/orders` |
| Obtener | GET | `GET /api/orders/{id}` |
| Crear | POST | `POST /api/orders` |
| Actualizar completo | PUT | `PUT /api/orders/{id}` |
| Actualizar parcial | PATCH | `PATCH /api/orders/{id}` (no usado) |
| Eliminar | DELETE | `DELETE /api/orders/{id}` |
| Acción específica | POST/PUT | `PUT /api/orders/{id}/status` |

---

## Validaciones

### Ubicación de Validaciones

| Tipo de Validación | Ubicación | Ejemplo |
|-------------------|-----------|---------|
| Formato de datos | Data Annotations en DTO | `[Required]`, `[Range]` |
| Lógica simple | Handler directo | `if (value == null) throw...` |
| Lógica compleja | Business Service | `_businessRules.CanUpdate()` |
| Permisos | Handler con CurrentUser | `if (role != "admin") throw...` |

### Ejemplo Combinado

```csharp
// 1. Data Annotations (automáticas)
public class CreateDto
{
    [Required(ErrorMessage = "El campo es requerido")]
    public string Name { get; set; }
}

// 2. Validaciones en Handler
public async Task<Result> Handle(...)
{
    // Validación simple
    if (string.IsNullOrWhiteSpace(request.GuestName))
        throw new BusinessException("El nombre del invitado es requerido");
    
    // Validación de permisos
    if (_currentUser.Role != "admin")
        throw new BusinessException("No tienes permisos");
    
    // Validación compleja (business rules)
    if (!_businessRules.CanUpdateOrder(order, _currentUser.Role))
        throw new BusinessException("No puedes modificar este pedido");
}
```

---

## Async/Await

### SIEMPRE Usar Async

```csharp
// ✅ Correcto
public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken ct)
{
    var order = await _repository.CreateAsync(order);
    return _mapper.Map<OrderDto>(order);
}

// ❌ Incorrecto
public OrderDto Handle(CreateOrderCommand request, CancellationToken ct)
{
    var order = _repository.CreateAsync(order).Result;  // ← NO hacer
    return _mapper.Map<OrderDto>(order);
}
```

### CancellationToken

Siempre aceptar `CancellationToken` y pasarlo a métodos async:

```csharp
public async Task<Result> Handle(Query request, CancellationToken cancellationToken)
{
    var items = await _repository.GetAllAsync(cancellationToken);
    // ...
}
```

---

## Dependency Injection

### Registro por Capa

#### Application
```csharp
// Application/DependencyInjection.cs
services.AddSingleton<IMapper>(mapper);
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));
services.AddScoped<IOrderBusinessRulesService, OrderBusinessRulesService>();
```

#### Infrastructure
```csharp
// Infrastructure/DependencyInjection.cs
services.AddDbContext<ApplicationDbContext>(...);
services.AddScoped<IOrderRepository, OrderRepository>();
services.AddScoped<ICurrentUser, CurrentUserService>();
services.AddScoped<IJwtService, JwtService>();
```

### Lifetimes

| Lifetime | Uso | Ejemplo |
|----------|-----|---------|
| Transient | Nueva instancia cada vez | - |
| Scoped | Una instancia por request | Repositories, Handlers, CurrentUser |
| Singleton | Una instancia para toda la app | Mapper, Configuración |

---

## Resumen de Convenciones Críticas

1. **Enums**: PascalCase en C#, camelCase en JSON, snake_case en DB
2. **DTOs**: Siempre para API, nunca exponer entities
3. **Paginación**: Siempre PagedResult para listados
4. **Sucursal**: Siempre filtrar por BranchId (excepto superadmin)
5. **Excepciones**: Siempre usar las personalizadas
6. **Async**: Siempre async/await
7. **Navegaciones**: Siempre .Include() para propiedades navegadas
8. **Timestamps**: Nunca establecer manualmente CreatedAt/UpdatedAt
9. **UTC**: Todos los DateTime en UTC
10. **Validaciones**: En el orden: DTO → Handler → Business Rules

