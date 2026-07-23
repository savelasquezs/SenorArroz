# Migración de Base de Datos en Railway

## Procedimiento obligatorio

Todos los scripts SQL de este repositorio destinados a Railway deben ejecutarse desde **Bash** mediante la conexión del servicio `MainDatabase`. Desde la raíz del backend:

```bash
cd SenorArroz
railway connect MainDatabase
```

La conexión abre una sesión interactiva de `psql`. Dentro de ella se ejecuta el script requerido con `\i`:

```sql
\i SenorArroz.Infrastructure/Scripts/<script>.sql
\q
```

Por ejemplo, para instalar el esquema de sesión exclusiva de domiciliarios:

```sql
\i SenorArroz.Infrastructure/Scripts/add_exclusive_delivery_sessions.sql
```

Si el script está en la carpeta histórica `Scripts`, se conserva el mismo procedimiento y solo cambia la ruta usada con `\i`. El código que dependa del cambio de esquema debe desplegarse únicamente después de comprobar que el script terminó sin errores.

## Información del Proyecto

- **Proyecto**: señor arroz c# vue js
- **Project ID**: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- **Plataforma**: Railway
- **Servicio PostgreSQL**: `MainDatabase`
- **Scripts SQL**: 
  - `railway-initial-utf8.sql` - Migración inicial completa
  - `Scripts/deliveryman.sql` - Tabla `deliveryman_advance` para gestión de abonos
  - `Scripts/update-product-table.sql` - Columna `product.weight_grams` (incremental; ver proceso abajo)
  - `Scripts/update-product-weights-by-size.sql` - Rellena `weight_grams` por tamaño (Personal/Dúo/Trío/Familiar/Súper) para productos `Arroz%` con/sin chicharrón

## Aplicar `update-product-table.sql` con Railway CLI (`railway connect` + `\i`)

Proceso usado para **añadir `weight_grams`** a la tabla `product` en Railway sin pegar la URL a mano:

1. **Instalar** [Railway CLI](https://docs.railway.com/guides/cli) (si no lo tenés).
2. **Iniciar sesión** y **enlazar el proyecto** (una vez por carpeta):
   ```bash
   cd senorArrozAPI
   railway login
   railway link
   ```
   Elegí el workspace, el proyecto (p. ej. *señor arroz c# vue js*), el entorno **production** y el servicio **PostgreSQL** (p. ej. *MainDatabase*).
3. **Abrir `psql` conectado a `MainDatabase`** (desde la carpeta del API para que `\i` relativo funcione):
   ```bash
   cd senorArrozAPI
   railway connect MainDatabase
   ```
   Se abre una sesión interactiva de `psql` ya autenticada (sin exponer `DATABASE_URL` en la terminal).
4. **Ejecutar el script** (ajustá la ruta si tu usuario o carpeta distintos):
   - **Git Bash / Linux / macOS** (con el `cd` del paso 3):
     ```sql
     \i Scripts/update-product-table.sql
     ```
     `\i` resuelve rutas respecto al directorio de trabajo actual del proceso `psql` (por eso el `cd senorArrozAPI` antes de conectar).
   - **Windows:** si `\i` no encuentra el archivo, usá ruta con barras y comillas:
     ```sql
     \i 'C:/Users/TU_USUARIO/source/repos/SenorArroz/senorArrozAPI/Scripts/update-product-table.sql'
     ```
5. Deberías ver `BEGIN` / `ALTER TABLE` / `COMMENT` / `COMMIT` sin errores. Salir con `\q`.

Para scripts en Railway no se usa una conexión pública directa: se aplica el procedimiento obligatorio con `railway connect MainDatabase`.

---

## Prerrequisitos

1. Tener Railway CLI instalado y configurado
2. Tener el proyecto vinculado a Railway
3. Tener acceso al servicio PostgreSQL en Railway

## Conexión

Railway es la plataforma y `MainDatabase` es el servicio PostgreSQL. El nombre interno de la base se obtiene de `PGDATABASE`; no debe documentarse como `railway` ni inferirse del nombre del servicio. Los valores de conexión vigentes se consultan en las variables de `MainDatabase` y no se copian en el repositorio.

## Pasos para Ejecutar la Migración

### Método recomendado: sesión interactiva con Railway CLI desde Bash

```bash
# Desde la raíz del backend
cd SenorArroz

# Conectarse al servicio PostgreSQL MainDatabase alojado en Railway
railway connect MainDatabase
```

Esto abrirá una sesión interactiva de `psql` conectada al servicio `MainDatabase`.

Una vez dentro de `psql`, ejecuta el script:

```sql
-- Desde dentro de psql
\i Scripts/deliveryman.sql
```

`railway run ... psql -f` no es el procedimiento aprobado para aplicar scripts. Usar siempre la sesión abierta desde Bash con `railway connect MainDatabase` y ejecutar `\i` dentro de `psql`.

### Paso 3: Verificar la Ejecución

Después de ejecutar el script, verifica que se haya ejecutado correctamente:

```sql
-- Verificar que las migraciones se registraron
SELECT "MigrationId", "ProductVersion" 
FROM "__EFMigrationsHistory" 
ORDER BY "MigrationId";
```

Deberías ver:
- `20251122122758_InitialSchema`
- `20251122123044_CreateDatabaseFunctionsAndTriggers`
- `20251122123208_SeedInitialData`

### Paso 4: Verificar Datos Iniciales

#### Verificar usuarios creados

```sql
SELECT name, email, role FROM "user" ORDER BY role, name;
```

Deberías ver:
- Santiago (superadmin)
- Daniel Alvarez (admin)
- Abelardo (deliveryman)
- Maikol Martinez Serna (deliveryman)
- juan (kitchen)

#### Verificar barrios

```sql
SELECT name, delivery_fee FROM neighborhood ORDER BY name;
```

Deberías ver: Castilla, Florencia, Pedregal, Picacho, Santander

#### Verificar productos

```sql
SELECT COUNT(*) as total_productos FROM product;
```

Deberías ver: 28 productos

#### Verificar banco y app

```sql
SELECT b.name as banco, a.name as app 
FROM bank b 
LEFT JOIN app a ON a.bank_id = b.id;
```

Deberías ver: Bancolombia y Didi

#### Verificar clientes

```sql
SELECT COUNT(*) as total_clientes FROM customer;
```

Deberías ver: 8 clientes

## Ejecutar Migración de Tabla deliveryman_advance

Para agregar la tabla `deliveryman_advance` (gestión de abonos a domiciliarios):

```bash
# Desde Bash y la raíz del backend
railway connect MainDatabase
```

```sql
\i Scripts/deliveryman.sql
```

**Verificar que se creó correctamente:**

```sql
-- Verificar la tabla
\d deliveryman_advance

-- Verificar la migración
SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128005639_AddDeliverymanAdvanceTable';
```

**Características del Script deliveryman.sql:**

- Usa transacción explícita (`BEGIN;` / `COMMIT;`) para garantizar atomicidad
- Delimitador `$migration$` para evitar conflictos con funciones que usan `$$`
- Función `update_deliveryman_advance_updated_at()` creada dentro del bloque condicional
- Idempotente: puede ejecutarse múltiples veces sin errores

## Ejecutar Migración: AddCreatedByIdToExpenseHeader

Para agregar la columna `created_by_id` a la tabla `expense_header`:

```bash
# Desde Bash y la raíz del backend
railway connect MainDatabase
```

```sql
\i Scripts/add-created-by-id-to-expense-header.sql
```

**Verificar que se aplicó correctamente:**

```sql
-- Verificar que la columna existe
\d expense_header

-- Verificar la migración
SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251128025240_AddCreatedByIdToExpenseHeader';

-- Verificar que los registros existentes tienen created_by_id
SELECT COUNT(*) as total, COUNT(created_by_id) as con_created_by 
FROM expense_header;
```

**Características del Script add-created-by-id-to-expense-header.sql:**

- Usa transacción explícita (`BEGIN;` / `COMMIT;`) para garantizar atomicidad
- Delimitador `$migration$` para evitar conflictos
- Actualiza registros existentes asignando el primer admin/usuario de cada sucursal
- Idempotente: puede ejecutarse múltiples veces sin errores
- Verifica existencia antes de crear columnas, índices y foreign keys

## Ejecutar Migración: AddBranchIdToSupplier

Para convertir los proveedores en entidades por sucursal ejecuta el nuevo script `Scripts/add-branch-id-to-supplier.sql`.

**Railway (psql contra la base en la nube):**

```bash
# Desde Bash y la raíz del backend
railway connect MainDatabase
```

```sql
\i Scripts/add-branch-id-to-supplier.sql
```

**Docker / Postgres local (mismo script, connection string local):**

```bash
# Desde el directorio senorArrozAPI
psql "Host=localhost;Port=5433;Database=senor_arroz;Username=postgres;Password=1234" -f Scripts/add-branch-id-to-supplier.sql
```

**Verificar que se aplicó correctamente:**

```sql
-- Confirmar columna y constraint
\d supplier

-- Validar que no queden proveedores sin branch
SELECT COUNT(*) FILTER (WHERE branch_id IS NULL) AS proveedores_sin_branch FROM supplier;

-- Revisar índice
SELECT indexname FROM pg_indexes WHERE tablename = 'supplier' AND indexname = 'idx_supplier_branch_name';
```

**Características del Script add-branch-id-to-supplier.sql:**

- Usa `BEGIN/COMMIT` para atomicidad.
- Agrega `branch_id` si aún no existe.
- Pobla los valores usando `expense_header` y, si es necesario, la primera sucursal disponible.
- Marca la columna como `NOT NULL`, crea el índice `idx_supplier_branch_name` y la FK `FK_supplier_branch_branch_id` solo si no existen.
- Idempotente: puede ejecutarse múltiples veces sin errores.

## Scripts Disponibles

### `railway-initial-utf8.sql`
Migración inicial completa del sistema. Incluye:
- Estructura de todas las tablas
- Índices y Foreign Keys
- Funciones y Triggers
- Datos iniciales (usuarios, barrios, productos, clientes, etc.)

### `Scripts/deliveryman.sql`
Script para crear la tabla `deliveryman_advance` (gestión de abonos a domiciliarios). Incluye:
- Tabla `deliveryman_advance` con todas sus columnas y constraints
- Función `update_deliveryman_advance_updated_at()` para actualizar `updated_at` automáticamente
- Trigger asociado
- Índices para optimización
- Comentarios descriptivos
- Registro en `__EFMigrationsHistory`

### `Scripts/add-created-by-id-to-expense-header.sql`
Script para agregar la columna `created_by_id` a la tabla `expense_header`. Incluye:
- Columna `created_by_id` en `expense_header`
- Actualización de registros existentes (asigna primer admin/usuario de cada sucursal)
- Índice `idx_expense_header_created_by` para optimización
- Foreign Key `FK_expense_header_user_created_by_id` hacia la tabla `user`
- Registro en `__EFMigrationsHistory`

### `Scripts/add-branch-id-to-supplier.sql`
Script para agregar `branch_id` a `supplier` y enlazarlos con sucursales:
- Columna `branch_id` (con valores poblados e índice `idx_supplier_branch_name`)
- Foreign Key `FK_supplier_branch_branch_id`
- Actualización de datos existente tomando `branch_id` desde `expense_header`
- Idempotente e independiente del entorno (Railway o Docker)

**Características de los Scripts:**

Todos los scripts SQL son **idempotentes**, lo que significa que:

- Pueden ejecutarse múltiples veces sin causar errores
- Usan `IF NOT EXISTS` y `ON CONFLICT DO NOTHING` para evitar duplicados
- Verifican el historial de migraciones antes de ejecutar cada sección
- Están codificados en UTF-8 sin caracteres especiales problemáticos
- Usan transacciones explícitas (`BEGIN;` / `COMMIT;`) para garantizar atomicidad

## Troubleshooting

### Error: "could not translate host name"

- **Solución recomendada:** confirma que Railway CLI esté autenticado y ejecuta `railway connect MainDatabase` desde Bash.
- Si usas `railway run`, puede fallar por resolución del host; utiliza la conexión interactiva aprobada.
- Asegúrate de que el proyecto esté vinculado: `railway link`

### Error: "password authentication failed"

- Verifica que la contraseña en el connection string sea correcta
- Si cambiaste `PGPASSWORD` en Railway, actualiza el connection string

### Error: "database does not exist"

- Verifica el valor vigente de `PGDATABASE` en `MainDatabase`.
- Railway es la plataforma; no se debe usar `railway` como nombre de base por inferencia.

### Error: "relation already exists"

- El script es idempotente, pero si hay conflictos, puedes verificar qué tablas existen:
  ```sql
  SELECT table_name FROM information_schema.tables 
  WHERE table_schema = 'public' 
  ORDER BY table_name;
  ```

### Error: "character with byte sequence ... in encoding"

- El archivo `railway-initial-utf8.sql` ya está limpio de caracteres problemáticos
- Si aún tienes problemas, verifica que el archivo esté en UTF-8 sin BOM

### Error: "file not found" al usar \i

- Asegúrate de estar en el directorio correcto antes de ejecutar `\i`
- O usa la ruta completa: `\i /ruta/completa/railway-initial-utf8.sql`

### El script se ejecuta pero no veo datos

- Verifica que el script se ejecutó completamente (debe terminar con `COMMIT;`)
- Revisa los mensajes de `psql` para ver si hubo errores
- Verifica que las migraciones se registraron en `__EFMigrationsHistory`

## Notas Importantes

1. **No Automático**: La migración NO se ejecuta automáticamente. Debe ejecutarse manualmente.

2. **Host Interno vs Público**: 
   - **Host interno** (`postgres.railway.internal`): Solo funciona dentro de Railway (entre servicios). Usado por el backend.
   - **Host público** (`centerbeam.proxy.rlwy.net:52635`): Necesario para ejecutar migraciones desde tu máquina local. Obtén el actual desde Railway Dashboard → Variables → `DATABASE_URL` o `PUBLIC_URL`.

3. **Seguridad**: No commitees connection strings con contraseñas en el repositorio. Usa variables de entorno o Railway Secrets.

4. **Idempotencia**: Los scripts pueden ejecutarse múltiples veces sin problemas gracias a las verificaciones de existencia.

5. **Encoding**: Los archivos están en UTF-8 sin BOM y sin caracteres especiales problemáticos (ñ, acentos) para evitar errores de encoding.

6. **Transacciones**: El script `deliveryman.sql` usa transacciones explícitas (`BEGIN;` / `COMMIT;`) para garantizar atomicidad.

## Estructura de los Scripts

### `railway-initial-utf8.sql`

Contiene:

1. **Creación de tablas**: Todas las tablas del esquema de la base de datos
2. **Índices y Foreign Keys**: Todas las relaciones y índices
3. **Funciones PostgreSQL**: Funciones personalizadas para cálculos y triggers
4. **Triggers**: Triggers para actualización automática de campos y cálculos
5. **Datos iniciales**:
   - 1 sucursal (Santander)
   - 5 usuarios (Santiago, Daniel, Abelardo, Maikol, Juan)
   - 5 barrios del norte de Medellín
   - 1 banco (Bancolombia)
   - 1 app (Didi)
   - 5 categorías de productos
   - 28 productos
   - 8 clientes con direcciones y coordenadas

### `Scripts/deliveryman.sql`

Contiene:

1. **Transacción explícita**: `BEGIN;` al inicio y `COMMIT;` al final
2. **Verificación de existencia**: Bloque `DO $migration$` con `IF NOT EXISTS`
3. **Creación de tabla**: `deliveryman_advance` con todas sus columnas y constraints
4. **Índices**: 4 índices para optimización de consultas
5. **Función**: `update_deliveryman_advance_updated_at()` para actualizar `updated_at`
6. **Trigger**: `trigger_deliveryman_advance_updated_at` asociado a la función
7. **Comentarios**: Descripción de tabla y columnas
8. **Registro de migración**: Inserción en `__EFMigrationsHistory`
