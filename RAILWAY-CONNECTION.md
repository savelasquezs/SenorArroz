# Conexión al servicio PostgreSQL `MainDatabase` en Railway

## Información del Proyecto

- **Plataforma**: Railway
- **Nombre del Proyecto**: señor arroz c# vue js
- **Project ID**: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- **Servicio PostgreSQL**: `MainDatabase`
- **Nombre interno de la base**: usar el valor vigente de `PGDATABASE`; no inferirlo del nombre de la plataforma ni del servicio.

## Connection String

La conexión del backend se configura con los valores vigentes del servicio `MainDatabase`. No se deben copiar credenciales ni connection strings reales en este repositorio.

- Railway: usar la referencia o variable `DATABASE_URL` suministrada por `MainDatabase`.
- .NET: configurar `ConnectionStrings__DefaultConnection` con `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` y `PGPASSWORD`.

## Variables de PostgreSQL en Railway

Consultar los valores vigentes en el servicio `MainDatabase`:

- `PGPASSWORD`
- `PGUSER`
- `PGDATABASE`
- `PGHOST`
- `PGPORT`

## Nota Importante: Host Interno vs Público

El connection string proporcionado usa `postgres.railway.internal`, que es el **host interno** de Railway. Este host solo funciona para conexiones **dentro de Railway** (entre servicios).

### Para conexiones internas (Backend → PostgreSQL)
Usar: `postgres.railway.internal`

### Para ejecutar scripts desde tu máquina local

Usar Bash y `railway connect MainDatabase`; Railway CLI resuelve la conexión al servicio sin que el operador tenga que copiar una URL.

## Cómo Obtener el Connection String

1. Accede a Railway Dashboard: https://railway.app
2. Selecciona el proyecto: "señor arroz c# vue js"
3. Haz clic en el servicio PostgreSQL
4. Ve a la pestaña "Variables"
5. Busca la variable `DATABASE_URL` suministrada por `MainDatabase`
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
postgresql://<usuario>:<contraseña>@<host>:<puerto>/<base>
```

**Salida:**
```
Host=<host>;Port=<puerto>;Database=<base>;Username=<usuario>;Password=<contraseña>
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

# Desde Bash, conectar al servicio PostgreSQL
railway connect MainDatabase
```

Esta es la conexión obligatoria para ejecutar scripts del repositorio en Railway. Una vez dentro de `psql`, usar `\i` con la ruta del script:

```sql
\i SenorArroz.Infrastructure/Scripts/<script>.sql
\q
```

### Usando psql directamente desde una terminal local

Puede utilizarse para diagnóstico manual con credenciales vigentes obtenidas desde Railway, pero no es el procedimiento aprobado para ejecutar scripts. Para scripts, usar Bash y `railway connect MainDatabase`.

### Usando pgAdmin o DBeaver

- **Host**: Host público de Railway (no `postgres.railway.internal`)
- **Port**: valor de `PGPORT`
- **Database**: valor de `PGDATABASE`
- **Username**: valor de `PGUSER`
- **Password**: valor de `PGPASSWORD`

## Seguridad

⚠️ **IMPORTANTE**: 
- No commitees connection strings en el repositorio
- Usa Railway Secrets para almacenar valores sensibles
- El connection string puede regenerarse automáticamente por Railway
- Si cambias `PGPASSWORD` o `PGUSER`, actualiza todas las referencias

## Troubleshooting

### Error: "could not connect to server" o "could not translate host name"

- **Solución para scripts:** usa Bash y `railway connect MainDatabase`.
- El host interno solo funciona entre servicios dentro de Railway.
- Verifica que el servicio PostgreSQL esté activo en Railway
- Verifica las credenciales

### Error: "database does not exist"
- Verifica el nombre de la base de datos en `PGDATABASE`
- `MainDatabase` es el nombre del servicio usado por `railway connect`; no es necesariamente el valor de `PGDATABASE`.
- Railway es la plataforma, no el nombre de la base de datos.

