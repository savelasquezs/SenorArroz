# Despliegue de la fundación multitenant

## Prechecks

1. Crear un respaldo y restaurarlo en staging.
2. Confirmar que producción contiene únicamente Tenant 1 (`senor-arroz`). No crear Tenant 2.
3. Crear un rol de aplicación PostgreSQL con `LOGIN NOSUPERUSER NOBYPASSRLS`. El rol de despliegue/owner debe ser diferente.
4. Mantener `No Reset On Close=false` en la cadena Npgsql.

## Orden de aplicación

Con el rol owner, desde la raíz del workspace:

```powershell
psql "$env:DATABASE_OWNER_URL" -v ON_ERROR_STOP=1 -f .\senorArrozAPI\SenorArroz.Infrastructure\Scripts\multitenant_isolation_v2.sql
psql "$env:DATABASE_OWNER_URL" -v ON_ERROR_STOP=1 -f .\senorArrozAPI\SenorArroz.Infrastructure\Scripts\tenant_scoped_unique_indexes_v3.sql
psql "$env:DATABASE_OWNER_URL" -v ON_ERROR_STOP=1 -f .\senorArrozAPI\SenorArroz.Infrastructure\Scripts\enable_multitenant_rls_v3.sql
```

Otorgar al rol runtime `CONNECT`, `USAGE` en `public, app`, DML sobre tablas, uso de secuencias y ejecución de las funciones de `app`. Configurar `ConnectionStrings__DefaultConnection` en Railway con ese rol. Luego validar conectado como runtime:

```powershell
psql "$env:DATABASE_RUNTIME_URL" -v ON_ERROR_STOP=1 -f .\senorArrozAPI\SenorArroz.Infrastructure\Scripts\verify_multitenant_runtime_role.sql
```

El backend de producción también aborta el arranque si el rol tiene `SUPERUSER`/`BYPASSRLS` o hay menos de 93 tablas con RLS forzado.

## Smoke test

- Login, refresh y revocación al incrementar `tenant.access_version`.
- POS, storefront, WhatsApp/Flow, Wompi y Rappi del Tenant 1.
- Cocina, domicilio, caja y agente de impresión.
- SignalR de pedidos, WhatsApp e impresión sin grupos sin prefijo `Tenant_1_`.
- Subida y reemplazo de archivos bajo `tenants/1/`; las URLs históricas continúan siendo legibles.
- Workers sin errores RLS y sin filas procesadas fuera de su scope.

## Rollback

Si falla antes de desplegar la API, restaurar el respaldo. Si la API ya fue desplegada, volver primero a la versión anterior y usar el rol owner para ejecutar `disable_multitenant_rls_v3.sql`; después restaurar la cadena anterior. No eliminar `tenant_id`, FK ni índices durante una contingencia: son cambios de datos no reversibles de forma segura sin restauración.
