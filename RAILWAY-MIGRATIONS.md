# Migración de Base de Datos en Railway

## Información del Proyecto

- **Proyecto**: señor arroz c# vue js
- **Project ID**: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- **Base de Datos**: railway
- **Script SQL**: `railway-initial-utf8.sql`

## Prerrequisitos

1. Tener Railway CLI instalado y configurado
2. Tener el proyecto vinculado a Railway
3. Tener acceso al servicio PostgreSQL en Railway

## Connection String

### Formato Railway (DATABASE_URL)
```
postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@postgres.railway.internal:5432/railway
```

### Formato .NET (para variables de entorno)
```
Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg
```

## Pasos para Ejecutar la Migración

### Paso 1: Conectarse a Railway PostgreSQL

Desde tu máquina local, en el directorio del proyecto:

```bash
# Asegúrate de estar en el directorio correcto
cd senorArrozAPI

# Conectarse a PostgreSQL usando Railway CLI
railway connect postgres
```

Esto abrirá una sesión interactiva de `psql` conectada a la base de datos Railway.

### Paso 2: Ejecutar el Script SQL

Una vez dentro de `psql`, ejecuta el script:

```sql
-- Desde dentro de psql
\i railway-initial-utf8.sql
```

**Alternativa: Ejecutar desde la línea de comandos**

Si prefieres ejecutar el script directamente sin entrar en `psql`:

```bash
# Desde el directorio senorArrozAPI
railway run --service MainDatabase psql -U postgres -d railway -f railway-initial-utf8.sql
```

O usando el connection string directamente:

```bash
railway run --service MainDatabase psql "postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@postgres.railway.internal:5432/railway" -f railway-initial-utf8.sql
```

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

## Características del Script

El script `railway-initial-utf8.sql` es **idempotente**, lo que significa que:

- Puede ejecutarse múltiples veces sin causar errores
- Usa `IF NOT EXISTS` y `ON CONFLICT DO NOTHING` para evitar duplicados
- Verifica el historial de migraciones antes de ejecutar cada sección
- Está codificado en UTF-8 sin caracteres especiales problemáticos

## Troubleshooting

### Error: "could not translate host name"

- Verifica que estés usando Railway CLI correctamente
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

2. **Host Interno**: Usamos `postgres.railway.internal` que solo funciona dentro de Railway, evitando cargos adicionales por host público.

3. **Seguridad**: No commitees connection strings con contraseñas en el repositorio. Usa variables de entorno o Railway Secrets.

4. **Idempotencia**: El script puede ejecutarse múltiples veces sin problemas gracias a las verificaciones de existencia.

5. **Encoding**: El archivo está en UTF-8 sin BOM y sin caracteres especiales problemáticos (ñ, acentos) para evitar errores de encoding.

## Estructura del Script

El script `railway-initial-utf8.sql` contiene:

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
