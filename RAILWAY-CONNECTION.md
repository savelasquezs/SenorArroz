# Conexión a Railway PostgreSQL

## Información del Proyecto

- **Nombre del Proyecto**: señor arroz c# vue js
- **Project ID**: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- **Base de Datos**: railway (nombre por defecto de Railway)
- **Servicio**: PostgreSQL

## Connection String

### Formato Railway (DATABASE_URL)
```
postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@postgres.railway.internal:5432/railway
```

### Formato .NET (para variables de entorno)
```
Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg
```

## Variables de PostgreSQL en Railway

Las siguientes variables están disponibles en Railway y pueden modificarse:

- **PGPASSWORD**: `ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg` (puede modificarse)
- **PGUSER**: `postgres` (puede modificarse)
- **PGDATABASE**: `railway` (puede modificarse)
- **PGHOST**: `postgres.railway.internal` (generado por Railway, no modificar)
- **PGPORT**: `5432` (puerto estándar, no modificar)

## Nota Importante: Host Interno vs Público

El connection string proporcionado usa `postgres.railway.internal`, que es el **host interno** de Railway. Este host solo funciona para conexiones **dentro de Railway** (entre servicios).

### Para conexiones internas (Backend → PostgreSQL)
Usar: `postgres.railway.internal`

### Para conexiones externas (Migraciones desde tu máquina local)
Necesitas el **host público** de Railway. Para obtenerlo:

1. Ve al servicio PostgreSQL en Railway Dashboard
2. Pestaña "Variables"
3. Busca `PGHOST` o `PUBLIC_URL` (si está disponible)
4. O usa Railway CLI: `railway variables`

El host público generalmente tiene el formato: `containers-us-west-xxx.railway.app` o similar.

## Cómo Obtener el Connection String

1. Accede a Railway Dashboard: https://railway.app
2. Selecciona el proyecto: "señor arroz c# vue js"
3. Haz clic en el servicio PostgreSQL
4. Ve a la pestaña "Variables"
5. Busca la variable `DATABASE_URL`
6. Copia el valor completo

## Conversión de Formato

### De PostgreSQL URL a .NET Connection String

**Formato PostgreSQL URL:**
```
postgresql://username:password@host:port/database
```

**Formato .NET:**
```
Host=host;Port=port;Database=database;Username=username;Password=password
```

### Ejemplo de Conversión

**Entrada:**
```
postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@postgres.railway.internal:5432/railway
```

**Salida:**
```
Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg
```

## Conexión desde Herramientas Externas

### Usando Railway CLI

```bash
# Instalar Railway CLI (si no está instalado)
npm i -g @railway/cli

# Login
railway login

# Seleccionar proyecto
railway link

# Conectar a PostgreSQL
railway connect postgres
```

### Usando psql (desde terminal local)

Necesitas el **host público** de Railway:

```bash
psql -h HOST_PUBLICO -p 5432 -U postgres -d railway
```

### Usando pgAdmin o DBeaver

- **Host**: Host público de Railway (no `postgres.railway.internal`)
- **Port**: `5432`
- **Database**: `railway`
- **Username**: `postgres`
- **Password**: `ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg`

## Seguridad

⚠️ **IMPORTANTE**: 
- No commitees connection strings en el repositorio
- Usa Railway Secrets para almacenar valores sensibles
- El connection string puede regenerarse automáticamente por Railway
- Si cambias `PGPASSWORD` o `PGUSER`, actualiza todas las referencias

## Troubleshooting

### Error: "could not connect to server"
- Verifica que estés usando el host correcto (interno vs público)
- Verifica que el servicio PostgreSQL esté activo en Railway
- Verifica las credenciales

### Error: "database does not exist"
- Verifica el nombre de la base de datos en `PGDATABASE`
- Railway usa `railway` por defecto, no `MainDatabase`

