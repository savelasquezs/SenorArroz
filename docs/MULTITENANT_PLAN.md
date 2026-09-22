# Señor Arroz - Multitenant Plan

## Decisión arquitectónica

Señor Arroz debe evolucionar a SaaS usando:

```text
Una sola base de datos PostgreSQL compartida
+ columna TenantId en tablas del negocio
+ filtros globales en EF Core
+ tenant resuelto en backend
```

No usar una base de datos por cliente en esta etapa. No usar schema por cliente salvo que exista una razón fuerte futura.

## Contexto importante

El sistema ya tiene clientes reales. Por eso la migración debe ser progresiva y segura.

No se debe agregar `TenantId` a todas las tablas de golpe sin plan de backfill, pruebas y despliegue controlado.

## Estado actual: núcleo organizacional y de clientes implementado

La primera base real agrega `TenantId` a `Branch`, `Customer`, `Address`, `Neighborhood` y `Order`, y crea `CustomerPhone` y `AddressBranch`. El tenant se toma del claim/configuración segura del backend; nunca del request público. El resto de tablas tenant-owned continúa en las fases progresivas de este documento.

El modelo conceptual de clientes es:

```text
Tenant -> Customers -> CustomerPhones
Tenant -> Branches
Customer -> Addresses -> AddressBranches -> Branch
Customer -> Orders -> Branch
```

- `Tenant` es la raíz organizacional y conserva `Name`, `Slug`, `IsActive`, `CreatedAt` y `UpdatedAt`.
- `Tenant` incorpora `PublicId`, `Status`, `StatusReason`, `AccessVersion`, datos de contacto/facturación y fechas de suspensión/cancelación.
- `Branch` y `User` pertenecen directamente a un tenant. La FK compuesta `(tenant_id, branch_id)` impide asignar un usuario a una sucursal de otro tenant.
- `RefreshToken`, `PasswordResetToken` y `UserDeviceToken` pertenecen al mismo tenant que su usuario mediante FKs compuestas.
- Login y refresh cargan `Tenant`, `Branch` y `User`, validan consistencia y emiten `tenant_id`, `tenant_public_id` y `tenant_access_version` desde datos persistidos.
- `ICurrentTenant` resuelve requests autenticados desde JWT y procesos públicos/background desde configuración segura del servidor.
- `X-Branch-Id` mantiene su semántica operativa, pero una selección autenticada debe pertenecer al tenant del JWT.
- El esquema se instala con `SenorArroz.Infrastructure/Scripts/multitenant_core_v2.sql`.

`IsActive` se mantiene por compatibilidad. Mientras exista, el acceso exige simultáneamente `IsActive = true` y `Status = Active`; todo cambio de estado debe mantener ambos valores sincronizados.

## Objetivo final

Modelo lógico esperado:

```text
Tenant
  └── Branch
        ├── Users
        ├── Orders
        ├── Customers
        ├── Products
        ├── Payments
        ├── Expenses
        ├── Delivery routes
        └── Print settings/jobs
```

`BranchId` sigue existiendo, pero queda subordinado a `TenantId`.

## Regla principal

Toda información operativa de un restaurante debe pertenecer a un tenant.

El frontend nunca debe decidir el tenant. El tenant debe resolverse en backend desde:

- Usuario autenticado
- Claim JWT
- Subdominio
- Configuración segura
- Token de agente de impresión asociado a sucursal/tenant

## Fases recomendadas

### Fase 0 - Documentación y mapa

Crear y mantener:

- `AGENTS.md`
- `docs/PROJECT_MAP.md`
- `docs/DATABASE_MAP.md`
- `docs/BUSINESS_RULES.md`
- `docs/MULTITENANT_PLAN.md`

Objetivo: que Codex y los humanos sepan dónde tocar sin escanear todo.

### Fase 1 - Introducir entidad Tenant sin romper operación (completada)

Crear entidad `Tenant`.

Campos mínimos sugeridos:

```csharp
public class Tenant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

Crear tenant inicial para el negocio actual, por ejemplo:

```text
TenantId = 1
Name = Señor Arroz
Slug = senor-arroz
```

### Fase 2 - Asociar Branch a Tenant (completada)

Estado previo implementado: existe `IBranchContext` y el selector de Superadmin usa
`X-Branch-Id`. Este header solo selecciona una sucursal operativa; nunca es autoridad
de tenant. Al implementar esta fase, el tenant deberá provenir del JWT/contexto seguro
y `IBranchContext` deberá validar que la sucursal seleccionada pertenezca a ese tenant.

Agregar `TenantId` a `Branch` primero.

Backfill:

```sql
UPDATE branches SET tenant_id = 1 WHERE tenant_id IS NULL;
```

Después de validar datos:

- Marcar `tenant_id` como NOT NULL.
- Crear índice `ix_branches_tenant_id`.
- Crear índice único recomendado: `(tenant_id, name)` si aplica.

### Fase 3 - Propagar TenantId a tablas dependientes (Bloque 2 completado)

El Bloque 2 propagó `TenantId` a las 93 entidades operativas, dejando `Tenant` como única entidad global. También incorporó filtros globales, write guards, scopes explícitos de sistema/tenant, aislamiento de workers y el script idempotente `multitenant_isolation_v2.sql`.

La matriz completa y las excepciones están en `docs/MULTITENANT_ISOLATION_AUDIT.md`.

Pendiente para el Bloque 3: RLS, SignalR/archivos, control plane, planes, add-ons, suscripciones, metering, invitaciones, portal `/platform` y alta controlada del segundo restaurante real.

Agregar `TenantId` progresivamente a tablas operativas.

Orden recomendado:

1. Branches
2. Users
3. Customers
4. Addresses
5. Neighborhoods
6. ProductCategories
7. Products
8. Orders
9. OrderDetails
10. Payments: BankPayment, AppPayment, BankTransfer, ReservationDeposit
11. Cash closures and vault movements
12. Expenses and suppliers
13. Delivery routes, stops, locations and advances
14. Print settings and print jobs
15. Tokens and device tokens según corresponda

## Backfill recomendado

Cuando una tabla tiene `BranchId`:

```sql
UPDATE target_table t
SET tenant_id = b.tenant_id
FROM branches b
WHERE t.branch_id = b.id
  AND t.tenant_id IS NULL;
```

Cuando una tabla depende de Order:

```sql
UPDATE order_details od
SET tenant_id = o.tenant_id
FROM orders o
WHERE od.order_id = o.id
  AND od.tenant_id IS NULL;
```

Cuando una tabla depende de User:

```sql
UPDATE user_device_tokens udt
SET tenant_id = u.tenant_id
FROM users u
WHERE udt.user_id = u.id
  AND udt.tenant_id IS NULL;
```

Cada tabla debe tener su estrategia documentada en `docs/DATABASE_MAP.md`.

## Fase 4 - Implementar contexto de tenant

Crear abstracción de aplicación:

```csharp
public interface ICurrentTenant
{
    int TenantId { get; }
    bool HasTenant { get; }
}
```

Implementaciones posibles:

- Desde JWT claim `tenant_id`.
- Desde usuario actual.
- Desde subdominio.
- Desde token de print agent.

Regla:

```text
Handlers y repositorios no deben recibir TenantId desde request público.
```

## Fase 5 - Filtros globales en EF Core

Aplicar filtros globales para entidades tenant-owned.

Ejemplo conceptual:

```csharp
modelBuilder.Entity<Order>()
    .HasQueryFilter(x => x.TenantId == _currentTenant.TenantId);
```

Evitar depender solamente de filtros manuales en repositorios.

Los filtros manuales pueden existir como defensa adicional, pero no deben ser la única protección.

## Fase 6 - Índices únicos compuestos

Todo índice único que hoy sea global debe revisarse.

Ejemplos:

```text
Antes:  UNIQUE (name)
Después: UNIQUE (tenant_id, name)
```

```text
Antes:  UNIQUE (phone)
Después: UNIQUE (tenant_id, phone)
```

No todos los campos deben ser únicos por tenant, pero todo índice único debe revisarse explícitamente.

## Fase 7 - Seguridad y pruebas

Crear pruebas mínimas:

- Tenant A no ve pedidos de Tenant B.
- Tenant A no puede modificar cliente de Tenant B.
- Superadmin conserva acceso controlado.
- Usuario de sucursal solo ve su sucursal dentro de su tenant.
- Print agent solo obtiene trabajos de su sucursal y tenant.
- App móvil solo opera sobre domiciliario/sucursal/tenant autorizados.

## Reglas para Codex

Cuando una tarea mencione multitenant, SaaS, tenants, sucursales, permisos, queries, filtros o seguridad de datos:

1. Leer `AGENTS.md`.
2. Leer este archivo.
3. Leer `docs/DATABASE_MAP.md`.
4. Identificar si las tablas tocadas son tenant-owned.
5. Proponer plan antes de editar.
6. No hacer migraciones destructivas sin backfill.
7. No usar `request.TenantId` como fuente de verdad.
8. Actualizar documentación si cambia el modelo.

## Criterios de terminado

Una feature está lista para multitenant cuando:

- Sus tablas tienen `TenantId` si son tenant-owned.
- Tiene índices adecuados por tenant.
- Sus queries quedan filtradas automáticamente o de forma garantizada.
- Sus endpoints no aceptan tenant desde frontend como autoridad.
- Sus pruebas o validaciones manuales cubren aislamiento entre tenants.
- La documentación queda actualizada.
