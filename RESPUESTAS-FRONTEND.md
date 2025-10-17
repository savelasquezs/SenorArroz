# Respuestas a Preguntas del Frontend - Endpoints de Actualización

---

## 1. Endpoints de Actualización

### a) ¿Existe PATCH/PUT /api/orders/{id} para actualizar datos básicos?

✅ **Sí existe**: `PUT /api/orders/{id}`

**Roles permitidos**: Admin, Superadmin, Cashier

### b) ¿Qué estructura espera el body?

```json
{
  "customerId": 45,
  "addressId": 12,
  "loyaltyRuleId": 2,
  "guestName": "Santiago",
  "deliveryFee": 6000,
  "reservedFor": "2024-10-17T20:00:00Z",
  "subtotal": 50000,
  "total": 56000,
  "discountTotal": 0,
  "notes": "Sin cebolla"
}
```

**Nota**: Todos los campos son **opcionales**. Solo envía lo que quieres modificar.

**Validaciones aplicadas**:
- ✅ **Pedidos NO entregados**: Admin/Superadmin/Cashier pueden modificar todos los campos
- ⚠️ **Pedidos entregados (mismo día)**: 
  - Admin/Superadmin: Pueden modificar TODO
  - Cashier: NO puede modificar (ver restricción abajo)
- ✅ **Pedidos cancelados**: Solo Superadmin

---

## 2. Actualización de Productos (OrderDetails)

### a) ¿Hay endpoints separados como PUT /api/orders/{id}/details?

❌ **No existe endpoint separado**

### b) ¿Se actualiza producto por producto con PATCH /api/order-details/{id}?

❌ **No existe endpoint por producto individual**

### c) ¿Se pueden agregar/eliminar productos de un pedido existente?

✅ **Sí, mediante PUT /api/orders/{id}**

Los productos se actualizan enviando el array completo en el mismo endpoint:

```json
PUT /api/orders/123

{
  "orderDetails": [
    {
      "id": 201,           // ID del OrderDetail existente
      "productId": 15,
      "quantity": 3,       // Nueva cantidad
      "unitPrice": 18000,
      "discount": 500,
      "notes": "Extra picante"
    },
    {
      "id": 202,
      "productId": 23,
      "quantity": 1,
      "unitPrice": 3000,
      "discount": 0
    }
  ]
}
```

**Validaciones específicas para productos**:
- ✅ **Pedidos NO entregados**: Admin/Superadmin/Cashier pueden modificar
- ⚠️ **Pedidos entregados (mismo día)**: 
  - ✅ Admin/Superadmin: SÍ pueden modificar
  - ❌ Cashier: NO puede modificar productos
- ❌ **Pedidos entregados (otro día)**: Nadie puede modificar
- ✅ **Pedidos cancelados**: Solo Superadmin

---

## 3. Actualización de Pagos

### a) ¿Endpoints para agregar/editar/eliminar pagos?

✅ **Sí existen**:

#### **Pagos Bancarios**:
```
POST   /api/bank-payments              → Crear pago
PUT    /api/bank-payments/{id}         → Actualizar monto (NUEVO)
DELETE /api/bank-payments/{id}         → Eliminar pago
```

#### **Pagos por App**:
```
POST   /api/app-payments               → Crear pago
PUT    /api/app-payments/{id}          → Actualizar monto (NUEVO)
DELETE /api/app-payments/{id}          → Eliminar pago
```

**Ejemplo de actualización**:
```json
PUT /api/bank-payments/301

{
  "amount": 35000
}
```

### b) ¿Los pagos se pueden modificar una vez creados?

✅ **Sí, pero con restricciones temporales**:

#### **Mismo día** (pedido creado hoy):
- ✅ Admin/Superadmin/Cashier: Pueden crear, modificar y eliminar
- ✅ Aplica para pedidos en **cualquier estado** (incluso entregados)

#### **Días posteriores** (pedido creado en días anteriores):
- ✅ Solo Superadmin: Puede modificar
- ❌ Admin/Cashier: NO pueden modificar

**Ejemplo de error para cajero en día posterior**:
```json
{
  "message": "No tienes permisos para modificar pagos de este pedido"
}
```

---

## 4. Cambio de Estado

### a) ¿Existe endpoint específico para cambiar estado?

✅ **Sí existe**: `PUT /api/orders/{id}/status`

**NO es parte del PATCH/PUT general del pedido**

### b) Estructura del endpoint:

```json
PUT /api/orders/123/status

{
  "status": "in_preparation",
  "reason": null
}
```

**Estados disponibles**:
- `taken` - Tomado
- `in_preparation` - En preparación
- `ready` - Listo
- `on_the_way` - En camino
- `delivered` - Entregado
- `cancelled` - Cancelado (usar endpoint específico)

---

## 5. Cancelación

### a) ¿Endpoint específico para cancelar?

✅ **Sí existe**: `PUT /api/orders/{id}/cancel`

**NO se usa el endpoint general con status cancelled**

### b) Estructura:

```json
PUT /api/orders/123/cancel

{
  "reason": "Cliente canceló el pedido"
}
```

**Características**:
- ✅ Campo `reason` es **obligatorio**
- ✅ Solo Admin/Superadmin pueden cancelar
- ✅ Solo se pueden cancelar pedidos del **mismo día**
- ✅ Cancela automáticamente todos los **pagos asociados**

---

## 6. Permisos por Rol - Matriz Completa

### **Superadmin**
| Acción | Restricción |
|--------|-------------|
| Modificar pedido (datos básicos) | ✅ Sin restricciones |
| Modificar productos | ✅ Sin restricciones |
| Modificar pagos | ✅ Sin restricciones (incluso días anteriores) |
| Cambiar estado | ✅ Puede cambiar a cualquier estado (incluso revertir) |
| Cancelar | ✅ Sin restricciones |

### **Admin**
| Acción | Restricción |
|--------|-------------|
| Modificar pedido (datos básicos) | ✅ Solo mismo día si está entregado |
| Modificar productos | ✅ Solo mismo día si está entregado |
| Modificar pagos | ✅ Solo mismo día |
| Cambiar estado | ✅ Puede cambiar a cualquier estado |
| Cancelar | ✅ Solo mismo día |

### **Cashier (Cajero)**
| Acción | Restricción |
|--------|-------------|
| Modificar pedido (datos básicos) | ✅ Solo pedidos NO entregados |
| Modificar productos | ❌ NO puede modificar si está entregado |
| Modificar pagos | ✅ Solo mismo día |
| Cambiar estado | ⚠️ **Solo hacia adelante** (no puede retroceder) |
| Cancelar | ❌ Debe hacerlo un Admin |

**Transiciones permitidas para Cajero**:
```
taken → in_preparation
in_preparation → ready
ready → on_the_way
on_the_way → delivered
```

❌ **NO puede**: `delivered → ready` (retroceder)

### **Kitchen (Cocina)**
| Acción | Restricción |
|--------|-------------|
| Modificar pedido | ❌ No tiene acceso |
| Modificar productos | ❌ No tiene acceso |
| Modificar pagos | ❌ No tiene acceso |
| Cambiar estado | ⚠️ **Solo estados de preparación** |
| Cancelar | ❌ No tiene acceso |

**Transiciones permitidas para Cocina**:
```
taken → in_preparation
in_preparation → ready
```

### **Deliveryman (Domiciliario)**
| Acción | Restricción |
|--------|-------------|
| Modificar pedido | ❌ No tiene acceso |
| Modificar productos | ❌ No tiene acceso |
| Modificar pagos | ❌ No tiene acceso |
| Cambiar estado | ❌ **NO puede usar** `PUT /api/orders/{id}/status` |
| Cancelar | ❌ No tiene acceso |

**Endpoints específicos para domiciliarios**:
```
POST /api/orders/delivery/self-assign  → Auto-asignarse pedidos
```

❌ **Error si intenta usar endpoint general**:
```json
{
  "message": "Los domiciliarios deben usar los endpoints específicos de auto-asignación"
}
```

---

## 7. Matriz de Transiciones de Estado

| Desde / Hacia | Taken | InPrep | Ready | OnWay | Delivered | Cancelled |
|---------------|-------|--------|-------|-------|-----------|-----------|
| **Taken** | - | ✅ Todos | ❌ Cashier/Kitchen | ❌ Cashier/Kitchen | ❌ Cashier/Kitchen | ✅ Admin+ |
| **InPreparation** | ✅ Admin+ | - | ✅ Todos | ❌ Cashier/Kitchen | ❌ Cashier/Kitchen | ✅ Admin+ |
| **Ready** | ✅ Admin+ | ✅ Admin+ | - | ✅ Cashier | ❌ Kitchen | ✅ Admin+ |
| **OnTheWay** | ✅ Admin+ | ✅ Admin+ | ✅ Admin+ | - | ✅ Cashier | ✅ Admin+ |
| **Delivered** | ✅ Admin+ | ✅ Admin+ | ✅ Admin+ | ✅ Admin+ | - | ✅ Admin+ |
| **Cancelled** | ✅ Superadmin | ❌ | ❌ | ❌ | ❌ | - |

**Leyenda**:
- ✅ **Todos**: Cashier, Kitchen, Admin, Superadmin
- ✅ **Cashier**: Cashier, Admin, Superadmin
- ✅ **Admin+**: Solo Admin y Superadmin
- ✅ **Superadmin**: Solo Superadmin
- ❌ **Cashier/Kitchen**: Todos excepto Cajero y Cocina
- ❌: Nadie (transición no permitida)

---

## 8. Resumen de Todos los Endpoints

### **Pedidos**
```
PUT    /api/orders/{id}                     → Actualizar datos básicos y productos
PUT    /api/orders/{id}/status              → Cambiar estado
PUT    /api/orders/{id}/cancel              → Cancelar pedido
PUT    /api/orders/{id}/assign-delivery     → Asignar domiciliario
PUT    /api/orders/{id}/unassign-delivery   → Desasignar domiciliario
DELETE /api/orders/{id}                     → Eliminar pedido
```

### **Pagos Bancarios**
```
POST   /api/bank-payments                   → Crear pago
PUT    /api/bank-payments/{id}              → Actualizar monto (NUEVO)
DELETE /api/bank-payments/{id}              → Eliminar pago
POST   /api/bank-payments/{id}/verify       → Verificar pago
POST   /api/bank-payments/{id}/unverify     → Desverificar pago
```

### **Pagos por App**
```
POST   /api/app-payments                    → Crear pago
PUT    /api/app-payments/{id}               → Actualizar monto (NUEVO)
DELETE /api/app-payments/{id}               → Eliminar pago
POST   /api/app-payments/{id}/settle        → Liquidar pago
```

---

## 9. Ejemplos de Casos Comunes

### Caso 1: Cajero actualiza nota de pedido NO entregado
```http
PUT /api/orders/123
{
  "notes": "Cliente pidió cubiertos extra"
}
```
✅ **PERMITIDO**

### Caso 2: Cajero intenta modificar productos de pedido entregado
```http
PUT /api/orders/123
{
  "orderDetails": [...]
}
```
❌ **ERROR**: `"No tienes permisos para modificar los productos de este pedido"`

### Caso 3: Cajero intenta retroceder estado
```http
PUT /api/orders/123/status
// Estado actual: delivered
{
  "status": "ready"
}
```
❌ **ERROR**: `"No puedes cambiar el estado de delivered a ready"`

### Caso 4: Admin modifica pago del mismo día
```http
PUT /api/bank-payments/301
{
  "amount": 40000
}
```
✅ **PERMITIDO** (mismo día)

### Caso 5: Cajero intenta modificar pago de ayer
```http
PUT /api/bank-payments/301
{
  "amount": 40000
}
```
❌ **ERROR**: `"No tienes permisos para modificar pagos de este pedido"`

---

## 10. Validación Temporal - "Mismo Día"

La validación de "mismo día" se calcula comparando:
- **Fecha de creación del pedido** (UTC)
- **Fecha actual** (UTC)

**Ejemplo**:
```
Pedido creado: 2024-10-17 08:00:00 UTC
Hoy: 2024-10-17 23:59:59 UTC
Resultado: ✅ MISMO DÍA

Pedido creado: 2024-10-16 23:59:59 UTC
Hoy: 2024-10-17 00:00:01 UTC
Resultado: ❌ DÍA DIFERENTE
```

---

**Fecha de actualización**: 2024-10-17  
**Versión**: 2.0


