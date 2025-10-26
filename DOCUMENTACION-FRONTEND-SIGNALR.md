# Documentación para Frontend - Sistema de Notificaciones en Tiempo Real

## Configuración Inicial

### 1. Instalar SignalR Client

```bash
npm install @microsoft/signalr
```

### 2. URL del Hub

```
http://localhost:5257/hubs/orders
```

## Conexión al Hub

### Ejemplo de Configuración Base

```typescript
import * as signalR from "@microsoft/signalr";

class OrderNotificationService {
  private connection: signalR.HubConnection | null = null;
  private token: string | null = null;

  constructor() {
    this.token = localStorage.getItem("token");
  }

  async start(): Promise<void> {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl("http://localhost:5257/hubs/orders", {
        accessTokenFactory: () => {
          const token = localStorage.getItem("token") || "";
          return token;
        }
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: () => {
          return 3000; // Reintentar cada 3 segundos
        }
      })
      .build();

    // Eventos de conexión
    this.connection.onreconnecting(() => {
      console.log("Reconectando al servidor...");
    });

    this.connection.onreconnected(() => {
      console.log("Reconectado exitosamente");
    });

    this.connection.onclose(() => {
      console.log("Conexión cerrada");
    });

    await this.connection.start();
    console.log("Conectado al hub de notificaciones");
  }

  async stop(): Promise<void> {
    if (this.connection) {
      await this.connection.stop();
      this.connection = null;
    }
  }
}
```

## Eventos Disponibles

### 1. NewOrder - Nuevo Pedido (Cocina)

**Trigger:** Cuando se crea un pedido nuevo en estado `Taken` o una reserva a menos de 2 horas.

**Escuchadores:**
- Usuarios con rol `Kitchen`
- Filtrar por sucursal

**Ejemplo de uso:**

```typescript
connection.on("NewOrder", (order: OrderDto) => {
  console.log("Nuevo pedido recibido:", order);
  
  // Mostrar notificación
  showNotification({
    title: "Nuevo Pedido",
    message: `Pedido #${order.id} - ${order.customerName || order.guestName}`,
    type: "success"
  });

  // Actualizar lista de pedidos en preparación
  addOrderToInPreparationList(order);
});
```

### 2. OrderReady - Pedido Listo (Domiciliarios)

**Trigger:** Cuando un pedido cambia a estado `Ready`.

**Escuchadores:**
- Usuarios con rol `Deliveryman`

**Ejemplo de uso:**

```typescript
connection.on("OrderReady", (order: OrderDto) => {
  console.log("Pedido listo para entrega:", order);
  
  // Mostrar notificación
  showNotification({
    title: "Pedido Listo",
    message: `Pedido #${order.id} listo para entrega`,
    type: "info"
  });

  // Mostrar pedidos disponibles
  addOrderToReadyList(order);
});
```

### 3. ReservationReady - Reserva Próxima (Cocina)

**Trigger:** Cuando una reserva está próxima (2 horas antes).

**Escuchadores:**
- Usuarios con rol `Kitchen`

**Ejemplo de uso:**

```typescript
connection.on("ReservationReady", (order: OrderDto) => {
  console.log("Reserva próxima:", order);
  
  // Mostrar notificación
  showNotification({
    title: "Reserva Próxima",
    message: `Reserva #${order.id} - Hora: ${formatTime(order.reservedFor)}`,
    type: "warning"
  });

  // Agregar a lista de reservas próximas
  addToUpcomingReservations(order);
});
```

## Estructura de OrderDto

```typescript
interface OrderDto {
  id: number;
  branchId: number;
  branchName: string;
  takenById: number;
  takenByName: string;
  customerId?: number;
  customerName?: string;
  customerPhone?: string;
  addressId?: number;
  addressDescription?: string;
  loyaltyRuleId?: number;
  loyaltyRuleName?: string;
  deliveryManId?: number;
  deliveryManName?: string;
  guestName?: string;
  type?: "delivery" | "pickup" | "reservation";
  typeDisplayName?: string;
  deliveryFee?: number;
  reservedFor?: string; // ISO 8601 format
  status: "pending" | "taken" | "inPreparation" | "ready" | "onTheWay" | "delivered" | "cancelled";
  statusDisplayName?: string;
  statusTimes: { [key: string]: string };
  subtotal: number;
  total: number;
  discountTotal: number;
  notes?: string;
  cancelledReason?: string;
  createdAt: string;
  updatedAt: string;
  bankPayments: BankPaymentDto[];
  appPayments: AppPaymentDto[];
}

interface BankPaymentDto {
  id: number;
  orderId: number;
  bankId: number;
  bankName: string;
  branchId: number;
  branchName: string;
  amount: number;
  isVerified: boolean;
  verifiedAt?: string;
  createdAt: string;
  updatedAt: string;
}

interface AppPaymentDto {
  id: number;
  orderId: number;
  appId: number;
  appName: string;
  bankId: number;
  bankName: string;
  branchId: number;
  branchName: string;
  amount: number;
  isSetted: boolean;
  createdAt: string;
  updatedAt: string;
}
```

## Implementación React

```typescript
import { useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';

export const useOrderNotifications = (userRole: string, branchId: number) => {
  const [connection, setConnection] = useState<signalR.HubConnection | null>(null);
  const [newOrders, setNewOrders] = useState<OrderDto[]>([]);
  const [readyOrders, setReadyOrders] = useState<OrderDto[]>([]);
  const [reservations, setReservations] = useState<OrderDto[]>([]);

  useEffect(() => {
    const startConnection = async () => {
      const newConnection = new signalR.HubConnectionBuilder()
        .withUrl("http://localhost:5257/hubs/orders", {
          accessTokenFactory: () => {
            const token = localStorage.getItem("token") || "";
            return token;
          }
        })
        .withAutomaticReconnect()
        .build();

      // Configurar listeners según el rol
      if (userRole === "kitchen") {
        newConnection.on("NewOrder", (order: OrderDto) => {
          setNewOrders(prev => [order, ...prev]);
        });

        newConnection.on("ReservationReady", (order: OrderDto) => {
          setReservations(prev => [order, ...prev]);
        });
      }

      if (userRole === "deliveryman") {
        newConnection.on("OrderReady", (order: OrderDto) => {
          setReadyOrders(prev => [order, ...prev]);
        });
      }

      await newConnection.start();
      setConnection(newConnection);
    };

    startConnection();

    return () => {
      if (connection) {
        connection.stop();
      }
    };
  }, [userRole, branchId]);

  return { newOrders, readyOrders, reservations };
};
```

## Manejo de Autenticación

- Se requiere token JWT en cada petición
- El token va como query parameter: `?access_token={token}`
- Renovar el token si expira

## Grupos y Filtros

- Grupos: `Branch_{branchId}`, `Branch_{branchId}_Kitchen`, `Branch_{branchId}_Delivery`
- Notificaciones: filtradas por sucursal y rol

## Manejo de Errores

```typescript
connection.onclose((error) => {
  if (error) {
    console.error("Conexión cerrada con error:", error);
    // Intentar reconectar
    setTimeout(() => {
      startConnection();
    }, 3000);
  }
});

connection.onreconnecting(() => {
  console.log("Intentando reconectar...");
});

connection.onreconnected(() => {
  console.log("Reconectado exitosamente");
});
```

## Testing

```javascript
// Simular conexión y recibir notificaciones
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5257/hubs/orders", {
    accessTokenFactory: () => "tu_token_aqui"
  })
  .build();

connection.on("NewOrder", (order) => {
  console.log("Nuevo pedido:", order);
});

await connection.start();
console.log("Conectado");
```

## Endpoints Relacionados

Además de notificaciones en tiempo real, también tenemos:

- `GET /api/orders?fromDate=2025-10-25&page=1&pageSize=10` - Lista paginada de órdenes con pagos incluidos
- `GET /api/orders/{id}/details` - Detalles completos de una orden
- `POST /api/bankpayments/{id}/verify` - Verificar pago bancario (sin body)
- `DELETE /api/bankpayments/{id}` - Eliminar pago bancario
- `DELETE /api/apppayments/{id}` - Eliminar pago de app
