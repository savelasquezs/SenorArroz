# Migración de Base de Datos en Railway

## Información del Proyecto

- **Proyecto**: señor arroz c# vue js
- **Project ID**: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- **Base de Datos**: railway
- **Scripts SQL**: 
  - `railway-initial-utf8.sql` - Migración inicial completa
  - `Scripts/deliveryman.sql` - Tabla `deliveryman_advance` para gestión de abonos

## Prerrequisitos

1. Tener Railway CLI instalado y configurado
2. Tener el proyecto vinculado a Railway
3. Tener acceso al servicio PostgreSQL en Railway

## Connection String

### Formato Railway (DATABASE_URL - Host Interno)
```
postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@postgres.railway.internal:5432/railway
```
**Nota:** Este host solo funciona dentro de Railway (entre servicios).

### Formato Railway (Host Público - Para migraciones desde local)
```
postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@centerbeam.proxy.rlwy.net:52635/railway
```
**Nota:** El host público puede cambiar. Obtén el actual desde Railway Dashboard → Variables → `DATABASE_URL` o `PUBLIC_URL`.

### Formato .NET (para variables de entorno)
```
Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg
```

## Pasos para Ejecutar la Migración

### Método Recomendado: Usando psql con Connection String Público

Este es el método más confiable para ejecutar scripts SQL desde tu máquina local:

```bash
# Desde el directorio senorArrozAPI
psql "postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@centerbeam.proxy.rlwy.net:52635/railway" -f Scripts/deliveryman.sql
```

**Nota:** El host público (`centerbeam.proxy.rlwy.net:52635`) puede cambiar. Para obtener el connection string actualizado:

1. Ve a Railway Dashboard → Tu proyecto → Servicio PostgreSQL
2. Pestaña "Variables" → Busca `DATABASE_URL` o `PUBLIC_URL`
3. Copia el connection string completo

### Método Alternativo 1: Sesión Interactiva con Railway CLI

```bash
# Asegúrate de estar en el directorio correcto
cd senorArrozAPI

# Conectarse a PostgreSQL usando Railway CLI
railway connect postgres
```

Esto abrirá una sesión interactiva de `psql` conectada a la base de datos Railway.

Una vez dentro de `psql`, ejecuta el script:

```sql
-- Desde dentro de psql
\i Scripts/deliveryman.sql
```

### Método Alternativo 2: Usando Railway run (puede tener problemas de conexión)

```bash
# Desde el directorio senorArrozAPI
railway run --service MainDatabase psql -U postgres -d railway -f Scripts/deliveryman.sql
```

**Nota:** Este método puede fallar con errores de resolución de hostname. Si ocurre, usa el método recomendado.

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
# Desde el directorio senorArrozAPI
psql "postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@centerbeam.proxy.rlwy.net:52635/railway" -f Scripts/deliveryman.sql
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

**Características de los Scripts:**

Todos los scripts SQL son **idempotentes**, lo que significa que:

- Pueden ejecutarse múltiples veces sin causar errores
- Usan `IF NOT EXISTS` y `ON CONFLICT DO NOTHING` para evitar duplicados
- Verifican el historial de migraciones antes de ejecutar cada sección
- Están codificados en UTF-8 sin caracteres especiales problemáticos
- Usan transacciones explícitas (`BEGIN;` / `COMMIT;`) para garantizar atomicidad

## Troubleshooting

### Error: "could not translate host name"

- **Solución recomendada:** Usa el método con connection string público (ver Método Recomendado arriba)
- Si usas `railway run`, puede fallar. Usa `psql` directamente con el connection string público
- Para obtener el host público: Railway Dashboard → Variables → `DATABASE_URL` o `PUBLIC_URL`
- Asegúrate de que el proyecto esté vinculado: `railway link`

### Error: "password authentication failed"

- Verifica que la contraseña en el connection string sea correcta
- Si cambiaste `PGPASSWORD` en Railway, actualiza el connection string

### Error: "database does not exist"

- Verifica que el nombre de la base de datos sea `railway`
- Railway usa `railway` por defecto

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
