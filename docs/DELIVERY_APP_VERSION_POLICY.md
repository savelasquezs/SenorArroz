# Política de versión de la app de domiciliarios

El backend es la única autoridad de compatibilidad para la app Flutter de
domiciliarios. Vue no participa en esta validación.

El portal Vue identifica sus requests con `X-Senor-Arroz-Client: web`. Esa
marca excluye únicamente al cliente web del control de versión móvil; no es un
mecanismo de autenticación ni reemplaza la autorización por rol.

## Configuración

```text
DeliveryAppVersion__Enabled=true
DeliveryAppVersion__RequiredVersionName=1.2.5
DeliveryAppVersion__MinimumBuildNumber=11
DeliveryAppVersion__PlayStoreUrl=https://play.google.com/store/apps/details?id=com.senorarroz.delivery_app
```

`RequiredVersionName` exige igualdad exacta. Con `1.2.5`, las versiones `1.2.4`
y `1.2.6` son incompatibles. `MinimumBuildNumber` es un mínimo: los builds 11,
12 y posteriores son válidos; el 10 no lo es. También se exige el package
`com.senorarroz.delivery_app`.

## Flujo

```text
Flutter PackageInfo
      ↓
headers X-Delivery-App-*
      ↓
preflight, LoginHandler, RefreshTokenHandler y middleware
      ↓
DeliveryAppVersionPolicy
      ├── compatible → operación normal
      └── incompatible → HTTP 426 → Google Play
```

El endpoint público `GET /api/auth/delivery-app-version` permite el preflight de
experiencia de usuario. Login, refresh y el middleware autenticado vuelven a
validar la política; la consulta pública no constituye autorización.

La validación del login ocurre después de comprobar credenciales y antes de
crear una sesión, cerrar una jornada, borrar tokens de dispositivo o emitir
tokens. En refresh ocurre antes de revocar o rotar el token.

## Publicar una actualización

Un cambio normal se envía a `main`. El workflow Flutter consulta Google Play,
calcula el siguiente `versionCode`, genera el App Bundle firmado y publica en el
track `internal`.

Para una versión visible nueva, cambiar `version: 1.2.6+11` en `pubspec.yaml` y,
cuando deba ser obligatoria, configurar
`DeliveryAppVersion__RequiredVersionName=1.2.6` en Railway.

Para forzar un hotfix conservando `1.2.5`, aumentar únicamente
`DeliveryAppVersion__MinimumBuildNumber` al menor build aceptado.

No se requieren secretos adicionales para esta política.
