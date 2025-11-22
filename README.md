# SenorArroz API

Sistema de gestión para restaurantes construido con ASP.NET Core 8, Entity Framework Core y PostgreSQL.

## 🚀 Inicio Rápido

### Requisitos Previos
- .NET 8.0 SDK
- PostgreSQL 14+
- IDE: Visual Studio 2022 o VS Code

### Configuración

1. **Clonar el repositorio**
   ```bash
   git clone <repository-url>
   cd senorArrozAPI
   ```

2. **Configurar base de datos**
   - Crear base de datos en PostgreSQL
   - Actualizar connection string en `appsettings.json`

3. **Ejecutar la aplicación**
   ```bash
   cd SenorArroz.API
   dotnet run
   ```

4. **Acceder a Swagger**
   - Abre `http://localhost:5257` en tu navegador
   - Documentación interactiva de la API

---

## 📚 Documentación

### Para Desarrolladores Nuevos (Lee en este orden)

1. **[ARQUITECTURA.md](ARQUITECTURA.md)** - Estructura del proyecto y capas
2. **[FLUJO-DE-DATOS.md](FLUJO-DE-DATOS.md)** - Cómo fluyen los datos (CQRS)
3. **[CONVENCIONES.md](CONVENCIONES.md)** - Naming y patrones de código
4. **[AUTENTICACION-AUTORIZACION.md](AUTENTICACION-AUTORIZACION.md)** - Sistema de seguridad
5. **[MANEJO-ERRORES.md](MANEJO-ERRORES.md)** - Excepciones y respuestas de error
6. **[REGLAS-NEGOCIO.md](REGLAS-NEGOCIO.md)** - Validaciones por rol y estado

### Para Frontend

- **[RESPUESTAS-FRONTEND.md](RESPUESTAS-FRONTEND.md)** - Endpoints y estructura de respuestas

### Para Agentes IA

- **[.cursorrules](.cursorrules)** - Reglas para Cursor AI y otros agentes

---

## 🏗️ Arquitectura

```
┌─────────────────────────────────┐
│     SenorArroz.API (Web API)    │  Controllers, Middleware
└──────────────┬──────────────────┘
               │
               ↓
┌─────────────────────────────────┐
│  SenorArroz.Application (CQRS)  │  Commands, Queries, DTOs
└──────────────┬──────────────────┘
               │
               ↓
┌─────────────────────────────────┐
│   SenorArroz.Domain (Entities)  │  Core Business Logic
└─────────────────────────────────┘
               ↑
               │
┌─────────────────────────────────┐
│ SenorArroz.Infrastructure (EF)  │  Repositories, DbContext
└─────────────────────────────────┘
```

**Principios**:
- Clean Architecture
- CQRS (Command Query Responsibility Segregation)
- Repository Pattern
- Dependency Injection

---

## 🔑 Tecnologías Principales

| Tecnología | Uso |
|------------|-----|
| ASP.NET Core 8 | Framework Web API |
| Entity Framework Core 8 | ORM |
| PostgreSQL | Base de datos |
| MediatR | Patrón Mediator (CQRS) |
| AutoMapper | Mapeo DTO ↔ Entity |
| JWT | Autenticación |
| Swagger/OpenAPI | Documentación API |
| BCrypt | Hash de contraseñas |

---

## 📁 Estructura del Proyecto

```
senorArrozAPI/
├── SenorArroz.API/                    → Presentation Layer
│   ├── Controllers/                   → API Endpoints
│   ├── Middleware/                    → Error handling, JWT
│   └── Program.cs                     → App configuration
│
├── SenorArroz.Application/            → Application Layer
│   ├── Features/                      → CQRS organizad por feature
│   │   ├── Orders/
│   │   │   ├── Commands/              → CreateOrder, UpdateOrder
│   │   │   ├── Queries/               → GetOrders, SearchOrders
│   │   │   └── DTOs/                  → Contratos de API
│   │   ├── Users/
│   │   ├── Customers/
│   │   └── ...
│   ├── Mappings/                      → AutoMapper Profiles
│   ├── Common/
│   │   ├── Interfaces/                → ICurrentUser, etc.
│   │   └── Services/                  → Business Rules
│   └── DependencyInjection.cs         → Registro de servicios
│
├── SenorArroz.Domain/                 → Domain Layer
│   ├── Entities/                      → Order, User, Customer
│   ├── Enums/                         → OrderStatus, UserRole
│   ├── Exceptions/                    → BusinessException
│   └── Interfaces/                    → Contratos
│       ├── Repositories/              → IOrderRepository
│       └── Services/                  → IJwtService
│
├── SenorArroz.Infrastructure/         → Infrastructure Layer
│   ├── Data/
│   │   ├── ApplicationDbContext.cs    → EF Core DbContext
│   │   └── Configurations/            → Entity configs
│   ├── Repositories/                  → Implementaciones
│   ├── Services/                      → JwtService, EmailService
│   └── DependencyInjection.cs         → Registro de servicios
│
└── SenorArroz.Tests/                  → Tests
    ├── Unit/
    └── Integration/
```

---

## 🔐 Autenticación

El sistema usa **JWT (JSON Web Tokens)** para autenticación stateless.

### Login

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "admin@example.com",
  "password": "password"
}
```

**Respuesta**:
```json
{
  "accessToken": "eyJhbGc...",
  "refreshToken": "550e8400...",
  "expiresIn": 3600,
  "user": {
    "id": 1,
    "name": "Admin User",
    "email": "admin@example.com",
    "role": "admin",
    "branchId": 1,
    "branchName": "Sucursal Centro"
  }
}
```

### Uso del Token

```http
GET /api/orders
Authorization: Bearer eyJhbGc...
```

---

## 👥 Roles del Sistema

| Rol | Descripción | Permisos |
|-----|-------------|----------|
| **Superadmin** | Control total | Todo sin restricciones |
| **Admin** | Administrador de sucursal | Control de su sucursal |
| **Cashier** | Cajero | Pedidos y pagos (con restricciones) |
| **Kitchen** | Cocina | Solo estados de preparación |
| **Deliveryman** | Domiciliario | Auto-asignación de pedidos |

Ver detalles en [AUTENTICACION-AUTORIZACION.md](AUTENTICACION-AUTORIZACION.md)

---

## 📋 Módulos Principales

### Orders (Pedidos)
- Creación de pedidos (Onsite, Delivery, Reservation)
- Cambio de estados del flujo
- Asignación de domiciliarios
- Cancelación de pedidos

### Users (Usuarios)
- Gestión de usuarios del sistema
- Asignación de roles
- Control de acceso por sucursal

### Customers (Clientes)
- Registro de clientes
- Gestión de direcciones
- Reglas de fidelidad

### Payments (Pagos)
- Pagos bancarios (verificación)
- Pagos por apps (liquidación)
- Control de pagos por sucursal

### Products (Productos)
- Catálogo de productos
- Categorías
- Precios

---

## 🔄 Flujo Típico de un Pedido

```
1. Taken (Cajero toma el pedido)
   ↓
2. InPreparation (Cocina empieza a preparar)
   ↓
3. Ready (Cocina termina)
   ↓
4. OnTheWay (Domiciliario en camino) - Solo para Delivery
   ↓
5. Delivered (Entregado)
```

**Alternativamente**: Cancelled (en cualquier momento por Admin)

---

## 🛠️ Scripts Útiles

### Desarrollo

```bash
# Compilar
dotnet build

# Ejecutar
dotnet run --project SenorArroz.API

# Tests
dotnet test

# Limpiar
dotnet clean
```

### Base de Datos

El proyecto utiliza **Entity Framework Core Migrations** para gestionar la estructura de la base de datos. Las migraciones se ejecutan **manualmente** (no automáticamente al iniciar la aplicación).

**Migraciones disponibles:**

1. **InitialSchema**: Crea toda la estructura de la base de datos (tablas, índices, foreign keys)
2. **CreateDatabaseFunctionsAndTriggers**: Crea funciones y triggers de PostgreSQL
3. **SeedInitialData**: Inserta datos iniciales (sucursal, usuarios, barrios, banco, app, clientes, productos)

**Comandos:**

```bash
# Crear nueva migración
dotnet ef migrations add NombreMigracion --project SenorArroz.Infrastructure --startup-project SenorArroz.API

# Aplicar migraciones (desde tu máquina local)
dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API

# Aplicar migraciones en Docker
docker exec senorarroz-api dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API

# Ver migraciones aplicadas
dotnet ef migrations list --project SenorArroz.Infrastructure --startup-project SenorArroz.API
```

**Nota**: Las migraciones son idempotentes y solo se ejecutan si no se han aplicado previamente.

---

## 🐛 Debugging

### Ver Logs

La aplicación loggea a consola. Niveles:
- **Information**: Operaciones normales
- **Warning**: Situaciones inusuales
- **Error**: Excepciones y fallos

### Habilitar Logs Detallados

```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Information"  // Ver queries SQL
    }
  }
}
```

---

## 📞 Soporte

Para preguntas o problemas:
1. Revisar la documentación en `/docs`
2. Consultar con el equipo de desarrollo
3. Revisar los ejemplos en `Features/Orders` (ejemplo completo)

---

## 📄 Licencia

Propietario: SenorArroz Restaurant Management System

---

## 🎯 Próximos Pasos

### Para Desarrolladores Nuevos

1. ✅ Leer [ARQUITECTURA.md](ARQUITECTURA.md)
2. ✅ Entender [FLUJO-DE-DATOS.md](FLUJO-DE-DATOS.md)
3. ✅ Explorar el módulo `Orders` como ejemplo completo
4. ✅ Revisar [CONVENCIONES.md](CONVENCIONES.md)
5. ✅ Familiarizarse con [AUTENTICACION-AUTORIZACION.md](AUTENTICACION-AUTORIZACION.md)
6. 🚀 Crear tu primer feature siguiendo los patrones establecidos

### Para Agentes IA

1. ✅ Leer [.cursorrules](.cursorrules)
2. ✅ Entender las convenciones de naming
3. ✅ Seguir los patrones CQRS establecidos
4. ✅ Usar excepciones personalizadas
5. ✅ Validar permisos y sucursales

---

**Última actualización**: Octubre 2024  
**Versión API**: 2.0

