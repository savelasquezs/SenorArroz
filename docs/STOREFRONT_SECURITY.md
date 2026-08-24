# Storefront: configuración segura

## Backend

Variables requeridas:

- `ConnectionStrings__DefaultConnection`
- `JwtSettings__SecretKey`
- `Storefront__KeyId`: identificador no secreto; `web-main` ya es el valor predeterminado
- `Storefront__KeyHash`: SHA-256 hexadecimal de la clave, nunca la clave original
- `GoogleMaps__GeocodingApiKey`
- `GoogleMaps__RoutesApiKey`
- `AllowedHosts`: ya restringido a `api.srarroz.com`, `senorarrozapi.up.railway.app` y desarrollo local
- `Cors__AllowedOrigins__0` y siguientes
- `ReverseProxy__KnownProxies__0` y siguientes con las IP del proxy confiable
- `ReverseProxy__KnownNetworks__0` y siguientes para rangos CIDR confiables, cuando el proveedor publique una red estable

Para generar una clave aleatoria y su hash en Windows PowerShell 5.1 o PowerShell 7, sin guardar la clave en el historial:

```powershell
$keyBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
$key = [Convert]::ToBase64String($keyBytes)

$sha = [Security.Cryptography.SHA256]::Create()
try { $hashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key)) } finally { $sha.Dispose() }
$hash = ([BitConverter]::ToString($hashBytes)).Replace('-', '')
```

`Set-Clipboard -Value $key` copia la clave original para `STOREFRONT_API_KEY` en el nuevo servicio Next.js. `Set-Clipboard -Value $hash` copia el hash para `Storefront__KeyHash` en `SenorArroz`. Ambos servicios usan el identificador `web-main`.

`ReverseProxy__KnownProxies` no se inventa ni se rellena con la IP pública del dominio. Debe contener la IP del proxy que se conecta directamente al contenedor. Si Railway no ofrece una IP o red estable, se determina después del despliegue a partir del aviso `Unknown proxy` del middleware y se valida que pertenezca a la infraestructura esperada antes de confiar en ella.

Las claves de Geocoding y Routes deben ser distintas, privadas, restringidas a sus APIs, con cuotas y alertas. Swagger permanece deshabilitado en producción.

## Next.js

Variables del servidor:

- `API_URL`
- `STOREFRONT_KEY_ID`
- `STOREFRONT_API_KEY`

Variable pública:

- `NEXT_PUBLIC_GOOGLE_MAPS_API_KEY`: restringida por dominio a `srarroz.com` y a Maps JavaScript API/Places API (New)

El navegador solo usa `/api/storefront/delivery-quote`; nunca recibe las credenciales del backend.

## Despliegue

1. Rotar la contraseña de base de datos y el secreto JWT que estuvieron versionados.
2. Generar una clave nueva para el storefront y configurar su valor original solo en el servicio público Next.js y su hash solo en el backend. No modificar `senorArrozFront`, que es el panel interno.
3. Ejecutar `SenorArroz.Infrastructure/Scripts/add_branch_active_storefront.sql` en Railway.
4. Desplegar backend, configurar claves, desplegar frontend y validar catálogo/cobertura con direcciones reales.
5. Monitorear latencia, errores, respuestas 429 y consumo de Google sin registrar nombre, teléfono, dirección ni coordenadas.
