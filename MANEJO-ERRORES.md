# Manejo de Errores

## Sistema Centralizado de Excepciones

El proyecto usa **GlobalExceptionMiddleware** para capturar y formatear todos los errores de manera consistente.

## Excepciones Personalizadas

### 1. BusinessException (400 Bad Request)

**Uso**: Errores de lógica de negocio

```csharp
// Domain/Exceptions/BusinessException.cs
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}
```

**Cuándo usar**:
- Validaciones de negocio fallidas
- Operaciones no permitidas por reglas de negocio
- Restricciones de permisos

**Ejemplos**:
```csharp
throw new BusinessException("Los pedidos de domicilio requieren un cliente");
throw new BusinessException("No tienes permisos para modificar este pedido");
throw new BusinessException($"El pedido debe estar en estado 'Ready'. Estado actual: {order.Status}");
throw new BusinessException("El domiciliario ya tiene 3 pedidos activos");
```

**Respuesta al frontend**:
```json
HTTP 400 Bad Request
{
  "success": false,
  "message": "Los pedidos de domicilio requieren un cliente",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

---

### 2. NotFoundException (404 Not Found)

**Uso**: Recurso no encontrado

```csharp
// Domain/Exceptions/BusinessException.cs
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
```

**Cuándo usar**:
- Recurso solicitado no existe
- ID no válido

**Ejemplos**:
```csharp
throw new NotFoundException("Pedido no encontrado");
throw new NotFoundException("Usuario no encontrado");
throw new NotFoundException("Cliente no encontrado");
```

**Respuesta al frontend**:
```json
HTTP 404 Not Found
{
  "success": false,
  "message": "Pedido no encontrado",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

---

### 3. ValidationException (400 Bad Request)

**Uso**: Múltiples errores de validación

```csharp
// Domain/Exceptions/BusinessException.cs
public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors) : this()
    {
        Errors = errors;
    }
}
```

**Cuándo usar**:
- FluentValidation (si se implementa)
- Múltiples campos con errores

**Ejemplo**:
```csharp
var errors = new Dictionary<string, string[]>
{
    { "Email", new[] { "El email es requerido", "Email inválido" } },
    { "Phone", new[] { "El teléfono debe tener 10 dígitos" } }
};
throw new ValidationException(errors);
```

**Respuesta al frontend**:
```json
HTTP 400 Bad Request
{
  "success": false,
  "message": "Errores de validación",
  "errors": {
    "email": ["El email es requerido", "Email inválido"],
    "phone": ["El teléfono debe tener 10 dígitos"]
  },
  "timestamp": "2024-10-17T15:30:00Z"
}
```

---

### 4. UnauthorizedAccessException (401 Unauthorized)

**Uso**: Problemas de autenticación

```csharp
throw new UnauthorizedAccessException("No autorizado");
```

**Cuándo usar**:
- Token inválido o expirado
- Usuario no autenticado

**Respuesta al frontend**:
```json
HTTP 401 Unauthorized
{
  "success": false,
  "message": "No autorizado",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

---

## GlobalExceptionMiddleware

### Implementación

```csharp
// Middleware/GlobalExceptionMiddleware.cs
public class GlobalExceptionMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió una excepción no controlada: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Success = false,
            Message = exception.Message,
            Timestamp = DateTime.UtcNow
        };

        switch (exception)
        {
            case NotFoundException:
                response.StatusCode = 404;
                break;

            case BusinessException:
                response.StatusCode = 400;
                break;

            case ValidationException validationEx:
                response.StatusCode = 400;
                errorResponse.Message = "Errores de validación";
                errorResponse.Errors = validationEx.Errors;
                break;

            case UnauthorizedAccessException:
                response.StatusCode = 401;
                errorResponse.Message = "No autorizado";
                break;

            default:
                response.StatusCode = 500;
                errorResponse.Message = "Error interno del servidor";
                break;
        }

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await response.WriteAsync(jsonResponse);
    }
}
```

### Formato de Respuesta de Error

```csharp
private class ErrorResponse
{
    public bool Success { get; set; }                        // Siempre false
    public string Message { get; set; } = string.Empty;      // Mensaje principal
    public IDictionary<string, string[]>? Errors { get; set; } // Solo en ValidationException
    public DateTime Timestamp { get; set; }                  // Hora UTC del error
}
```

---

## Mapeo de Excepciones a Status Codes

| Excepción | Status Code | Mensaje al Frontend |
|-----------|-------------|---------------------|
| `BusinessException` | 400 | Mensaje de la excepción |
| `NotFoundException` | 404 | Mensaje de la excepción |
| `ValidationException` | 400 | "Errores de validación" + detalles |
| `UnauthorizedAccessException` | 401 | "No autorizado" |
| `InvalidOperationException` | 500 | "Error interno del servidor" |
| `ArgumentException` | 500 | "Error interno del servidor" |
| Cualquier otra | 500 | "Error interno del servidor" |

⚠️ **IMPORTANTE**: Para que el mensaje llegue al frontend, **SIEMPRE usar las excepciones personalizadas**.

---

## Ejemplos de Uso por Escenario

### Validación Simple

```csharp
if (string.IsNullOrWhiteSpace(request.Order.GuestName))
    throw new BusinessException("Los pedidos de domicilio requieren el nombre del invitado");

if (!request.Order.ReservedFor.HasValue)
    throw new BusinessException("Los pedidos de reserva requieren fecha y hora de entrega");
```

### Recurso No Encontrado

```csharp
var order = await _orderRepository.GetByIdAsync(request.Id);
if (order == null)
    throw new NotFoundException("Pedido no encontrado");

var user = await _userRepository.GetByIdAsync(userId);
if (user == null)
    throw new NotFoundException("Usuario no encontrado");
```

### Validación de Permisos

```csharp
if (_currentUser.Role != "superadmin" && order.BranchId != _currentUser.BranchId)
    throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

if (!new[] { "admin", "superadmin", "cashier" }.Contains(_currentUser.Role.ToLower()))
    throw new BusinessException("No tienes permisos para actualizar pedidos");
```

### Validación con Contexto

```csharp
// Mensaje descriptivo con estado actual
if (order.Status != OrderStatus.Ready)
    throw new BusinessException($"El pedido debe estar en estado 'Ready' para asignar domiciliario. Estado actual: {order.Status}");

// Mensaje con conteo
var activeOrders = await GetActiveOrdersCount(deliveryManId);
if (activeOrders >= 3)
    throw new BusinessException($"El domiciliario ya tiene {activeOrders} pedidos activos. No se pueden asignar más (máximo 3)");
```

### Validación con Business Rules

```csharp
if (!_businessRules.CanUpdateOrder(order, _currentUser.Role))
    throw new BusinessException("No tienes permisos para modificar este pedido en su estado actual");

if (!_businessRules.IsStatusTransitionValid(order.Status, newStatus, _currentUser.Role))
    throw new BusinessException($"No puedes cambiar el estado de {order.Status} a {newStatus}");
```

---

## Logging de Errores

### En GlobalExceptionMiddleware

Todos los errores se loggean automáticamente:

```csharp
_logger.LogError(ex, "Ocurrió una excepción no controlada: {Message}", ex.Message);
```

**Output en consola**:
```
fail: SenorArroz.API.Middleware.GlobalExceptionMiddleware[0]
      Ocurrió una excepción no controlada: Los pedidos de domicilio requieren un cliente
      SenorArroz.Domain.Exceptions.BusinessException: Los pedidos de domicilio requieren un cliente
         at SenorArroz.Application.Features.Orders.Commands.CreateOrderHandler.Handle(...)
```

### Logging Adicional en Handlers

Para debugging, puedes agregar logs informativos:

```csharp
private readonly ILogger<CreateOrderHandler> _logger;

public async Task<OrderDto> Handle(...)
{
    _logger.LogInformation("Creando pedido para sucursal {BranchId}", branchId);
    _logger.LogInformation("Usuario: {UserId}, Rol: {Role}", _currentUser.Id, _currentUser.Role);
    
    // ... lógica
    
    _logger.LogInformation("Pedido creado exitosamente: {OrderId}", createdOrder.Id);
}
```

---

## Validación de Entrada (Data Annotations)

### En DTOs

```csharp
public class CreateBankPaymentDto
{
    [Required(ErrorMessage = "El pedido es requerido")]
    public int OrderId { get; set; }

    [Required(ErrorMessage = "El banco es requerido")]
    public int BankId { get; set; }

    [Required(ErrorMessage = "El monto es requerido")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
    public decimal Amount { get; set; }
}
```

ASP.NET Core valida automáticamente y retorna 400 si falla.

---

## Respuestas de Error HTTP

### 400 Bad Request
```json
{
  "success": false,
  "message": "Los pedidos de domicilio requieren un cliente",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

### 401 Unauthorized
```json
{
  "error": "Unauthorized",
  "message": "Token inválido o expirado"
}
```

### 403 Forbidden
```json
{
  "error": "Forbidden",
  "message": "No tienes permisos para acceder a este recurso"
}
```

### 404 Not Found
```json
{
  "success": false,
  "message": "Pedido no encontrado",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

### 500 Internal Server Error
```json
{
  "success": false,
  "message": "Error interno del servidor",
  "timestamp": "2024-10-17T15:30:00Z"
}
```

---

## Mejores Prácticas

### ✅ Hacer

1. **Usar excepciones personalizadas**
   ```csharp
   throw new BusinessException("Mensaje claro");
   throw new NotFoundException("Recurso no encontrado");
   ```

2. **Mensajes descriptivos**
   ```csharp
   throw new BusinessException($"El pedido debe estar en estado 'Ready'. Estado actual: {order.Status}");
   ```

3. **Loggear errores importantes**
   ```csharp
   _logger.LogError(ex, "Error al procesar pedido {OrderId}", orderId);
   ```

### ❌ NO Hacer

1. **NO usar excepciones genéricas**
   ```csharp
   // ❌ Mal - se convierte en 500
   throw new InvalidOperationException("Error");
   throw new ArgumentException("Error");
   
   // ✅ Bien
   throw new BusinessException("Error");
   ```

2. **NO mensajes genéricos**
   ```csharp
   // ❌ Mal
   throw new BusinessException("Error");
   
   // ✅ Bien
   throw new BusinessException("El domiciliario ya tiene 3 pedidos activos");
   ```

3. **NO exponer datos sensibles**
   ```csharp
   // ❌ Mal
   throw new BusinessException($"Hash de contraseña: {user.PasswordHash}");
   
   // ✅ Bien
   throw new BusinessException("Contraseña incorrecta");
   ```

---

## Debugging

### Habilitar Detalles de Error en PostgreSQL

En desarrollo, puedes ver más detalles de errores de BD:

```json
// appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=senorarroz;Include Error Detail=true"
  }
}
```

⚠️ **NO** habilitar en producción por seguridad.

---

## Casos Comunes de Errores

### Error de Check Constraint en PostgreSQL

**Error**:
```
Npgsql.PostgresException: 23514: new row for relation "order" violates check constraint "order_status_check"
```

**Causa**: Valor de enum no coincide con constraint de BD

**Solución**: Verificar conversión de enums en OrderConfiguration.cs

### Error de Null Reference

**Error**:
```
Object reference not set to an instance of an object
```

**Causas comunes**:
- Falta `.Include()` en repositorio para navegación
- DTO espera propiedad navegada pero es null

**Solución**: Agregar `.Include()` necesarios:
```csharp
.Include(o => o.Address)      // Para AddressDescription
.Include(o => o.LoyaltyRule)  // Para LoyaltyRuleName
```

### Error de DateTime Kind

**Error**:
```
Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'
```

**Causa**: DateTime sin Kind=Utc

**Solución**: El ApplicationDbContext ya maneja esto automáticamente en `SaveChangesAsync()`. Si ves este error, verifica que no estés usando `SaveChanges()` sincrónico.

---

## Checklist para Nuevos Handlers

Al crear un nuevo handler, asegúrate de:

- [ ] Usar `BusinessException` para errores de negocio
- [ ] Usar `NotFoundException` para recursos no encontrados
- [ ] Validar acceso a sucursal (`BranchId`)
- [ ] Validar permisos por rol
- [ ] Mensajes de error descriptivos y específicos
- [ ] Loggear operaciones importantes
- [ ] NO exponer datos sensibles en mensajes
- [ ] Incluir navegaciones necesarias en queries

