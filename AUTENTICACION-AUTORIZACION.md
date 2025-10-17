# Autenticación y Autorización

## Sistema de Autenticación JWT

El proyecto usa **JSON Web Tokens (JWT)** para autenticación stateless.

## Flujo de Autenticación

```
1. Login
   POST /api/auth/login
   { "email": "user@example.com", "password": "******" }
            ↓
2. LoginHandler
   - Valida credenciales
   - Genera access token (JWT)
   - Genera refresh token
   - Retorna ambos tokens
            ↓
3. Frontend recibe
   {
     "accessToken": "eyJhbGc...",
     "refreshToken": "abc123...",
     "expiresIn": 3600,
     "user": { ... }
   }
            ↓
4. Frontend guarda tokens
   - accessToken en memoria o localStorage
   - refreshToken en httpOnly cookie (recomendado)
            ↓
5. Requests subsecuentes
   Authorization: Bearer eyJhbGc...
            ↓
6. JwtMiddleware
   - Extrae token del header
   - Valida firma y expiración
   - Carga usuario actual
   - Adjunta a HttpContext
            ↓
7. CurrentUserService
   - Lee claims del token
   - Expone: Id, Role, BranchId
            ↓
8. Handler usa CurrentUser
   var userId = _currentUser.Id;
   var role = _currentUser.Role;
   var branchId = _currentUser.BranchId;
```

---

## JWT Service

### Generación de Tokens

```csharp
// JwtService.cs
public string GenerateAccessToken(User user)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role.ToString().ToLower()),
        new Claim("branch_id", user.BranchId.ToString()),  ← Para filtros
        new Claim("name", user.Name)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: _issuer,
        audience: _audience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

### Claims en el Token

| Claim | Uso | Ejemplo |
|-------|-----|---------|
| `NameIdentifier` | ID del usuario | `"25"` |
| `Email` | Email del usuario | `"admin@example.com"` |
| `Role` | Rol del usuario | `"admin"` |
| `branch_id` | Sucursal del usuario | `"9"` |
| `name` | Nombre del usuario | `"María González"` |

---

## CurrentUserService

### Interfaz

```csharp
public interface ICurrentUser
{
    int Id { get; }              // ID del usuario logueado
    string Role { get; }         // Rol: "admin", "cashier", "superadmin"
    int BranchId { get; }        // Sucursal del usuario
    bool IsAuthenticated { get; } // Usuario autenticado?
}
```

### Implementación

```csharp
public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public int Id => int.Parse(
        _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0"
    );

    public string Role => _httpContextAccessor.HttpContext?.User
        ?.FindFirst(ClaimTypes.Role)?.Value?.ToLower() ?? string.Empty;

    public int BranchId
    {
        get
        {
            var branchClaim = _httpContextAccessor.HttpContext?.User
                ?.FindFirst("branch_id")?.Value;
            return branchClaim != null ? int.Parse(branchClaim) : 0;
        }
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User
        ?.Identity?.IsAuthenticated ?? false;
}
```

### Uso en Handlers

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly ICurrentUser _currentUser;

    public async Task<OrderDto> Handle(CreateOrderCommand request, ...)
    {
        // Determinar sucursal según rol
        int branchId;

        if (_currentUser.Role == "superadmin")
        {
            // Superadmin puede especificar cualquier sucursal
            if (request.Order.BranchId <= 0)
                throw new BusinessException("Superadmin debe especificar la sucursal");
            
            branchId = request.Order.BranchId;
        }
        else if (_currentUser.Role == "admin" || _currentUser.Role == "cashier")
        {
            // Admin y Cashier usan su propia sucursal
            branchId = _currentUser.BranchId;  ← Filtro automático
        }
        else
        {
            throw new BusinessException("No tienes permisos para crear pedidos");
        }

        order.BranchId = branchId;
        // ...
    }
}
```

---

## Middleware Pipeline

### Orden de Ejecución

```csharp
// Program.cs
app.UseMiddleware<GlobalExceptionMiddleware>();  // 1. Captura errores
app.UseHttpsRedirection();                       // 2. Redirect HTTPS
app.UseCors("AllowAll");                         // 3. CORS
app.UseAuthentication();                         // 4. JWT Authentication
app.UseAuthorization();                          // 5. Autorización por roles
app.UseMiddleware<JwtMiddleware>();              // 6. Cargar CurrentUser
app.MapControllers();                            // 7. Routing a controllers
```

### JwtMiddleware (Custom)

**Propósito**: Verificar que el usuario sigue activo en la BD

```csharp
public async Task Invoke(HttpContext context, IAuthRepository authRepository)
{
    var token = context.Request.Headers.Authorization
        .FirstOrDefault()?.Split(" ").Last();

    if (token != null)
    {
        // Validar token
        var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
        
        // Verificar usuario activo en BD
        var userId = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier).Value);
        var user = await authRepository.GetUserByIdWithBranchAsync(userId);
        
        if (user != null && user.Active)
        {
            context.Items["User"] = user;      // Adjuntar al contexto
            context.Items["UserId"] = userId;
        }
    }

    await _next(context);
}
```

**Ventaja**: Invalida tokens si el usuario fue desactivado, aunque el token no haya expirado.

---

## Autorización por Roles

### A Nivel de Controller

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]  // ← Requiere autenticación para todo el controller
public class OrdersController : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]  // ← Solo estos roles
    public async Task<ActionResult<OrderDto>> CreateOrder([FromBody] CreateOrderDto dto)
    {
        // ...
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]  // ← Solo Admin y Superadmin
    public async Task<ActionResult> DeleteOrder(int id)
    {
        // ...
    }
}
```

### A Nivel de Handler (Lógica Compleja)

```csharp
public async Task<OrderDto> Handle(UpdateOrderCommand request, ...)
{
    var order = await _orderRepository.GetByIdAsync(request.Id);

    // Validación por rol y estado
    if (!_businessRules.CanUpdateOrder(order, _currentUser.Role))
        throw new BusinessException("No tienes permisos...");

    // Validación específica de productos
    if (request.Order.OrderDetails != null && 
        !_businessRules.CanUpdateOrderProducts(order, _currentUser.Role))
    {
        throw new BusinessException("No puedes modificar productos...");
    }

    // ...
}
```

---

## Filtros Automáticos por Sucursal

### Patrón Estándar

**TODOS los handlers de lectura** deben filtrar por sucursal:

```csharp
public async Task<PagedResult<ItemDto>> Handle(GetItemsQuery request, ...)
{
    // Determinar filtro de sucursal
    int? branchFilter = null;
    
    if (_currentUser.Role != "superadmin")
    {
        // Usuarios normales solo ven datos de su sucursal
        branchFilter = _currentUser.BranchId;
    }
    else if (request.BranchId.HasValue)
    {
        // Superadmin puede filtrar por sucursal específica
        branchFilter = request.BranchId;
    }

    // Obtener datos filtrados
    var items = await _repository.GetAllAsync(branchFilter, ...);
    
    // ...
}
```

### Validación en Modificaciones

```csharp
public async Task<ItemDto> Handle(UpdateItemCommand request, ...)
{
    var item = await _repository.GetByIdAsync(request.Id);

    // Validar acceso a sucursal
    if (_currentUser.Role != "superadmin" && 
        item.BranchId != _currentUser.BranchId)
    {
        throw new BusinessException("No tienes permisos para modificar items de esta sucursal");
    }

    // ...
}
```

---

## Refresh Tokens

### Flujo de Renovación

```
1. Access token expira
            ↓
2. Frontend detecta 401
            ↓
3. POST /api/auth/refresh
   { "refreshToken": "abc123..." }
            ↓
4. RefreshTokenHandler
   - Valida refresh token
   - Verifica que no esté revocado
   - Genera nuevo access token
   - Opcionalmente rota refresh token
            ↓
5. Retorna nuevo access token
   {
     "accessToken": "eyJhbGc...",
     "refreshToken": "xyz789...",  // Nuevo (rotación)
     "expiresIn": 3600
   }
```

### Revocación de Tokens

```csharp
// Logout
POST /api/auth/logout
{
    "refreshToken": "abc123..."
}
```

Handler marca el refresh token como revocado en la BD.

### Limpieza Automática

**TokenCleanupService** (background service):
- Ejecuta cada 24 horas
- Elimina refresh tokens expirados
- Elimina password reset tokens expirados

---

## Matriz de Permisos por Endpoint

### Pedidos

| Endpoint | Superadmin | Admin | Cashier | Kitchen | Deliveryman |
|----------|------------|-------|---------|---------|-------------|
| POST /orders | ✅ | ✅ | ✅ | ❌ | ❌ |
| PUT /orders/{id} | ✅ | ✅ | ✅* | ❌ | ❌ |
| PUT /orders/{id}/status | ✅ | ✅ | ⚠️ | ⚠️ | ❌ |
| PUT /orders/{id}/cancel | ✅ | ✅ | ❌ | ❌ | ❌ |
| DELETE /orders/{id} | ✅ | ✅ | ❌ | ❌ | ❌ |

*Con restricciones según estado del pedido

### Pagos

| Endpoint | Superadmin | Admin | Cashier | Kitchen | Deliveryman |
|----------|------------|-------|---------|---------|-------------|
| POST /bank-payments | ✅ | ✅ | ❌ | ❌ | ❌ |
| PUT /bank-payments/{id} | ✅ | ✅* | ✅* | ❌ | ❌ |
| DELETE /bank-payments/{id} | ✅ | ✅ | ❌ | ❌ | ❌ |
| POST /bank-payments/{id}/verify | ✅ | ✅ | ❌ | ❌ | ❌ |

*Solo mismo día

---

## Configuración JWT

### appsettings.json

```json
{
  "JwtSettings": {
    "SecretKey": "tu-clave-secreta-muy-larga-y-segura-minimo-32-caracteres",
    "Issuer": "SenorArrozAPI",
    "Audience": "SenorArrozClient",
    "ExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 7
  }
}
```

### Configuración en Program.cs

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero  // Sin tolerancia de expiración
    };
});
```

---

## Respuestas de Autenticación

### Login Exitoso (200 OK)

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "550e8400-e29b-41d4-a716-446655440000",
  "expiresIn": 3600,
  "user": {
    "id": 25,
    "name": "María González",
    "email": "maria@example.com",
    "role": "admin",
    "branchId": 9,
    "branchName": "Sucursal Centro"
  }
}
```

### Login Fallido (401 Unauthorized)

```json
{
  "success": false,
  "message": "Credenciales inválidas",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

### Token Expirado (401 Unauthorized)

```json
{
  "error": "Unauthorized",
  "message": "Token inválido o expirado"
}
```

Headers adicionales:
```
Token-Expired: true
```

---

## Seguridad

### Contraseñas

**NUNCA** guardar contraseñas en texto plano. Usar PasswordService:

```csharp
// Al crear usuario
var passwordHash = _passwordService.HashPassword(password);
user.PasswordHash = passwordHash;

// Al validar login
var isValid = _passwordService.VerifyPassword(password, user.PasswordHash);
```

Implementación usa **BCrypt** con salt automático.

### Refresh Tokens

- Almacenados hasheados en BD
- One-time use (rotación)
- Expiración de 7 días
- Pueden ser revocados (logout)
- Limpieza automática de expirados

### Password Reset

1. Usuario solicita reset: `POST /api/auth/forgot-password`
2. Sistema genera token temporal (válido 1 hora)
3. Envía email con link
4. Usuario usa token: `POST /api/auth/reset-password`
5. Token se marca como usado y ya no funciona

---

## Validaciones de Permisos en Handlers

### Patrón Estándar

```csharp
public async Task<ResultDto> Handle(SomeCommand request, ...)
{
    // 1. Obtener recurso
    var resource = await _repository.GetByIdAsync(request.Id);
    if (resource == null)
        throw new NotFoundException("Recurso no encontrado");

    // 2. Validar acceso a sucursal
    if (_currentUser.Role != "superadmin" && 
        resource.BranchId != _currentUser.BranchId)
    {
        throw new BusinessException("No tienes permisos para acceder a recursos de esta sucursal");
    }

    // 3. Validar permisos por rol
    if (!new[] { "admin", "superadmin", "cashier" }.Contains(_currentUser.Role.ToLower()))
    {
        throw new BusinessException("No tienes permisos para esta operación");
    }

    // 4. Validaciones específicas de negocio
    if (!_businessRules.CanPerformAction(resource, _currentUser.Role))
    {
        throw new BusinessException("No puedes realizar esta acción en el estado actual");
    }

    // 5. Proceder con la operación
    // ...
}
```

---

## Business Rules Service

### OrderBusinessRulesService

Servicio especializado para validaciones complejas de pedidos:

```csharp
public interface IOrderBusinessRulesService
{
    bool CanUpdateOrder(Order order, string userRole);
    bool CanUpdateOrderProducts(Order order, string userRole);
    bool CanModifyPayments(Order order, string userRole);
    bool IsStatusTransitionValid(OrderStatus current, OrderStatus next, string role);
    bool IsSameDay(DateTime orderCreatedAt);
}
```

### Uso en Handlers

```csharp
private readonly IOrderBusinessRulesService _businessRules;

public async Task<OrderDto> Handle(...)
{
    // Validar según reglas de negocio complejas
    if (!_businessRules.CanUpdateOrder(order, _currentUser.Role))
        throw new BusinessException("No tienes permisos para modificar este pedido");

    if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
        throw new BusinessException("No puedes modificar pagos de este pedido");

    // Solo superadmin puede modificar pedidos de días anteriores
    if (!_businessRules.IsSameDay(order.CreatedAt) && _currentUser.Role != "superadmin")
        throw new BusinessException("Solo se pueden modificar pedidos del día actual");
}
```

---

## Roles y Permisos del Sistema

### Superadmin
- ✅ Control total del sistema
- ✅ Puede ver/modificar todas las sucursales
- ✅ Puede revertir estados
- ✅ Puede modificar datos de días anteriores

### Admin
- ✅ Control de su sucursal
- ✅ Puede ver/modificar solo su sucursal
- ✅ Puede revertir estados
- ⚠️ Solo puede modificar datos del día actual

### Cashier (Cajero)
- ⚠️ Operaciones de caja en su sucursal
- ❌ No puede revertir estados (solo avanzar)
- ❌ No puede modificar productos de pedidos entregados
- ⚠️ Solo puede modificar pagos del día actual

### Kitchen (Cocina)
- ⚠️ Solo puede cambiar estados de preparación
- ❌ No puede modificar pedidos ni pagos
- ✅ Solo ve pedidos de su sucursal

### Deliveryman (Domiciliario)
- ⚠️ Solo auto-asignación de pedidos
- ❌ No puede usar endpoint general de cambio de estado
- ✅ Solo ve pedidos asignados a él

---

## Casos Especiales

### Superadmin sin Sucursal

Algunos superadmins podrían no tener sucursal asignada:

```csharp
if (_currentUser.Role == "superadmin")
{
    // Superadmin DEBE especificar sucursal en operaciones de creación
    if (request.BranchId <= 0)
        throw new BusinessException("Superadmin debe especificar la sucursal");
}
```

### Validaciones Temporales

Muchas operaciones solo se permiten el mismo día:

```csharp
private bool IsSameDay(DateTime orderCreatedAt)
{
    return orderCreatedAt.Date == DateTime.UtcNow.Date;
}

// Uso
if (order.Status == OrderStatus.Delivered && !IsSameDay(order.CreatedAt))
{
    if (_currentUser.Role != "superadmin")
        throw new BusinessException("Solo se pueden modificar pedidos del día actual");
}
```

---

## Mejores Prácticas

### 1. SIEMPRE validar BranchId
```csharp
if (_currentUser.Role != "superadmin" && resource.BranchId != _currentUser.BranchId)
    throw new BusinessException("No tienes permisos...");
```

### 2. SIEMPRE validar rol
```csharp
if (!new[] { "admin", "superadmin" }.Contains(_currentUser.Role.ToLower()))
    throw new BusinessException("No tienes permisos...");
```

### 3. Usar Business Rules para validaciones complejas
```csharp
if (!_businessRules.CanPerformAction(resource, _currentUser.Role))
    throw new BusinessException("No puedes realizar esta acción");
```

### 4. Mensajes de error descriptivos
```csharp
// ❌ Mal
throw new BusinessException("Error");

// ✅ Bien
throw new BusinessException($"El pedido debe estar en estado 'Ready' para asignar domiciliario. Estado actual: {order.Status}");
```

