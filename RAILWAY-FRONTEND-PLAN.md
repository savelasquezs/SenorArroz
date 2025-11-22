# Plan de Despliegue del Frontend en Railway

## Respuesta a la Pregunta sobre Hosts Privados

**¿Se pueden hacer peticiones desde el host privado (senorarroz.railway.internal)?**

**Respuesta: NO para el frontend.**

**Explicación:**
- El frontend se ejecuta en el **navegador del usuario**, no en Railway
- Los navegadores solo pueden hacer peticiones a URLs públicas (HTTPS/HTTP)
- Los hosts internos como `senorarroz.railway.internal` solo funcionan para comunicación entre servicios backend dentro de Railway
- Por lo tanto, el frontend **DEBE usar la URL pública** del backend (ej: `https://senorarroz-api.railway.app`)

**Comunicación interna en Railway:**
- Los servicios backend SÍ pueden comunicarse entre sí usando hosts internos
- Por ejemplo: Backend → PostgreSQL usa `postgres.railway.internal`
- Pero Frontend (navegador) → Backend debe usar URL pública

## Especificaciones del Backend

### URL del Backend
- **URL Pública**: `https://senorarrozapi.up.railway.app`
- **URL Interna**: `senorarroz.railway.internal` (solo para servicios backend dentro de Railway)

### Endpoints Importantes
- **API Base**: `https://senorarrozapi.up.railway.app/api`
- **SignalR Hub**: `https://senorarrozapi.up.railway.app/hubs/orders`
- **Swagger**: `https://senorarrozapi.up.railway.app/swagger`

### Variables de Entorno del Backend (para referencia)
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://+:8080`
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

## Plan de Despliegue del Frontend

### Paso 1: Preparar el Dockerfile del Frontend

**Archivo**: `senorArrozFront/Dockerfile`

**Estado actual:**
- ✅ Ya incluye `VITE_API_URL` como build arg
- ✅ Ya incluye `VITE_SIGNALR_HUB_URL` como build arg (actualizado)
- ✅ Ya incluye `VITE_GOOGLE_MAPS_API_KEY` como build arg
- ✅ Ya incluye `VITE_GOOGLE_MAPS_MAP_ID` como build arg

**Variables de entorno requeridas en build time:**
- `VITE_API_URL` - URL pública del backend API: `https://senorarrozapi.up.railway.app/api`
- `VITE_SIGNALR_HUB_URL` - URL pública del SignalR hub: `https://senorarrozapi.up.railway.app/hubs/orders`
- `VITE_GOOGLE_MAPS_API_KEY` - API key de Google Maps: `AIzaSyCDIdNFxyStE1Kmeg1TCQEbw6f8NfHiOcI`
- `VITE_GOOGLE_MAPS_MAP_ID` - Map ID de Google Maps: `bd195b095873dfbd928a393f`

**Variables opcionales (usadas en el código pero no en Dockerfile actual):**
- `VITE_APP_NAME` - Nombre de la aplicación: `Sistema Señor Arroz` (usado en algunos componentes)
- `VITE_LOGO_URL` - URL del logo: vacío (usado en `Login.vue`)
- `VITE_WHATSAPP_API_URL` - URL de API de WhatsApp: vacío (definido en `.env.production`)

**Nota**: Si necesitas usar `VITE_APP_NAME`, `VITE_LOGO_URL` o `VITE_WHATSAPP_API_URL` en producción, deberás agregarlos al Dockerfile como build args.

### Paso 2: Actualizar Dockerfile del Frontend

**Modificaciones necesarias:**
- Agregar `ARG VITE_SIGNALR_HUB_URL` en el Dockerfile
- Agregar `ENV VITE_SIGNALR_HUB_URL=$VITE_SIGNALR_HUB_URL` en el Dockerfile

### Paso 3: Crear Servicio Frontend en Railway

**Pasos en Railway Dashboard:**

1. **Crear nuevo servicio:**
   - En Railway Dashboard, proyecto "señor arroz c# vue js"
   - "New" → "GitHub Repo" o "Empty Service"
   - Si usas GitHub: conectar repositorio y seleccionar carpeta `senorArrozFront`
   - Si usas Empty Service: subir código manualmente

2. **Configurar Build Settings:**
   - Root Directory: `senorArrozFront` (si el repo es la raíz) o `.` (si ya estás en la carpeta)
   - Build Command: Railway detectará automáticamente el Dockerfile
   - Start Command: Se ejecutará automáticamente desde el Dockerfile

3. **Configurar Build Arguments (Variables de Entorno de Build):**
   - `VITE_API_URL=https://senorarrozapi.up.railway.app/api`
   - `VITE_SIGNALR_HUB_URL=https://senorarrozapi.up.railway.app/hubs/orders`
   - `VITE_GOOGLE_MAPS_API_KEY=AIzaSyCDIdNFxyStE1Kmeg1TCQEbw6f8NfHiOcI`
   - `VITE_GOOGLE_MAPS_MAP_ID=bd195b095873dfbd928a393f`
   - `VITE_APP_NAME=Sistema Señor Arroz` (si el Dockerfile lo soporta)
   - `VITE_LOGO_URL=` (vacío, configurar si se necesita)
   - `VITE_WHATSAPP_API_URL=` (vacío, configurar si se necesita)

**Nota importante**: Estas variables se inyectan en tiempo de build, no en runtime. Vite las reemplaza en el código durante la compilación.

### Paso 4: Obtener URL del Backend

**Antes de configurar el frontend, necesitas:**
1. Verificar que el backend esté desplegado y activo
2. Obtener la URL pública del backend desde Railway Dashboard
3. Verificar que el backend responda en:
   - `https://TU-BACKEND-URL.railway.app/swagger`
   - `https://TU-BACKEND-URL.railway.app/api` (debe retornar 404 o similar, pero no error de conexión)

### Paso 5: Configurar CORS en el Backend

**Archivo a modificar**: `senorArrozAPI/SenorArroz.API/Program.cs`

**Verificar/Actualizar configuración de CORS:**
- Debe permitir el origen del frontend: `https://TU-FRONTEND-URL.railway.app`
- Debe permitir métodos: GET, POST, PUT, DELETE, PATCH, OPTIONS
- Debe permitir headers: Authorization, Content-Type
- Debe permitir credentials: true (si se usan cookies)

**Ejemplo de configuración CORS:**
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("https://TU-FRONTEND-URL.railway.app")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// En el pipeline:
app.UseCors("AllowFrontend");
```

**Nota**: Una vez que tengas la URL del frontend, actualiza `WithOrigins` con la URL real.

### Paso 6: Actualizar Variables de Entorno del Backend

**Después de obtener la URL del frontend:**
- Actualizar `FrontendSettings__ResetPasswordUrl` en Railway con la URL real del frontend
- Ejemplo: `FrontendSettings__ResetPasswordUrl=https://senorarroz-frontend.railway.app/reset-password`

### Paso 7: Verificar Despliegue del Frontend

**Pasos de verificación:**
1. Esperar a que el build termine
2. Verificar que el servicio esté "Active"
3. Obtener URL pública del frontend (ej: `https://senorarroz-frontend.railway.app`)
4. Abrir la URL en el navegador
5. Verificar que la aplicación cargue correctamente
6. Verificar que las peticiones al backend funcionen (abrir DevTools → Network)
7. Verificar que SignalR se conecte correctamente

### Paso 8: Probar Funcionalidad Completa

**Pruebas a realizar:**
1. **Login**: Probar login con usuario de prueba
2. **API Calls**: Verificar que las peticiones al backend funcionen
3. **SignalR**: Probar conexión SignalR (KitchenView, DeliveryView)
4. **Google Maps**: Verificar que los mapas se carguen correctamente
5. **Navegación**: Probar todas las rutas principales

## Archivos a Modificar/Crear

### Archivos a Modificar:
1. `senorArrozFront/Dockerfile` - Agregar `VITE_SIGNALR_HUB_URL` como build arg
2. `senorArrozAPI/SenorArroz.API/Program.cs` - Actualizar CORS con URL del frontend

### Archivos a Crear (Opcional):
1. `senorArrozFront/railway.json` - Configuración de Railway (opcional)

## Variables de Entorno del Frontend (Build Args)

**Formato JSON para copiar en Railway:**

```json
{
  "VITE_API_URL": "https://senorarrozapi.up.railway.app/api",
  "VITE_SIGNALR_HUB_URL": "https://senorarrozapi.up.railway.app/hubs/orders",
  "VITE_GOOGLE_MAPS_API_KEY": "AIzaSyCDIdNFxyStE1Kmeg1TCQEbw6f8NfHiOcI",
  "VITE_GOOGLE_MAPS_MAP_ID": "bd195b095873dfbd928a393f",
  "VITE_APP_NAME": "Sistema Señor Arroz",
  "VITE_LOGO_URL": "",
  "VITE_WHATSAPP_API_URL": ""
}
```

**Valores actualizados:**
- **Backend URL**: `https://senorarrozapi.up.railway.app` (URL real del backend en Railway)
- **Google Maps API Key**: `AIzaSyCDIdNFxyStE1Kmeg1TCQEbw6f8NfHiOcI` (valor de `.env` y `.env.development`)
- **Google Maps Map ID**: `bd195b095873dfbd928a393f` (valor de `.env` y `.env.development`)
- **App Name**: `Sistema Señor Arroz` (valor de `.env.development` y `.env.production`)
- **Logo URL**: Vacío (puede configurarse después si se necesita)
- **WhatsApp API URL**: Vacío (puede configurarse después si se necesita)

**Nota importante**: 
- `VITE_LOGO_URL` y `VITE_WHATSAPP_API_URL` están vacíos en los archivos `.env`. Si necesitas configurarlos, actualiza estos valores en Railway.
- El Dockerfile actual solo incluye `VITE_API_URL`, `VITE_SIGNALR_HUB_URL`, `VITE_GOOGLE_MAPS_API_KEY` y `VITE_GOOGLE_MAPS_MAP_ID`. Si necesitas `VITE_APP_NAME`, `VITE_LOGO_URL` o `VITE_WHATSAPP_API_URL`, deberás agregarlos al Dockerfile primero.

## Orden de Ejecución

1. ✅ Backend desplegado y funcionando (`https://senorarrozapi.up.railway.app`)
2. ✅ Obtener URL pública del backend
3. ✅ Dockerfile del frontend actualizado (incluye `VITE_SIGNALR_HUB_URL`)
4. 🔄 Crear servicio Frontend en Railway
5. 🔄 Configurar build args con valores reales (ver JSON arriba)
6. 🔄 Desplegar frontend
7. 🔄 Obtener URL pública del frontend
8. 🔄 Actualizar CORS en el backend con URL del frontend
9. 🔄 Actualizar `FrontendSettings__ResetPasswordUrl` en el backend con URL del frontend
10. 🔄 Verificar que todo funcione correctamente

## Consideraciones Importantes

1. **URLs Públicas Requeridas**: El frontend DEBE usar URLs públicas del backend, no hosts internos
2. **Build Time vs Runtime**: Las variables de Vite se inyectan en build time, no en runtime
3. **CORS**: Debe configurarse correctamente en el backend para permitir peticiones del frontend
4. **HTTPS**: Railway proporciona HTTPS automáticamente para todos los servicios
5. **Health Checks**: El Dockerfile ya incluye health check, Railway lo usará automáticamente
6. **Nginx**: El frontend usa Nginx para servir archivos estáticos en producción
7. **SPA Routing**: Nginx debe estar configurado para manejar rutas del SPA (redirect a index.html)

## Troubleshooting

### Error: "Failed to fetch" o CORS error
- Verificar que CORS esté configurado correctamente en el backend
- Verificar que la URL del frontend en CORS coincida exactamente con la URL pública
- Verificar que el backend esté accesible públicamente

### Error: SignalR no se conecta
- Verificar que `VITE_SIGNALR_HUB_URL` esté configurado correctamente
- Verificar que el backend tenga SignalR habilitado
- Verificar que la URL del SignalR hub sea correcta

### Error: Google Maps no carga
- Verificar que `VITE_GOOGLE_MAPS_API_KEY` esté configurado
- Verificar que la API key tenga los permisos correctos
- Verificar que el dominio esté autorizado en Google Cloud Console

### Error: 404 en rutas del SPA
- Verificar que `nginx.conf` tenga la configuración correcta para SPA routing
- Verificar que todas las rutas redirijan a `index.html`

## Documentación Relacionada

- `RAILWAY-ROADMAP.md` - Roadmap general de despliegue
- `RAILWAY-MIGRATIONS.md` - Guía de migraciones de base de datos
- `RAILWAY-CONNECTION.md` - Guía de conexión a PostgreSQL

