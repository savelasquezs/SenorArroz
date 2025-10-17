# Flujo de Datos - CQRS con MediatR

## Patrón CQRS

**CQRS** (Command Query Responsibility Segregation) separa las operaciones de lectura y escritura:

- **Commands**: Modifican datos (Create, Update, Delete)
- **Queries**: Solo leen datos (Get, List, Search)

## Flujo Completo de un Command

### Ejemplo: Crear un Pedido

```
1. Frontend
   POST /api/orders
   Body: { "branchId": 9, "type": "delivery", ... }
                 ↓
2. Controller (OrdersController.cs)
   [HttpPost]
   public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] CreateOrderDto orderDto)
   {
       var command = new CreateOrderCommand { Order = orderDto };
       var result = await _mediator.Send(command);  ← Envía a MediatR
       return CreatedAtAction(nameof(GetOrder), new { id = result.Id }, result);
   }
                 ↓
3. MediatR
   Encuentra el handler registrado para CreateOrderCommand
                 ↓
4. Handler (CreateOrderHandler.cs)
   public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
   {
       public async Task<OrderDto> Handle(CreateOrderCommand request, ...)
       {
           // 4.1 Validaciones de negocio
           if (request.Order.Type == OrderType.Delivery)
           {
               if (!request.Order.CustomerId.HasValue)
                   throw new BusinessException("Los pedidos de domicilio requieren un cliente");
           }
           
           // 4.2 Mapear DTO → Entity
           var order = _mapper.Map<Order>(request.Order);
           
           // 4.3 Establecer valores
           order.Status = OrderStatus.Taken;
           order.AddStatusTime(OrderStatus.Taken, DateTime.UtcNow);
           
           // 4.4 Guardar en repositorio
           var createdOrder = await _orderRepository.CreateAsync(order);
           
           // 4.5 Mapear Entity → DTO
           return _mapper.Map<OrderDto>(createdOrder);
       }
   }
                 ↓
5. Repository (OrderRepository.cs)
   public async Task<Order> CreateAsync(Order order)
   {
       _context.Orders.Add(order);
       await _context.SaveChangesAsync();
       return order;
   }
                 ↓
6. Entity Framework Core
   - Aplica conversiones de enums (InPreparation → in_preparation)
   - Convierte DateTime a UTC
   - Genera SQL
                 ↓
7. PostgreSQL
   INSERT INTO "order" (branch_id, status, type, guestname, ...)
   VALUES (9, 'taken', 'delivery', 'Santiago', ...)
   RETURNING id, created_at, updated_at, ...
                 ↓
8. Respuesta
   Entity Framework mapea resultado → Order entity
                 ↓
9. AutoMapper
   Order entity → OrderDto
                 ↓
10. Controller
    Retorna 201 Created con OrderDto en el body
                 ↓
11. Frontend
    Recibe: { "id": 123, "status": "taken", "guestName": "Santiago", ... }
```

---

## Flujo Completo de un Query

### Ejemplo: Obtener Pedidos del Día

```
1. Frontend
   GET /api/orders?page=1&pageSize=10
                 ↓
2. Controller (OrdersController.cs)
   [HttpGet]
   public async Task<ActionResult<PagedResult<OrderDto>>> GetOrders(
       [FromQuery] int page = 1,
       [FromQuery] int pageSize = 10,
       [FromQuery] DateTime? fromDate = null,
       [FromQuery] DateTime? toDate = null)
   {
       var query = new GetOrdersQuery 
       { 
           Page = page, 
           PageSize = pageSize,
           FromDate = fromDate,
           ToDate = toDate
       };
       var result = await _mediator.Send(query);  ← Envía a MediatR
       return Ok(result);
   }
                 ↓
3. MediatR
   Encuentra el handler registrado para GetOrdersQuery
                 ↓
4. Handler (GetOrdersHandler.cs)
   public async Task<PagedResult<OrderDto>> Handle(GetOrdersQuery request, ...)
   {
       // 4.1 Determinar filtro de sucursal
       int? branchFilter = null;
       if (_currentUser.Role != "superadmin")
       {
           branchFilter = _currentUser.BranchId;  // Solo su sucursal
       }
       
       // 4.2 Establecer filtros de fecha por defecto (día actual)
       var fromDate = request.FromDate ?? DateTime.UtcNow.Date;
       var toDate = request.ToDate ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
       
       // 4.3 Obtener del repositorio
       var result = await _orderRepository.GetAllAsync(
           request.Page, 
           request.PageSize, 
           request.SortBy, 
           request.SortOrder
       );
       
       // 4.4 Aplicar filtros
       var filteredItems = result.Items.AsQueryable();
       
       if (branchFilter.HasValue)
           filteredItems = filteredItems.Where(o => o.BranchId == branchFilter.Value);
       
       filteredItems = filteredItems.Where(o => 
           o.CreatedAt >= fromDate && o.CreatedAt <= toDate);
       
       var filteredList = filteredItems.ToList();
       
       // 4.5 Mapear a DTOs
       return new PagedResult<OrderDto>
       {
           Items = _mapper.Map<List<OrderDto>>(filteredList),
           TotalCount = filteredList.Count,
           Page = request.Page,
           PageSize = request.PageSize,
           TotalPages = (int)Math.Ceiling((double)filteredList.Count / request.PageSize)
       };
   }
                 ↓
5. Repository (OrderRepository.cs)
   public async Task<PagedResult<Order>> GetAllAsync(...)
   {
       var query = _context.Orders
           .Include(o => o.Branch)           // ← Navegaciones necesarias
           .Include(o => o.TakenBy)
           .Include(o => o.Customer)
           .Include(o => o.Address)          // ← Para addressDescription
           .Include(o => o.LoyaltyRule)      // ← Para loyaltyRuleName
           .Include(o => o.DeliveryMan)
           .AsQueryable();
       
       // Paginación
       var items = await query
           .Skip((page - 1) * pageSize)
           .Take(pageSize)
           .ToListAsync();
       
       return new PagedResult<Order> { Items = items, ... };
   }
                 ↓
6. Entity Framework Core
   SELECT o.id, o.branch_id, o.status, o.type, ...
          b.name as branch_name,
          c.name as customer_name,
          a.address as address_text
   FROM "order" o
   LEFT JOIN branch b ON o.branch_id = b.id
   LEFT JOIN customer c ON o.customer_id = c.id
   LEFT JOIN address a ON o.address_id = a.id
   WHERE o.created_at >= '2024-10-17' AND o.created_at <= '2024-10-17 23:59:59'
   LIMIT 10 OFFSET 0;
                 ↓
7. PostgreSQL
   Ejecuta query y retorna resultados
                 ↓
8. Entity Framework
   Materializa Order entities con navegaciones cargadas
                 ↓
9. AutoMapper
   Order → OrderDto
   Mapeos especiales:
   - BranchName ← Branch.Name
   - CustomerName ← Customer.Name
   - AddressDescription ← Address.AddressText  ← Por esto necesitamos Include
   - LoyaltyRuleName ← LoyaltyRule.Description
   - StatusTimes ← order.GetStatusTimes()
                 ↓
10. Controller
    Retorna 200 OK con PagedResult<OrderDto>
                 ↓
11. Frontend
    Recibe:
    {
      "items": [
        {
          "id": 123,
          "branchName": "Sucursal Centro",
          "customerName": "Juan Pérez",
          "addressDescription": "Calle 45 #23-10",  ← Viene del Include
          "status": "taken",
          ...
        }
      ],
      "totalCount": 5,
      "page": 1,
      "pageSize": 10,
      "totalPages": 1
    }
```

---

## AutoMapper - Conversiones DTO ↔ Entity

### Entity → DTO (Para Respuestas)

```csharp
// OrderMappingProfile.cs
CreateMap<Order, OrderDto>()
    .ForMember(dest => dest.BranchName, 
        opt => opt.MapFrom(src => src.Branch.Name))
    .ForMember(dest => dest.AddressDescription, 
        opt => opt.MapFrom(src => src.Address != null ? src.Address.AddressText : null))
    .ForMember(dest => dest.StatusTimes, 
        opt => opt.MapFrom(src => src.GetStatusTimes()));
```

**Importante**: 
- Requiere `.Include()` en el repositorio para navegaciones
- Sin Include, las propiedades navegadas serán null

### DTO → Entity (Para Crear/Actualizar)

```csharp
CreateMap<CreateOrderDto, Order>()
    .ForMember(dest => dest.Id, opt => opt.Ignore())
    .ForMember(dest => dest.Status, opt => opt.Ignore())  // Se establece en el handler
    .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
    .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
    .ForMember(dest => dest.Branch, opt => opt.Ignore())  // Solo FKs, no navegaciones
    .ForMember(dest => dest.Customer, opt => opt.Ignore());
```

---

## Repositorios - Includes Importantes

### Navegaciones Estándar para Orders

```csharp
var query = _context.Orders
    .Include(o => o.Branch)         // Para BranchName
    .Include(o => o.TakenBy)        // Para TakenByName
    .Include(o => o.Customer)       // Para CustomerName y CustomerPhone
    .Include(o => o.Address)        // Para AddressDescription ← CRÍTICO
    .Include(o => o.LoyaltyRule)    // Para LoyaltyRuleName ← CRÍTICO
    .Include(o => o.DeliveryMan)    // Para DeliveryManName
    .AsQueryable();
```

**Sin estos Includes**, los campos derivados llegarán null al frontend.

### Navegaciones para Detalles

```csharp
var order = await _context.Orders
    .Include(o => o.Branch)
    .Include(o => o.TakenBy)
    .Include(o => o.Customer)
    .Include(o => o.Address)
    .Include(o => o.LoyaltyRule)
    .Include(o => o.DeliveryMan)
    .Include(o => o.OrderDetails)           // Lista de productos
        .ThenInclude(od => od.Product)      // Detalles de cada producto
    .Include(o => o.BankPayments)           // Pagos bancarios
        .ThenInclude(bp => bp.Bank)
    .Include(o => o.AppPayments)            // Pagos por app
        .ThenInclude(ap => ap.App)
            .ThenInclude(a => a.Bank)
    .FirstOrDefaultAsync(o => o.Id == id);
```

---

## Paginación Estándar

### PagedResult<T>

```csharp
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
```

### Implementación en Handler

```csharp
// 1. Obtener datos
var allItems = await _repository.GetAllAsync();

// 2. Aplicar filtros
var filtered = allItems.Where(x => x.Active == true);

// 3. Contar total
var totalCount = filtered.Count();

// 4. Paginar
var paginatedItems = filtered
    .Skip((request.Page - 1) * request.PageSize)
    .Take(request.PageSize)
    .ToList();

// 5. Retornar PagedResult
return new PagedResult<ItemDto>
{
    Items = _mapper.Map<List<ItemDto>>(paginatedItems),
    TotalCount = totalCount,
    Page = request.Page,
    PageSize = request.PageSize,
    TotalPages = (int)Math.Ceiling((double)totalCount / request.PageSize)
};
```

---

## Conversión de Enums

### Problema: Tres Formatos Diferentes

| Capa | Formato | Ejemplo |
|------|---------|---------|
| C# Code | PascalCase | `OrderStatus.InPreparation` |
| PostgreSQL | snake_case | `'in_preparation'` |
| JSON API | camelCase | `"inPreparation"` |

### Solución Implementada

#### 1. JSON API (Program.cs)
```csharp
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
    );
});
```

**Resultado**: Enums se serializan como camelCase
```json
{ "status": "inPreparation", "type": "delivery" }
```

#### 2. Entity Framework (OrderConfiguration.cs)
```csharp
builder.Property(o => o.Status).HasConversion(
    v => ToSnakeCase(v.ToString()),              // InPreparation → in_preparation
    v => Enum.Parse<OrderStatus>(ToPascalCase(v), true) // in_preparation → InPreparation
);

// Métodos helper
private static string ToSnakeCase(string input)
{
    // InPreparation → in_preparation
}

private static string ToPascalCase(string input)
{
    // in_preparation → InPreparation
}
```

**Resultado**: Enums se guardan como snake_case en DB

### Flujo Completo de Conversión

```
Frontend envía:        "inPreparation" (camelCase - JSON)
        ↓
ASP.NET deserializa:   OrderStatus.InPreparation (enum)
        ↓
Handler procesa:       OrderStatus.InPreparation (enum)
        ↓
EF Core convierte:     "in_preparation" (snake_case)
        ↓
PostgreSQL guarda:     in_preparation
        ↓
PostgreSQL lee:        in_preparation
        ↓
EF Core convierte:     "InPreparation" (PascalCase)
        ↓
Enum.Parse:            OrderStatus.InPreparation (enum)
        ↓
AutoMapper mapea:      OrderStatus.InPreparation (enum)
        ↓
ASP.NET serializa:     "inPreparation" (camelCase - JSON)
        ↓
Frontend recibe:       "inPreparation"
```

---

## Timestamps Automáticos

### CreatedAt y UpdatedAt

**NO se establecen manualmente** en el código. PostgreSQL los maneja con triggers/defaults:

```csharp
builder.Property(o => o.CreatedAt)
    .HasColumnName("created_at")
    .HasDefaultValueSql("NOW()")
    .ValueGeneratedOnAdd()
    .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

builder.Property(o => o.UpdatedAt)
    .HasColumnName("updated_at")
    .HasDefaultValueSql("NOW()")
    .ValueGeneratedOnAddOrUpdate()
    .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
```

### Conversión Automática a UTC

El `ApplicationDbContext` convierte **todos** los DateTime a UTC antes de guardar:

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    ConvertDateTimesToUtc();  // Convierte automáticamente
    return await base.SaveChangesAsync(cancellationToken);
}

private void ConvertDateTimesToUtc()
{
    foreach (var entry in ChangeTracker.Entries())
    {
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.Name == "CreatedAt" || property.Metadata.Name == "UpdatedAt")
                continue;  // Estos los maneja la BD

            if (property.CurrentValue is DateTime dateTime && dateTime.Kind != DateTimeKind.Utc)
            {
                property.CurrentValue = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
            }
        }
    }
}
```

---

## Ejemplo Completo: Update con Validaciones

### UpdateOrderHandler.cs

```csharp
public class UpdateOrderHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderBusinessRulesService _businessRules;

    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken ct)
    {
        // 1. Obtener orden existente
        var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
        if (existingOrder == null)
            throw new NotFoundException("Pedido no encontrado");

        // 2. Validar acceso a sucursal
        if (_currentUser.Role != "superadmin" && 
            existingOrder.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");
        }

        // 3. Validar según estado del pedido y rol
        if (!_businessRules.CanUpdateOrder(existingOrder, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar este pedido");

        // 4. Validar modificación de productos
        if (request.Order.OrderDetails != null && 
            !_businessRules.CanUpdateOrderProducts(existingOrder, _currentUser.Role))
        {
            throw new BusinessException("No tienes permisos para modificar los productos");
        }

        // 5. Mapear cambios (solo campos no-null del DTO)
        _mapper.Map(request.Order, existingOrder);
        
        // 6. Actualizar en repositorio
        var updatedOrder = await _orderRepository.UpdateAsync(existingOrder);
        
        // 7. Retornar DTO
        return _mapper.Map<OrderDto>(updatedOrder);
    }
}
```

---

## MediatR - Registro Automático

MediatR auto-registra todos los handlers en el assembly:

```csharp
// Application/DependencyInjection.cs
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
});
```

**No necesitas** registrar manualmente cada handler. MediatR encuentra automáticamente:
- Todas las clases que implementan `IRequestHandler<TRequest, TResponse>`
- En el ensamblado de Application

---

## Resumen de Flujo

### Command Flow
```
Controller → MediatR → Handler → Repository → EF Core → PostgreSQL
                                      ↓
                                   Mapper
                                      ↓
                                  DTO Response
```

### Query Flow
```
Controller → MediatR → Handler → Repository → EF Core → PostgreSQL
                                      ↓
                                   Filters
                                      ↓
                                  Pagination
                                      ↓
                                   Mapper
                                      ↓
                              PagedResult<DTO>
```

### Puntos Clave
1. **Controllers**: Solo routing, NO lógica de negocio
2. **Handlers**: Toda la lógica de negocio y orquestación
3. **Repositories**: Solo acceso a datos
4. **Mappers**: Conversiones automáticas
5. **Middleware**: Manejo transversal (auth, errors)

