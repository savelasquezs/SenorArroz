# Comparación de Esquema: salida.sql vs Entity Framework

Este documento detalla las diferencias encontradas entre el esquema de base de datos en `salida.sql` y las configuraciones de Entity Framework Core.

## Resumen Ejecutivo

Se encontraron **4 diferencias principales** que requieren sincronización:

1. Tabla `address`: Falta columna `is_primary`
2. Tabla `order`: Falta columna `guestname`
3. Tabla `bank_payment`: Falta columna `verified_at` (script existente)
4. Tabla `password_reset_token`: No existe en `salida.sql`

## Diferencias Detalladas

### 1. Tabla `address`

#### En salida.sql:
```sql
CREATE TABLE public.address (
    id integer NOT NULL,
    customer_id integer NOT NULL,
    neighborhood_id integer NOT NULL,
    address character varying(200) NOT NULL,
    additional_info character varying(150),
    delivery_fee integer NOT NULL,
    latitude numeric(10,6),
    longitude numeric(10,6),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);
```

#### En Entity Framework (AddressConfiguration.cs):
- **Columna faltante**: `is_primary` (boolean, DEFAULT false)

**Acción requerida**: Agregar columna `is_primary` con valor por defecto `false`.

---

### 2. Tabla `order`

#### En salida.sql:
```sql
CREATE TABLE public."order" (
    id integer NOT NULL,
    branch_id integer NOT NULL,
    taken_by_id integer NOT NULL,
    customer_id integer,
    address_id integer,
    loyalty_rule_id integer,
    delivery_man_id integer,
    type character varying NOT NULL,
    delivery_fee integer,
    reserved_for timestamp without time zone,
    status character varying NOT NULL,
    status_times jsonb,
    subtotal integer DEFAULT 0 NOT NULL,
    total integer DEFAULT 0 NOT NULL,
    discount_total integer DEFAULT 0 NOT NULL,
    notes character varying(200),
    cancelled_reason character varying(200),
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);
```

#### En Entity Framework (OrderConfiguration.cs):
- **Columna faltante**: `guestname` (varchar(100), nullable)

**Acción requerida**: Agregar columna `guestname` como nullable.

---

### 3. Tabla `bank_payment`

#### En salida.sql:
La tabla existe pero no tiene la columna `verified_at`.

#### En Entity Framework (BankPaymentConfiguration.cs):
- **Columna faltante**: `verified_at` (timestamp, nullable)

**Nota**: Ya existe un script `Scripts/add_verified_at_column_and_trigger.sql` que:
- Agrega la columna `verified_at`
- Crea un trigger para actualizar automáticamente `verified_at` cuando `is_verified` cambia

**Acción requerida**: Aplicar el script existente o incluirlo en la sincronización.

---

### 4. Tabla `password_reset_token`

#### En salida.sql:
**La tabla NO EXISTE**.

#### En Entity Framework (PasswordResetTokenConfiguration.cs):
Tabla completa con la siguiente estructura:

```sql
CREATE TABLE password_reset_token (
    id integer PRIMARY KEY,
    user_id integer NOT NULL,
    token varchar(500) NOT NULL UNIQUE,
    email varchar(100) NOT NULL,
    expires_at timestamp NOT NULL,
    is_used boolean DEFAULT false,
    used_at timestamp,
    used_by_ip varchar(45),
    created_at timestamp DEFAULT NOW(),
    updated_at timestamp DEFAULT NOW(),
    FOREIGN KEY (user_id) REFERENCES "user"(id) ON DELETE CASCADE
);
```

**Índices requeridos**:
- `idx_password_reset_token_token` (UNIQUE)
- `idx_password_reset_token_user_id`
- `idx_password_reset_token_email`
- `idx_password_reset_token_expires_at`
- `idx_password_reset_token_user_used` (compuesto: user_id, is_used)

**Acción requerida**: Crear la tabla completa con todos sus índices.

---

## Funciones y Triggers en salida.sql

El archivo `salida.sql` incluye **6 funciones** y **19 triggers** que están correctamente implementados:

### Funciones (6):

1. **`calculate_expense_detail_total()`**
   - Calcula el total de `expense_detail` antes de INSERT/UPDATE

2. **`calculate_order_detail_subtotal()`**
   - Calcula el subtotal de `order_detail` antes de INSERT/UPDATE

3. **`update_expense_header_total()`**
   - Actualiza el total de `expense_header` cuando cambia `expense_detail`

4. **`update_order_total_on_delivery_fee_change()`**
   - Actualiza el total de la order cuando cambia `delivery_fee`

5. **`update_order_totals()`**
   - Actualiza subtotal, discount_total y total de la order cuando cambia `order_detail`

6. **`update_updated_at_column()`**
   - Actualiza automáticamente `updated_at` en todas las tablas

### Triggers (19):

#### Triggers para `order_detail`:
- `calculate_order_detail_subtotal_trigger` (BEFORE INSERT/UPDATE)
- `update_order_totals_on_detail_insert` (AFTER INSERT)
- `update_order_totals_on_detail_update` (AFTER UPDATE)
- `update_order_totals_on_detail_delete` (AFTER DELETE)

#### Triggers para `order`:
- `update_order_total_on_delivery_fee_change_trigger` (BEFORE UPDATE)
- `update_order_updated_at` (BEFORE UPDATE)

#### Triggers para `expense_detail`:
- `calculate_expense_detail_total_trigger` (BEFORE INSERT/UPDATE)
- `update_expense_header_total_on_detail_insert` (AFTER INSERT)
- `update_expense_header_total_on_detail_update` (AFTER UPDATE)
- `update_expense_header_total_on_detail_delete` (AFTER DELETE)

#### Triggers para `updated_at` (en todas las tablas):
- `update_address_updated_at`
- `update_branch_updated_at`
- `update_customer_updated_at`
- `update_neighborhood_updated_at`
- `update_order_detail_updated_at`
- `update_product_category_updated_at`
- `update_product_updated_at`
- `update_refresh_token_updated_at`
- `update_user_updated_at`

---

## Plan de Sincronización

### Orden de Ejecución:

1. **Restaurar `salida.sql` completo**
   - Esto crea toda la estructura, triggers, funciones y datos existentes

2. **Aplicar `Scripts/sync-schema-to-ef.sql`**
   - Agrega columnas faltantes: `is_primary`, `guestname`, `verified_at`
   - Crea tabla `password_reset_token` completa

3. **Aplicar `Scripts/seed-data.sql`** (opcional)
   - Datos iniciales: Branch, Usuarios, Productos de prueba

### Scripts Requeridos:

- ✅ `Scripts/sync-schema-to-ef.sql` - Sincronización de esquema
- ✅ `Scripts/seed-data.sql` - Datos iniciales
- ✅ `Scripts/add_verified_at_column_and_trigger.sql` - Ya existe (incluir en sync)

---

## Verificación Post-Sincronización

Después de aplicar los scripts, verificar:

```sql
-- Verificar is_primary en address
SELECT column_name, data_type, column_default 
FROM information_schema.columns 
WHERE table_name = 'address' AND column_name = 'is_primary';

-- Verificar guestname en order
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'order' AND column_name = 'guestname';

-- Verificar verified_at en bank_payment
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'bank_payment' AND column_name = 'verified_at';

-- Verificar password_reset_token existe
SELECT table_name 
FROM information_schema.tables 
WHERE table_name = 'password_reset_token';

-- Verificar índices de password_reset_token
SELECT indexname 
FROM pg_indexes 
WHERE tablename = 'password_reset_token';
```

---

## Notas Importantes

1. **Todos los scripts deben ser idempotentes**: Usar `IF NOT EXISTS` y `ON CONFLICT DO NOTHING`

2. **Triggers y funciones**: Ya están incluidos en `salida.sql`, no requieren sincronización

3. **Datos existentes**: `salida.sql` puede contener datos. Los scripts de sincronización deben manejar esto correctamente.

4. **Orden de ejecución en Docker**: Los scripts en `Scripts/` se ejecutan automáticamente en orden alfabético cuando se crea el contenedor por primera vez.

---

**Última actualización**: Noviembre 2024  
**Archivos comparados**: `salida.sql` vs Entity Framework Configurations

