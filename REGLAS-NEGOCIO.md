# Reglas de Negocio del Sistema

## OrderBusinessRulesService

Servicio centralizado para validaciones complejas de pedidos según estado y rol del usuario.

## Ubicación

```
Application/Common/Services/OrderBusinessRulesService.cs
Application/Common/Interfaces/IOrderBusinessRulesService.cs
```

---

## Métodos del Servicio

### 1. CanUpdateOrder

**Valida** si un usuario puede modificar un pedido (datos básicos).

```csharp
public bool CanUpdateOrder(Order order, string userRole)
{
    var role = userRole.ToLower();

    // Pedidos cancelados: solo superadmin
    if (order.Status == OrderStatus.Cancelled)
        return role == "superadmin";

    // Pedidos entregados
    if (order.Status == OrderStatus.Delivered)
    {
        // Solo admin y superadmin
        if (role != "admin" && role != "superadmin")
            return false;

        // Solo si es el mismo día
        return IsSameDay(order.CreatedAt);
    }

    // Pedidos no entregados: admin, superadmin y cashier
    return role == "admin" || role == "superadmin" || role == "cashier";
}
```

**Reglas**:
- ✅ **Pedidos NO entregados**: Admin/Superadmin/Cashier pueden modificar
- ⚠️ **Pedidos entregados (mismo día)**: Solo Admin/Superadmin
- ⚠️ **Pedidos entregados (otro día)**: Nadie (excepto superadmin en casos especiales)
- ✅ **Pedidos cancelados**: Solo Superadmin

---

### 2. CanUpdateOrderProducts

**Valida** si un usuario puede modificar los productos de un pedido.

```csharp
public bool CanUpdateOrderProducts(Order order, string userRole)
{
    var role = userRole.ToLower();

    // Pedidos cancelados: solo superadmin
    if (order.Status == OrderStatus.Cancelled)
        return role == "superadmin";

    // Pedidos entregados
    if (order.Status == OrderStatus.Delivered)
    {
        // Cashier NO puede modificar productos de pedidos entregados
        // Solo admin y superadmin pueden
        if (role != "admin" && role != "superadmin")
            return false;

        // Solo si es el mismo día
        return IsSameDay(order.CreatedAt);
    }

    // Pedidos no entregados: admin, superadmin y cashier
    return role == "admin" || role == "superadmin" || role == "cashier";
}
```

**Reglas**:
- ✅ **Pedidos NO entregados**: Admin/Superadmin/Cashier pueden modificar productos
- ⚠️ **Pedidos entregados (mismo día)**: Solo Admin/Superadmin (NO Cashier)
- ❌ **Pedidos entregados (otro día)**: Nadie
- ✅ **Pedidos cancelados**: Solo Superadmin

---

### 3. CanModifyPayments

**Valida** si un usuario puede modificar pagos de un pedido.

```csharp
public bool CanModifyPayments(Order order, string userRole)
{
    var role = userRole.ToLower();

    // Mismo día: admin, superadmin y cashier pueden modificar
    if (IsSameDay(order.CreatedAt))
    {
        return role == "admin" || role == "superadmin" || role == "cashier";
    }

    // Días posteriores: solo superadmin
    return role == "superadmin";
}
```

**Reglas**:
- ✅ **Mismo día**: Admin/Superadmin/Cashier pueden crear, modificar y eliminar pagos
- ✅ **Días posteriores**: Solo Superadmin puede modificar
- ✅ **Aplica para cualquier estado** del pedido (incluso entregados)

---

### 4. IsStatusTransitionValid

**Valida** si una transición de estado es permitida para un rol.

```csharp
public bool IsStatusTransitionValid(OrderStatus currentStatus, OrderStatus newStatus, string userRole)
{
    var role = userRole.ToLower();

    // Mismo estado, no hay cambio
    if (currentStatus == newStatus)
        return true;

    // Admin y Superadmin tienen control total
    if (role == "admin" || role == "superadmin")
        return true;

    // Desde Cancelled: solo superadmin
    if (currentStatus == OrderStatus.Cancelled)
        return false;

    // A Cancelled: solo admin y superadmin
    if (newStatus == OrderStatus.Cancelled)
        return false;

    // Validaciones por rol
    switch (role)
    {
        case "cashier":
            return IsValidCashierTransition(currentStatus, newStatus);

        case "kitchen":
            return IsValidKitchenTransition(currentStatus, newStatus);

        case "deliveryman":
            return false;  // NO pueden usar este endpoint

        default:
            return false;
    }
}
```

---

### 5. IsValidCashierTransition (Helper Privado)

**Cajero solo puede avanzar**, no retroceder:

```csharp
private bool IsValidCashierTransition(OrderStatus current, OrderStatus next)
{
    return (current, next) switch
    {
        (OrderStatus.Taken, OrderStatus.InPreparation) => true,
        (OrderStatus.InPreparation, OrderStatus.Ready) => true,
        (OrderStatus.Ready, OrderStatus.OnTheWay) => true,
        (OrderStatus.OnTheWay, OrderStatus.Delivered) => true,
        _ => false  // No puede retroceder ni saltar estados
    };
}
```

**Flujo permitido para Cajero**:
```
Taken → InPreparation → Ready → OnTheWay → Delivered
```

**NO permitido**:
```
Delivered → Ready  ❌ (retroceder)
Taken → Ready      ❌ (saltar)
```

---

### 6. IsValidKitchenTransition (Helper Privado)

**Cocina solo maneja preparación**:

```csharp
private bool IsValidKitchenTransition(OrderStatus current, OrderStatus next)
{
    return (current, next) switch
    {
        (OrderStatus.Taken, OrderStatus.InPreparation) => true,
        (OrderStatus.InPreparation, OrderStatus.Ready) => true,
        _ => false
    };
}
```

**Flujo permitido para Cocina**:
```
Taken → InPreparation → Ready
```

---

### 7. IsSameDay

**Valida** si el pedido fue creado el mismo día (comparación en UTC):

```csharp
public bool IsSameDay(DateTime orderCreatedAt)
{
    return orderCreatedAt.Date == DateTime.UtcNow.Date;
}
```

**Ejemplos**:
```
Pedido creado: 2024-10-17 08:00 UTC
Ahora: 2024-10-17 23:59 UTC
Resultado: true (mismo día)

Pedido creado: 2024-10-16 23:59 UTC
Ahora: 2024-10-17 00:01 UTC
Resultado: false (día diferente)
```

---

## Matriz de Transiciones de Estado

### Tabla Completa

| Desde / Hacia | Taken | InPrep | Ready | OnWay | Delivered | Cancelled |
|---------------|-------|--------|-------|-------|-----------|-----------|
| **Taken** | - | ✅ Todos | ❌ | ❌ | ❌ | ✅ Admin+ |
| **InPreparation** | ✅ Admin+ | - | ✅ Todos | ❌ | ❌ | ✅ Admin+ |
| **Ready** | ✅ Admin+ | ✅ Admin+ | - | ✅ Cashier | ❌ | ✅ Admin+ |
| **OnTheWay** | ✅ Admin+ | ✅ Admin+ | ✅ Admin+ | - | ✅ Cashier | ✅ Admin+ |
| **Delivered** | ✅ Admin+ | ✅ Admin+ | ✅ Admin+ | ✅ Admin+ | - | ✅ Admin+ |
| **Cancelled** | ✅ Super | ❌ | ❌ | ❌ | ❌ | - |

**Leyenda**:
- ✅ **Todos**: Kitchen, Cashier, Admin, Superadmin
- ✅ **Cashier**: Cashier, Admin, Superadmin  
- ✅ **Admin+**: Admin y Superadmin
- ✅ **Super**: Solo Superadmin
- ❌: No permitido

---

## Reglas por Tipo de Pedido

### Delivery (Domicilio)

**Campos obligatorios**:
```csharp
if (request.Order.Type == OrderType.Delivery)
{
    if (!request.Order.CustomerId.HasValue)
        throw new BusinessException("Los pedidos de domicilio requieren un cliente");
    
    if (!request.Order.AddressId.HasValue)
        throw new BusinessException("Los pedidos de domicilio requieren una dirección");
    
    if (string.IsNullOrWhiteSpace(request.Order.GuestName))
        throw new BusinessException("Los pedidos de domicilio requieren el nombre del invitado");
}
```

### Reservation (Reserva)

**Campos obligatorios**:
```csharp
if (request.Order.Type == OrderType.Reservation)
{
    if (!request.Order.ReservedFor.HasValue)
        throw new BusinessException("Los pedidos de reserva requieren fecha y hora de entrega");
    
    if (string.IsNullOrWhiteSpace(request.Order.GuestName))
        throw new BusinessException("Los pedidos de reserva requieren el nombre del invitado");
}
```

### Onsite (En el local)

**Campos opcionales**:
- GuestName es opcional
- No requiere customer ni address

---

## Reglas de Asignación de Domiciliarios

### Validaciones

```csharp
public async Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId)
{
    var order = await _context.Orders.FindAsync(orderId);
    
    // 1. Pedido debe estar en estado Ready
    if (order.Status != OrderStatus.Ready)
        throw new BusinessException($"El pedido debe estar en estado 'Ready' para asignar domiciliario. Estado actual: {order.Status}");

    // 2. Verificar límite de pedidos del domiciliario
    var activeOrders = await _context.Orders
        .Where(o => o.DeliveryManId == deliveryManId && 
                  (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready))
        .CountAsync();

    if (activeOrders >= 3)
        throw new BusinessException($"El domiciliario ya tiene {activeOrders} pedidos activos. No se pueden asignar más (máximo 3)");

    order.DeliveryManId = deliveryManId;
    await _context.SaveChangesAsync();
    return order;
}
```

**Reglas**:
- ✅ Solo pedidos en estado `Ready`
- ✅ Máximo 3 pedidos activos por domiciliario
- ✅ Pedidos activos = `Ready` + `OnTheWay`

---

## Reglas de Cancelación

### Validaciones

```csharp
public async Task<Order> CancelOrderAsync(int orderId, string reason)
{
    var order = await _context.Orders.FindAsync(orderId);
    
    // 1. Solo del mismo día
    if (!IsSameDay(order.CreatedAt))
        throw new BusinessException("Solo se pueden cancelar pedidos del mismo día");
    
    // 2. Razón obligatoria
    if (string.IsNullOrWhiteSpace(reason))
        throw new BusinessException("Debe especificar una razón para la cancelación");
    
    // 3. Cambiar estado
    order.Status = OrderStatus.Cancelled;
    order.CancelledReason = reason;
    order.AddStatusTime(OrderStatus.Cancelled, DateTime.UtcNow);
    
    // 4. Cancelar pagos asociados (automático)
    await CancelAssociatedPayments(orderId);
    
    await _context.SaveChangesAsync();
    return order;
}
```

**Reglas**:
- ✅ Solo Admin y Superadmin pueden cancelar
- ✅ Solo pedidos del mismo día
- ✅ Razón de cancelación obligatoria
- ✅ Cancela automáticamente todos los pagos asociados

---

## Reglas de Modificación de Pagos

### Matriz por Rol y Tiempo

| Rol / Tiempo | Mismo Día | Días Anteriores |
|--------------|-----------|-----------------|
| **Superadmin** | ✅ Crear, Modificar, Eliminar | ✅ Crear, Modificar, Eliminar |
| **Admin** | ✅ Crear, Modificar, Eliminar | ❌ |
| **Cashier** | ⚠️ Solo Modificar monto | ❌ |
| **Kitchen** | ❌ | ❌ |
| **Deliveryman** | ❌ | ❌ |

### Operaciones por Endpoint

#### Crear Pago
```
POST /api/bank-payments
POST /api/app-payments
```
- ✅ Admin, Superadmin
- ❌ Cashier (no puede crear, solo modificar)

#### Modificar Monto
```
PUT /api/bank-payments/{id}
PUT /api/app-payments/{id}
```
- ✅ Admin, Superadmin, Cashier (mismo día)
- ✅ Solo Superadmin (días anteriores)

#### Eliminar Pago
```
DELETE /api/bank-payments/{id}
DELETE /api/app-payments/{id}
```
- ✅ Admin, Superadmin (mismo día)
- ✅ Solo Superadmin (días anteriores)

---

## Reglas de Verificación de Pagos Bancarios

### Verificar Pago
```
POST /api/bank-payments/{id}/verify
```

**Reglas**:
- ✅ Solo Admin y Superadmin
- ✅ Solo pagos de su sucursal (excepto superadmin)
- ✅ Marca `VerifiedAt` con timestamp actual

### Desverificar Pago
```
POST /api/bank-payments/{id}/unverify
```

**Reglas**:
- ✅ Solo Admin y Superadmin
- ✅ Solo si ya está verificado
- ✅ Limpia `VerifiedAt` (pone null)

---

## Reglas de Liquidación de Pagos por App

### Liquidar Pago
```
POST /api/app-payments/{id}/settle
```

**Proceso**:
1. Marca el app payment como `IsSetted = true`
2. Crea automáticamente un **BankPayment** correspondiente
3. El bank payment se asocia al banco de la app

**Reglas**:
- ✅ Solo Admin y Superadmin
- ✅ Solo pagos no liquidados
- ✅ Crea bank payment automáticamente

### Liquidar Múltiples
```
POST /api/app-payments/settle-multiple
Body: { "paymentIds": [1, 2, 3] }
```

**Proceso**:
1. Marca todos los app payments como liquidados
2. Crea **un solo** bank payment con la suma total
3. Útil para liquidaciones al final del día

### Desliquidar
```
POST /api/app-payments/{id}/unsettle
```

**Proceso**:
1. Marca el app payment como `IsSetted = false`
2. Elimina el bank payment asociado

---

## Reglas Temporales

### "Mismo Día"

**Definición**: Comparación de fechas en UTC

```csharp
public bool IsSameDay(DateTime orderCreatedAt)
{
    return orderCreatedAt.Date == DateTime.UtcNow.Date;
}
```

**Usado en**:
- Modificación de pedidos entregados
- Modificación de pagos
- Cancelación de pedidos

**Ejemplos**:
```
Pedido creado: 2024-10-17 02:00 UTC (10pm hora local)
Ahora: 2024-10-17 23:59 UTC (7pm hora local)
Resultado: true ✅ (mismo día en UTC)

Pedido creado: 2024-10-17 23:00 UTC (7pm hora local)
Ahora: 2024-10-18 01:00 UTC (9pm hora local)
Resultado: false ❌ (día diferente en UTC)
```

---

## Reglas por Rol

### Superadmin

**Permisos especiales**:
- ✅ Ver/modificar todas las sucursales
- ✅ Modificar pedidos de días anteriores
- ✅ Modificar pagos de días anteriores
- ✅ Revertir cualquier estado
- ✅ Modificar pedidos cancelados
- ✅ Operaciones administrativas sin restricciones

### Admin

**Permisos**:
- ✅ Ver/modificar solo su sucursal
- ✅ Modificar pedidos/pagos solo del día actual
- ✅ Revertir estados libremente
- ✅ Cancelar pedidos
- ✅ Crear/eliminar pagos

**Restricciones**:
- ❌ No puede acceder a otras sucursales
- ❌ No puede modificar datos de días anteriores

### Cashier

**Permisos**:
- ✅ Ver/modificar pedidos de su sucursal
- ✅ Cambiar estados (solo hacia adelante)
- ✅ Modificar montos de pagos (mismo día)
- ✅ Crear pedidos

**Restricciones**:
- ❌ No puede retroceder estados
- ❌ No puede modificar productos de pedidos entregados
- ❌ No puede crear/eliminar pagos (solo modificar montos)
- ❌ No puede cancelar pedidos
- ❌ No puede verificar pagos bancarios
- ❌ No puede liquidar app payments

### Kitchen

**Permisos**:
- ✅ Ver pedidos de su sucursal
- ✅ Cambiar estados de preparación

**Restricciones**:
- ❌ Solo puede cambiar: Taken → InPreparation → Ready
- ❌ No puede modificar pedidos
- ❌ No puede modificar pagos
- ❌ No puede asignar domiciliarios

### Deliveryman

**Permisos**:
- ✅ Auto-asignarse pedidos disponibles
- ✅ Ver pedidos asignados a él
- ✅ Marcar pedidos como entregados (mediante auto-asignación)

**Restricciones**:
- ❌ NO puede usar `PUT /api/orders/{id}/status`
- ❌ Debe usar `POST /api/orders/delivery/self-assign`
- ❌ No puede modificar pedidos
- ❌ No puede modificar pagos

---

## Validaciones en Handlers

### Patrón Estándar en UpdateOrderHandler

```csharp
public async Task<OrderDto> Handle(UpdateOrderCommand request, ...)
{
    // 1. Obtener recurso
    var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
    if (existingOrder == null)
        throw new NotFoundException("Pedido no encontrado");

    // 2. Validar acceso a sucursal
    if (_currentUser.Role != "superadmin" && 
        existingOrder.BranchId != _currentUser.BranchId)
    {
        throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");
    }

    // 3. Validar con Business Rules
    if (!_businessRules.CanUpdateOrder(existingOrder, _currentUser.Role))
        throw new BusinessException("No tienes permisos para modificar este pedido en su estado actual");

    // 4. Validar modificación de productos específicamente
    if (request.Order.OrderDetails != null && 
        !_businessRules.CanUpdateOrderProducts(existingOrder, _currentUser.Role))
    {
        throw new BusinessException("No tienes permisos para modificar los productos de este pedido");
    }

    // 5. Proceder con actualización
    _mapper.Map(request.Order, existingOrder);
    var updated = await _orderRepository.UpdateAsync(existingOrder);
    return _mapper.Map<OrderDto>(updated);
}
```

### Patrón Estándar en ChangeOrderStatusHandler

```csharp
public async Task<OrderDto> Handle(ChangeOrderStatusCommand request, ...)
{
    var existingOrder = await _orderRepository.GetByIdAsync(request.Id);
    if (existingOrder == null)
        throw new NotFoundException("Pedido no encontrado");

    // Validar acceso a sucursal
    if (_currentUser.Role != "superadmin" && 
        existingOrder.BranchId != _currentUser.BranchId)
    {
        throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");
    }

    // Prevenir que domiciliarios usen este endpoint
    if (_currentUser.Role.ToLower() == "deliveryman")
        throw new BusinessException("Los domiciliarios deben usar los endpoints específicos de auto-asignación");

    // Validar transición de estado
    if (!_businessRules.IsStatusTransitionValid(
        existingOrder.Status, 
        request.StatusChange.Status, 
        _currentUser.Role))
    {
        throw new BusinessException($"No puedes cambiar el estado de {existingOrder.Status} a {request.StatusChange.Status}");
    }

    var order = await _orderRepository.ChangeStatusAsync(
        request.Id, 
        request.StatusChange.Status, 
        request.StatusChange.Reason);

    return _mapper.Map<OrderDto>(order);
}
```

---

## Reglas de Filtrado Automático

### Por Sucursal

**TODOS los handlers de lectura** aplican este patrón:

```csharp
// Determinar filtro de sucursal
int? branchFilter = null;

if (_currentUser.Role != "superadmin")
{
    // Usuarios normales solo ven su sucursal
    branchFilter = _currentUser.BranchId;
}
else if (request.BranchId.HasValue)
{
    // Superadmin puede filtrar por sucursal específica
    branchFilter = request.BranchId;
}

// Aplicar filtro
var items = await _repository.GetAllAsync(branchFilter, ...);
```

### Por Fecha Actual

**Pedidos por defecto** del día actual:

```csharp
var fromDate = request.FromDate ?? DateTime.UtcNow.Date;
var toDate = request.ToDate ?? DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

var filtered = items.Where(o => 
    o.CreatedAt >= fromDate && 
    o.CreatedAt <= toDate);
```

---

## Uso en Código

### Inyectar el Servicio

```csharp
public class UpdateOrderHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly ICurrentUser _currentUser;

    public UpdateOrderHandler(..., IOrderBusinessRulesService businessRules, ...)
    {
        _businessRules = businessRules;
        // ...
    }
}
```

### Validar Antes de Operar

```csharp
public async Task<OrderDto> Handle(...)
{
    // Validar con business rules
    if (!_businessRules.CanUpdateOrder(order, _currentUser.Role))
        throw new BusinessException("No tienes permisos para modificar este pedido");

    if (!_businessRules.CanModifyPayments(order, _currentUser.Role))
        throw new BusinessException("No puedes modificar pagos de este pedido");

    // Proceder solo si pasa validaciones
    // ...
}
```

---

## Checklist de Validaciones

Al implementar un handler de modificación, validar:

- [ ] ¿El recurso existe? → `NotFoundException`
- [ ] ¿El usuario tiene acceso a la sucursal? → `BranchId`
- [ ] ¿El rol tiene permisos? → Validar contra roles permitidos
- [ ] ¿El estado del recurso permite la operación? → Business Rules
- [ ] ¿Es el mismo día si es necesario? → `IsSameDay()`
- [ ] ¿Hay restricciones adicionales? → Business Rules específicas

---

## Mensajes de Error Estándares

### Por Tipo de Validación

```csharp
// Recurso no encontrado
throw new NotFoundException("Pedido no encontrado");

// Sin acceso a sucursal
throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

// Sin permisos por rol
throw new BusinessException("No tienes permisos para actualizar pedidos");

// Estado incorrecto
throw new BusinessException($"El pedido debe estar en estado 'Ready'. Estado actual: {order.Status}");

// Transición de estado no permitida
throw new BusinessException($"No puedes cambiar el estado de {current} a {next}");

// Restricción temporal
throw new BusinessException("Solo se pueden modificar pedidos del día actual");

// Restricción de negocio
throw new BusinessException("El domiciliario ya tiene 3 pedidos activos");
```

