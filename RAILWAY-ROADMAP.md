# Mapa de Ruta - Despliegue en Railway

## Objetivo
Desplegar la aplicación SenorArroz completa (base de datos, backend y frontend) en Railway con configuración de producción, variables de entorno y migraciones.

## Estructura de Servicios en Railway

1. **PostgreSQL Service**: Base de datos usando Railway PostgreSQL ✅
2. **Backend Service**: API ASP.NET Core 9.0 (En progreso)
3. **Frontend Service**: Aplicación Vue.js con Nginx

## Tareas de Implementación

### 1. Configuración de Base de Datos (PostgreSQL) ✅ COMPLETADO

**Estado:** Completado

**Información del Proyecto:**
- Nombre del Proyecto: señor arroz c# vue js
- Project ID: 5cb08ee8-0129-4d5b-aba8-60b34cfeee58
- Base de Datos: railway

**Connection String:**
- Formato Railway: `postgresql://postgres:ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg@postgres.railway.internal:5432/railway`
- Formato .NET: `Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg`

**Documentación creada:**
- `RAILWAY-CONNECTION.md` - Guía de conexión
- `RAILWAY-MIGRATIONS.md` - Guía de migraciones

### 2. Configuración del Backend (EN PROGRESO)

**Archivos a crear/modificar:**
- `senorArrozAPI/railway.json` - Configuración de Railway para el backend (opcional)
- Verificar que `Dockerfile` esté listo para Railway

**Pasos a seguir:**

1. **Crear servicio Backend en Railway:**
   - En Railway Dashboard, proyecto "señor arroz c# vue js"
   - "New" → "GitHub Repo" o "Empty Service"
   - Si usas GitHub: conectar repositorio y seleccionar carpeta `senorArrozAPI`
   - Si usas Empty Service: subir código manualmente

2. **Configurar Build Settings:**
   - Root Directory: `senorArrozAPI` (si el repo es la raíz) o `.` (si ya estás en la carpeta)
   - Build Command: Railway detectará automáticamente el Dockerfile
   - Start Command: Se ejecutará automáticamente desde el Dockerfile

3. **Configurar Variables de Entorno:**
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `ASPNETCORE_URLS=http://+:8080` (Railway asignará puerto automáticamente)
   - `ConnectionStrings__DefaultConnection=Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=ZkDOPtBUOrPPvmFgFQeCqoLZnfsBzZRg`
   - `JwtSettings__SecretKey=IsmaelHermoso2023andPaolaHermosaEsposa2024andSantiagoPapasitoTodoeltiempo`
   - `JwtSettings__ExpiryInHours=24`
   - `JwtSettings__AccessTokenExpirationMinutes=480`
   - `JwtSettings__Issuer=SenorArroz.API`
   - `JwtSettings__Audience=SenorArroz.Client`
   - `JwtSettings__RefreshTokenExpirationDays=7`
   - `EmailSettings__SmtpHost=smtp.gmail.com`
   - `EmailSettings__SmtpPort=587`
   - `EmailSettings__SmtpUsername=arrozcastilla@gmail.com`
   - `EmailSettings__SmtpPassword=nifg ksbt cdbn mpcc`
   - `EmailSettings__FromEmail=arrozcastilla@gmail.com`
   - `EmailSettings__FromName=SenorArroz`
   - `EmailSettings__EnableSsl=true`
   - `FrontendSettings__ResetPasswordUrl=https://TU-FRONTEND-URL.railway.app/reset-password` (actualizar después de crear frontend)

4. **Verificar Despliegue:**
   - Esperar a que el build termine
   - Verificar que el servicio esté "Active"
   - Obtener URL pública del backend (ej: `https://senorarroz-api.railway.app`)

**Configuración de Railway:**
- Usar Dockerfile existente (`senorArrozAPI/Dockerfile`)
- Railway detectará automáticamente el Dockerfile y lo usará
- Health check ya está configurado en el Dockerfile

### 3. Configuración del Frontend

**Archivos a crear/modificar:**
- `senorArrozFront/railway.json` - Configuración de Railway para el frontend (opcional)
- Verificar que `Dockerfile` esté listo para Railway

**Variables de entorno a configurar en Railway (build args):**
- `VITE_API_URL` - URL del backend en Railway (ej: `https://senorarroz-api.railway.app/api`)
- `VITE_SIGNALR_HUB_URL` - URL del SignalR hub (ej: `https://senorarroz-api.railway.app/hubs/orders`)
- `VITE_GOOGLE_MAPS_API_KEY` - API key de Google Maps
- `VITE_GOOGLE_MAPS_MAP_ID` - Map ID de Google Maps (opcional)

**Configuración de Railway:**
- Usar Dockerfile existente (`senorArrozFront/Dockerfile`)
- Configurar build args para variables de entorno de Vite
- Health check ya está configurado en el Dockerfile

### 4. Migración de Base de Datos

**IMPORTANTE:** La migración NO se ejecuta automáticamente. Se ejecuta manualmente usando el script SQL `railway-initial-utf8.sql` directamente en Railway PostgreSQL.

**Proceso de Ejecución de la Migración:**

1. **Conectarse a Railway PostgreSQL:**

   ```bash
   # Desde el directorio senorArrozAPI
   cd senorArrozAPI
   railway connect postgres
   ```

   Esto abrirá una sesión interactiva de `psql`.

2. **Ejecutar el script SQL:**

   ```sql
   -- Desde dentro de psql
   \i railway-initial-utf8.sql
   ```

   **Alternativa: Ejecutar desde la línea de comandos:**

   ```bash
   railway run --service MainDatabase psql -U postgres -d railway -f railway-initial-utf8.sql
   ```

3. **Verificar que la migración se aplicó:**

   ```sql
   SELECT "MigrationId", "ProductVersion" 
   FROM "__EFMigrationsHistory" 
   ORDER BY "MigrationId";
   ```

   Deberías ver:
   - `20251122122758_InitialSchema`
   - `20251122123044_CreateDatabaseFunctionsAndTriggers`
   - `20251122123208_SeedInitialData`

4. **Verificar datos iniciales:**
   - Usuarios creados (Santiago, Daniel, Abelardo, Maikol, Juan)
   - Barrios creados (Castilla, Santander, Pedregal, Florencia, Picacho)
   - Productos creados (28 productos)
   - Banco y App creados (Bancolombia, Didi)
   - 8 clientes con direcciones y coordenadas

**Características del Script:**
- **Idempotente**: Puede ejecutarse múltiples veces sin causar errores
- **Completo**: Incluye estructura de tablas, funciones, triggers y datos iniciales
- **UTF-8 limpio**: Sin caracteres problemáticos que causen errores de encoding

**Ventajas de usar script SQL directo:**
- No requiere host público (evita cargos adicionales)
- Usa el host interno (`postgres.railway.internal`)
- No necesita `dotnet-ef` ni herramientas adicionales
- Ejecución simple y directa con `psql`

**Documentación:**
- Ver `RAILWAY-MIGRATIONS.md` para detalles completos y troubleshooting

### 5. Verificación de Conexiones

**Después de desplegar Backend y ejecutar migraciones:**

1. **Verificar conexión Backend → PostgreSQL:**
   - Revisar logs del servicio Backend en Railway
   - Buscar mensajes de conexión exitosa a la base de datos
   - Verificar que no haya errores de conexión

2. **Verificar migraciones aplicadas:**
   ```bash
   railway connect postgres
   # Dentro de psql:
   SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
   ```

3. **Probar endpoints de la API:**
   - Acceder a Swagger: `https://TU-BACKEND-URL.railway.app/swagger`
   - Probar endpoint de health check
   - Probar endpoint de login con usuario de prueba

4. **Verificar datos en la base de datos:**
   ```bash
   railway connect postgres
   # Dentro de psql:
   SELECT COUNT(*) FROM "user";
   SELECT COUNT(*) FROM neighborhood;
   SELECT COUNT(*) FROM product;
   ```

### 6. Configuración de Dominios y URLs

**Acciones:**
- Obtener URLs públicas de Railway para backend y frontend
- Actualizar `FrontendSettings__ResetPasswordUrl` con URL del frontend
- Actualizar `VITE_API_URL` y `VITE_SIGNALR_HUB_URL` con URLs del backend

### 7. Configuración de CORS

**Archivos a modificar:**
- `senorArrozAPI/SenorArroz.API/Program.cs` - Actualizar configuración de CORS para permitir el dominio del frontend en Railway

### 8. Documentación

**Archivos a crear/modificar:**
- `senorArrozAPI/RAILWAY-DEPLOY.md` - Guía de despliegue en Railway
- Actualizar `README-DOCKER.md` con referencia a Railway
- Documentar proceso de migraciones en producción

## Consideraciones Importantes

1. **Migración de Base de Datos**: Se ejecuta manualmente usando el script SQL `railway-initial-utf8.sql` directamente en Railway PostgreSQL usando Railway CLI. NO se ejecuta automáticamente al iniciar la aplicación.

2. **Variables de Entorno**: Todas las configuraciones sensibles deben estar en variables de entorno de Railway, no en archivos de configuración.

3. **SignalR**: Railway soporta WebSockets para SignalR por defecto.

4. **Health Checks**: Los Dockerfiles ya incluyen health checks, Railway los usará automáticamente.

5. **Build Context**: Railway necesita acceso al repositorio Git o a los archivos del proyecto.

6. **Secrets**: Usar Railway Secrets para variables sensibles como JWT secret y contraseñas.

7. **Host Público**: No es necesario habilitar host público para PostgreSQL. Las migraciones se ejecutan desde dentro de Railway.

## Orden de Ejecución

1. ✅ Crear servicio PostgreSQL en Railway
2. ✅ Obtener connection string de PostgreSQL
3. ✅ Documentar conexión y migraciones
4. 🔄 **Crear servicio Backend en Railway** (EN PROGRESO)
5. 🔄 **Configurar variables de entorno del backend**
6. 🔄 **Desplegar backend y verificar que esté corriendo**
7. 🔄 **Ejecutar script SQL `railway-initial-utf8.sql` usando Railway CLI para conectar a PostgreSQL**
8. 🔄 **Verificar conexiones y datos iniciales**
9. Crear servicio Frontend en Railway
10. Configurar variables de entorno del frontend (build args)
11. Actualizar URLs en variables de entorno del backend
12. Configurar CORS
13. Verificar que todos los servicios estén corriendo
14. Probar endpoints y funcionalidad completa
15. Configurar dominios personalizados (opcional)
