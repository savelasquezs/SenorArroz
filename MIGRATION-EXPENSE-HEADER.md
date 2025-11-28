# Migración: AddCreatedByIdToExpenseHeader

## Descripción

Esta migración agrega la columna `created_by_id` a la tabla `expense_header` para rastrear qué usuario creó cada gasto. Esto es necesario para implementar el control de acceso basado en roles:
- **Cashier**: Solo puede ver gastos creados por él mismo
- **Admin**: Puede ver todos los gastos de su sucursal
- **Superadmin**: Puede ver todos los gastos

## Script SQL

El script se encuentra en: `Scripts/add-created-by-id-to-expense-header.sql`

### Características del Script

- ✅ **Idempotente**: Puede ejecutarse múltiples veces sin errores
- ✅ **Transaccional**: Usa `BEGIN;` y `COMMIT;` para garantizar atomicidad
- ✅ **Actualiza datos existentes**: Asigna el primer admin/usuario de cada sucursal a los registros existentes
- ✅ **Verifica existencia**: Comprueba si la columna, índice y foreign key ya existen antes de crearlos
- ✅ **Registra migración**: Inserta el registro en `__EFMigrationsHistory`

## Aplicar en Docker

### Opción 1: Desde tu máquina local (Recomendado)

```powershell
# Desde el directorio senorArrozAPI
Get-Content Scripts/add-created-by-id-to-expense-header.sql | docker exec -i senorarroz-postgres psql -U postgres -d senor_arroz
```

### Opción 2: Copiar archivo al contenedor y ejecutar

```powershell
# Copiar el script al contenedor
docker cp Scripts/add-created-by-id-to-expense-header.sql senorarroz-postgres:/tmp/migration.sql

# Ejecutar el script
docker exec senorarroz-postgres psql -U postgres -d senor_arroz -f /tmp/migration.sql

# Limpiar
docker exec senorarroz-postgres rm /tmp/migration.sql
```

### Opción 3: Usando psql directamente (si tienes psql instalado)

```powershell
# Desde el directorio senorArrozAPI
psql -h localhost -p 5433 -U postgres -d senor_arroz -f Scripts/add-created-by-id-to-expense-header.sql
```

### Verificar que se aplicó correctamente

```powershell
# Verificar que la columna existe
docker exec senorarroz-postgres psql -U postgres -d senor_arroz -c "\d expense_header"

# Verificar que la migración está registrada (si la tabla __EFMigrationsHistory existe)
docker exec senorarroz-postgres psql -U postgres -d senor_arroz -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20251128025240_AddCreatedByIdToExpenseHeader';"

# Verificar que los registros existentes tienen created_by_id
docker exec senorarroz-postgres psql -U postgres -d senor_arroz -c "SELECT COUNT(*) as total, COUNT(created_by_id) as con_created_by FROM expense_header;"
```

## Aplicar en Railway

### Paso 1: Obtener el Connection String Público

1. Ve a [Railway Dashboard](https://railway.app)
2. Selecciona tu proyecto: **señor arroz c# vue js**
3. Haz clic en el servicio **PostgreSQL** (MainDatabase)
4. Ve a la pestaña **Variables**
5. Busca `DATABASE_URL` o `PUBLIC_URL`
6. Copia el connection string completo

**Ejemplo de formato:**
```
postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@centerbeam.proxy.rlwy.net:52635/railway
```

⚠️ **Importante**: El host público puede cambiar. Siempre obtén el connection string actualizado desde Railway Dashboard.

### Paso 2: Ejecutar el Script

#### Opción 1: Usando psql desde tu máquina local (Recomendado)

```powershell
# Desde el directorio senorArrozAPI
# Reemplaza el connection string con el actual de Railway
psql "postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@centerbeam.proxy.rlwy.net:52635/railway" -f Scripts/add-created-by-id-to-expense-header.sql
```

#### Opción 2: Usando Railway CLI

```powershell
# Asegúrate de estar en el directorio senorArrozAPI
cd senorArrozAPI

# Conectarse a PostgreSQL usando Railway CLI
railway connect postgres
```

Una vez dentro de `psql`, ejecuta:

```sql
\i Scripts/add-created-by-id-to-expense-header.sql
```

O si el archivo no está disponible en Railway, copia y pega el contenido del script directamente.

#### Opción 3: Sesión interactiva con psql

```powershell
# Conectarse a Railway PostgreSQL
psql "postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@centerbeam.proxy.rlwy.net:52635/railway"
```

Luego ejecuta:

```sql
\i Scripts/add-created-by-id-to-expense-header.sql
```

### Paso 3: Verificar que se aplicó correctamente

```sql
-- Verificar que la columna existe
\d expense_header

-- Verificar que la migración está registrada
SELECT "MigrationId" FROM "__EFMigrationsHistory" 
WHERE "MigrationId" = '20251128025240_AddCreatedByIdToExpenseHeader';

-- Verificar que los registros existentes tienen created_by_id
SELECT COUNT(*) as total, COUNT(created_by_id) as con_created_by 
FROM expense_header;

-- Verificar la estructura de la tabla
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'expense_header' 
AND column_name = 'created_by_id';
```

Deberías ver:
- La columna `created_by_id` en la descripción de la tabla
- Un registro en `__EFMigrationsHistory` con `MigrationId = '20251128025240_AddCreatedByIdToExpenseHeader'`
- Todos los registros con `created_by_id` no nulo

## Solución de Problemas

### Error: "column already exists"

Si la columna ya existe, el script es idempotente y no causará errores. Simplemente verificará que todo esté en orden.

### Error: "could not translate host name"

- **Solución**: Asegúrate de usar el connection string público actualizado desde Railway Dashboard
- Verifica que el host público sea correcto (puede cambiar)

### Error: "password authentication failed"

- Verifica que la contraseña en el connection string sea correcta
- Si cambiaste `PGPASSWORD` en Railway, actualiza el connection string

### Error: "database does not exist"

- Verifica que el nombre de la base de datos sea `railway` (o el que uses en Railway)
- Railway usa `railway` por defecto

### Error: "relation __EFMigrationsHistory does not exist"

Esto es normal si la base de datos se creó directamente sin usar migraciones de EF Core. El script registrará la migración en la tabla si existe, o la creará si no existe.

### Los registros existentes no tienen created_by_id

Si después de ejecutar el script algunos registros aún tienen `created_by_id` NULL:

1. Verifica que existan usuarios en la tabla `user`:
   ```sql
   SELECT id, name, role, branch_id FROM "user" WHERE active = true;
   ```

2. Verifica que los `expense_header` tengan `branch_id` válido:
   ```sql
   SELECT eh.id, eh.branch_id, b.id as branch_exists
   FROM expense_header eh
   LEFT JOIN branch b ON b.id = eh.branch_id
   WHERE eh.created_by_id IS NULL;
   ```

3. Si hay registros sin `branch_id` válido o sin usuarios en la sucursal, necesitarás asignarlos manualmente:
   ```sql
   UPDATE expense_header eh
   SET created_by_id = (
       SELECT u.id
       FROM "user" u
       WHERE u.active = true
       ORDER BY u.id
       LIMIT 1
   )
   WHERE eh.created_by_id IS NULL;
   ```

## Notas Importantes

1. **Backup**: Antes de aplicar la migración en producción (Railway), considera hacer un backup:
   ```powershell
   # Docker
   docker exec senorarroz-postgres pg_dump -U postgres senor_arroz > backup-$(Get-Date -Format "yyyyMMdd-HHmmss").sql
   
   # Railway (usando psql)
   pg_dump "postgresql://postgres:...@centerbeam.proxy.rlwy.net:52635/railway" > backup-railway-$(Get-Date -Format "yyyyMMdd-HHmmss").sql
   ```

2. **Orden de ejecución**: Esta migración debe ejecutarse después de:
   - `20251122122758_InitialSchema`
   - `20251122123044_CreateDatabaseFunctionsAndTriggers`
   - `20251122123208_SeedInitialData`
   - `20251128005639_AddDeliverymanAdvanceTable` (opcional)

3. **Impacto**: Esta migración:
   - Agrega una columna NOT NULL, por lo que actualiza registros existentes
   - No elimina datos
   - Es segura de ejecutar en producción

4. **Rendimiento**: La actualización de registros existentes puede tardar si hay muchos `expense_header`. El script usa consultas eficientes con `LIMIT 1`.

## Verificación Post-Migración

Después de aplicar la migración, verifica que:

1. ✅ La columna `created_by_id` existe y es NOT NULL
2. ✅ El índice `idx_expense_header_created_by` existe
3. ✅ El Foreign Key `FK_expense_header_user_created_by_id` existe
4. ✅ Todos los registros existentes tienen `created_by_id` asignado
5. ✅ La migración está registrada en `__EFMigrationsHistory`
6. ✅ La aplicación puede crear nuevos `expense_header` con `created_by_id`

## Próximos Pasos

Después de aplicar esta migración:

1. Verifica que el backend pueda crear nuevos `expense_header` con `created_by_id`
2. Prueba los filtros de acceso basados en roles:
   - Cashier solo ve sus propios gastos
   - Admin ve todos los gastos de su sucursal
   - Superadmin ve todos los gastos
3. Verifica que los endpoints de la API funcionen correctamente

