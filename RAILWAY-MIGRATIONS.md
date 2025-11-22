# Migraciones de Base de Datos en Railway

## Información del Proyecto

- **Proyecto**: señor arroz c# vue js
- **Project ID**: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- **Base de Datos**: railway

## Prerrequisitos

1. Tener `dotnet-ef` instalado globalmente:
   ```bash
   dotnet tool install --global dotnet-ef
   ```

2. Tener acceso al connection string de Railway PostgreSQL

3. Estar en el directorio del proyecto:
   ```bash
   cd senorArrozAPI
   ```

## Connection String para Migraciones

### Para conexiones desde tu máquina local

Necesitas el **host público** de Railway PostgreSQL. El connection string interno (`postgres.railway.internal`) no funciona desde fuera de Railway.

**Formato:**
```
Host=HOST_PUBLICO;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg
```

**Ejemplo (reemplaza HOST_PUBLICO con el host real):**
```
Host=containers-us-west-xxx.railway.app;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg
```

### Cómo obtener el host público

1. Ve a Railway Dashboard → Tu proyecto → Servicio PostgreSQL
2. Pestaña "Variables"
3. Busca `PGHOST` o busca en las variables públicas
4. O usa Railway CLI: `railway variables`

## Comando para Ejecutar Migraciones

### Comando Completo

```bash
dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API --connection "Host=HOST_PUBLICO;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg"
```

### Ejemplo con Host Público

```bash
dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API --connection "Host=containers-us-west-xxx.railway.app;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg"
```

## Orden de Migraciones

Las migraciones se ejecutarán en el siguiente orden:

1. **InitialSchema** (20251122122758_InitialSchema)
   - Crea toda la estructura de la base de datos (tablas, índices, foreign keys)

2. **CreateDatabaseFunctionsAndTriggers** (20251122123044_CreateDatabaseFunctionsAndTriggers)
   - Crea funciones y triggers de PostgreSQL

3. **SeedInitialData** (20251122123208_SeedInitialData)
   - Inserta datos iniciales (sucursal, usuarios, barrios, banco, app, clientes, productos)

## Verificar Migraciones Aplicadas

### Desde Railway CLI

```bash
railway connect postgres
```

Luego en psql:
```sql
SELECT "MigrationId", "ProductVersion" 
FROM "__EFMigrationsHistory" 
ORDER BY "MigrationId";
```

### Desde tu máquina local

```bash
psql -h HOST_PUBLICO -p 5432 -U postgres -d railway -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";"
```

Deberías ver:
- `20251122122758_InitialSchema`
- `20251122123044_CreateDatabaseFunctionsAndTriggers`
- `20251122123208_SeedInitialData`

## Verificar Datos Iniciales

### Verificar usuarios creados

```sql
SELECT name, email, role FROM "user" ORDER BY role, name;
```

Deberías ver:
- Santiago (superadmin)
- Daniel Alvarez (admin)
- Abelardo (deliveryman)
- Maikol Martinez Serna (deliveryman)
- juan (kitchen)

### Verificar barrios

```sql
SELECT name, delivery_fee FROM neighborhood ORDER BY name;
```

Deberías ver: Castilla, Florencia, Pedregal, Picacho, Santander

### Verificar productos

```sql
SELECT COUNT(*) as total_productos FROM product;
```

Deberías ver: 28 productos

## Troubleshooting

### Error: "could not translate host name"
- Verifica que estés usando el **host público**, no `postgres.railway.internal`
- El host interno solo funciona dentro de Railway

### Error: "password authentication failed"
- Verifica que la contraseña sea correcta
- Si cambiaste `PGPASSWORD` en Railway, usa el nuevo valor

### Error: "database does not exist"
- Verifica que el nombre de la base de datos sea `railway` (o el que configuraste)
- Railway usa `railway` por defecto, no `MainDatabase`

### Error: "relation already exists"
- Las tablas ya existen en la base de datos
- Verifica el historial de migraciones: `SELECT * FROM "__EFMigrationsHistory";`
- Si faltan migraciones, ejecuta el comando de nuevo

### Error: "dotnet-ef not found"
- Instala dotnet-ef: `dotnet tool install --global dotnet-ef`
- Verifica instalación: `dotnet ef --version`

## Notas Importantes

1. **Migraciones Idempotentes**: Las migraciones están diseñadas para ser idempotentes, puedes ejecutarlas múltiples veces sin problemas.

2. **No Automáticas**: Las migraciones NO se ejecutan automáticamente al iniciar la aplicación. Deben ejecutarse manualmente.

3. **Host Público Requerido**: Para ejecutar migraciones desde tu máquina local, necesitas el host público de Railway, no el interno.

4. **Seguridad**: No commitees connection strings con contraseñas en el repositorio. Usa variables de entorno o Railway Secrets.

