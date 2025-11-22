# 🐳 Guía de Dockerización - SenorArroz API

Esta guía te llevará paso a paso para dockerizar y ejecutar la aplicación SenorArroz API completa.

## 📋 Prerrequisitos

- ✅ Docker Desktop instalado y corriendo
- ✅ Git (opcional, si clonas el repositorio)
- ✅ Terminal/PowerShell

## 🚀 Paso a Paso

### Paso 1: Verificar Docker Desktop

Abre Docker Desktop y asegúrate de que esté corriendo. Verifica con:

```bash
docker --version
docker compose version
```

Deberías ver algo como:
```
Docker version 24.x.x
Docker Compose version v2.x.x
```

### Paso 2: Navegar al Directorio del Proyecto

Abre PowerShell o Terminal y navega a la raíz del proyecto:

```bash
cd C:\Users\LENOVO\source\repos\senorArrozAPI
```

### Paso 3: Revisar los Archivos Docker

Asegúrate de que existan estos archivos en la raíz del proyecto:

- ✅ `Dockerfile` - Para construir la imagen de la API
- ✅ `docker-compose.yml` - Para orquestar los servicios
- ✅ `.dockerignore` - Para optimizar el build

### Paso 4: Construir las Imágenes Docker

Construye las imágenes de Docker (esto puede tardar varios minutos la primera vez):

```bash
docker compose build
```

Este comando:
- Descarga la imagen base de .NET 9.0
- Descarga la imagen de PostgreSQL 16
- Compila tu aplicación .NET
- Crea las imágenes necesarias

### Paso 5: Iniciar los Contenedores

Inicia todos los servicios (API + PostgreSQL):

```bash
docker compose up -d
```

El flag `-d` ejecuta los contenedores en segundo plano (detached mode).

### Paso 6: Verificar que Todo Esté Corriendo

Verifica el estado de los contenedores:

```bash
docker compose ps
```

Deberías ver algo como:

```
NAME                  STATUS          PORTS
senorarroz-api        Up (healthy)    0.0.0.0:5000->8080/tcp, 0.0.0.0:5001->8081/tcp
senorarroz-postgres   Up (healthy)    0.0.0.0:5433->5432/tcp
```

### Paso 7: Ver los Logs

Para ver los logs de la aplicación en tiempo real:

```bash
# Ver todos los logs
docker compose logs -f

# Ver solo logs de la API
docker compose logs -f api

# Ver solo logs de PostgreSQL
docker compose logs -f postgres
```

### Paso 8: Aplicar Migraciones de Base de Datos

El proyecto utiliza **Entity Framework Core Migrations** para gestionar la estructura de la base de datos y los datos iniciales. Las migraciones se ejecutan **manualmente** mediante comando.

**Migraciones disponibles:**

1. **InitialSchema**: Crea toda la estructura de la base de datos (tablas, índices, foreign keys)
2. **CreateDatabaseFunctionsAndTriggers**: Crea funciones y triggers de PostgreSQL
3. **SeedInitialData**: Inserta datos iniciales (sucursal, usuarios, barrios, banco, app, clientes, productos)

**Ejecutar migraciones desde el contenedor:**

```bash
# Asegúrate de que el contenedor de la API esté corriendo
docker exec senorarroz-api dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API
```

**Ejecutar migraciones desde tu máquina local:**

Si tienes `dotnet ef` instalado localmente y la base de datos está accesible:

```bash
# Navegar al directorio del proyecto
cd senorArrozAPI

# Ejecutar migraciones apuntando a la base de datos en Docker
dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API --connection "Host=localhost;Port=5433;Database=senor_arroz;Username=postgres;Password=1234"
```

**Datos iniciales incluidos:**

La migración `SeedInitialData` incluye:
- Sucursal "Santander" con dirección "calle 108a # 77d-30"
- Usuarios: Santiago (superadmin), Daniel Alvarez (admin), Abelardo y Maikol (deliverymen), Juan (kitchen)
- Barrios del norte de Medellín: Castilla, Santander, Pedregal, Florencia, Picacho
- Banco: Bancolombia
- App: Didi (atada a Bancolombia)
- 8 clientes con direcciones y coordenadas
- Categorías y productos completos

### Paso 10: Probar la API

Abre tu navegador y visita:

- **Swagger UI**: http://localhost:5000
- **API Health Check**: http://localhost:5000/swagger/index.html

O prueba con curl/PowerShell:

```powershell
# Health check
Invoke-WebRequest -Uri http://localhost:5000/swagger/index.html

# O con curl
curl http://localhost:5000/swagger/index.html
```

## 🔧 Comandos Útiles

### Detener los Contenedores

```bash
docker compose down
```

### Detener y Eliminar Volúmenes (⚠️ Esto borra la base de datos)

```bash
docker compose down -v
```

### Reiniciar los Servicios

```bash
docker compose restart
```

### Reconstruir las Imágenes (después de cambios en el código)

```bash
docker compose up -d --build
```

### Ver el Uso de Recursos

```bash
docker stats
```

### Entrar al Contenedor de la API

```bash
docker exec -it senorarroz-api bash
```

### Entrar a PostgreSQL

```bash
docker exec -it senorarroz-postgres psql -U postgres -d senor_arroz
```

### Ver Logs de un Servicio Específico

```bash
docker compose logs -f api
docker compose logs -f postgres
```

## 🗄️ Gestión de Base de Datos

### Aplicar Migraciones

El proyecto utiliza Entity Framework Core Migrations para gestionar la base de datos. Las migraciones se ejecutan manualmente:

```bash
# Desde el contenedor
docker exec senorarroz-api dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API

# Desde tu máquina local
dotnet ef database update --project SenorArroz.Infrastructure --startup-project SenorArroz.API --connection "Host=localhost;Port=5433;Database=senor_arroz;Username=postgres;Password=1234"
```

### Crear Nueva Migración

Si necesitas crear una nueva migración después de modificar las entidades:

```bash
# Desde tu máquina local
dotnet ef migrations add NombreMigracion --project SenorArroz.Infrastructure --startup-project SenorArroz.API
```

### Ver Estado de Migraciones

Para ver qué migraciones están aplicadas:

```bash
# Desde el contenedor
docker exec senorarroz-api dotnet ef migrations list --project SenorArroz.Infrastructure --startup-project SenorArroz.API
```

### Backup de la Base de Datos

```bash
docker exec senorarroz-postgres pg_dump -U postgres senor_arroz > backup.sql
```

### Restaurar Base de Datos desde Backup

```bash
docker exec -i senorarroz-postgres psql -U postgres senor_arroz < backup.sql
```

### Conectar con Herramientas Externas

Puedes conectar herramientas como pgAdmin, DBeaver, o DataGrip usando:

- **Host**: `localhost`
- **Port**: `5433` (puerto externo, el interno del contenedor es 5432)
- **Database**: `senor_arroz`
- **Username**: `postgres`
- **Password**: `1234`

## 🔐 Variables de Entorno

Las variables de entorno están configuradas en `docker-compose.yml`. Para cambiar valores:

1. Edita `docker-compose.yml` directamente, o
2. Crea un archivo `.env` en la raíz del proyecto:

```env
POSTGRES_PASSWORD=tu_password_seguro
JWT_SECRET_KEY=tu_secret_key_muy_largo
```

Luego referencia las variables en `docker-compose.yml`:

```yaml
environment:
  - POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
```

## 🐛 Solución de Problemas

### Error: "Port already in use"

Si el puerto 5000 o 5433 ya está en uso:

1. Cambia los puertos en `docker-compose.yml` (el nombre del archivo se mantiene):
```yaml
ports:
  - "5001:8080"  # Cambia 5000 por otro puerto
```

2. O detén el servicio que está usando el puerto.

### Error: "Cannot connect to database"

1. Verifica que PostgreSQL esté corriendo:
```bash
docker compose ps
```

2. Verifica los logs:
```bash
docker compose logs postgres
```

3. Asegúrate de que el healthcheck de PostgreSQL haya pasado antes de iniciar la API.

### La API no inicia

1. Verifica los logs:
```bash
docker compose logs api
```

2. Verifica que la conexión a la base de datos sea correcta en las variables de entorno.

3. Reconstruye la imagen:
```bash
docker compose up -d --build --force-recreate
```

### Limpiar Todo y Empezar de Nuevo

```bash
# Detener y eliminar contenedores
docker compose down -v

# Eliminar imágenes
docker rmi senorarrozapi-api

# Limpiar sistema Docker (opcional, elimina todo lo no usado)
docker system prune -a --volumes
```

## 📊 Monitoreo

### Ver Uso de Recursos en Tiempo Real

```bash
docker stats
```

### Inspeccionar un Contenedor

```bash
docker inspect senorarroz-api
```

## 🚀 Producción

Para producción, considera:

1. **Cambiar contraseñas**: Usa contraseñas seguras en variables de entorno
2. **HTTPS**: Configura certificados SSL
3. **Secrets Management**: Usa Docker Secrets o un gestor de secretos
4. **Logging**: Configura logging centralizado
5. **Monitoring**: Agrega herramientas de monitoreo
6. **Backup**: Configura backups automáticos de la base de datos

## 📝 Notas Importantes

- Los datos de PostgreSQL se persisten en un volumen Docker llamado `postgres_data`
- Si eliminas el volumen (`docker compose down -v`), perderás todos los datos
- La API expone el puerto 5000 en tu máquina, mapeado al puerto 8080 dentro del contenedor
- PostgreSQL expone el puerto 5433 en tu máquina (mapeado al puerto 5432 interno del contenedor)
- Los healthchecks aseguran que los servicios estén listos antes de que otros dependan de ellos

## 🎉 ¡Listo!

Tu aplicación debería estar corriendo en:
- **API**: http://localhost:5000
- **Swagger**: http://localhost:5000/swagger
- **PostgreSQL**: localhost:5433

¡Disfruta de tu aplicación dockerizada! 🚀

