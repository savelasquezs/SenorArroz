# Disaster Recovery de PostgreSQL

Este documento es el runbook de emergencia de la base de datos de **El Señor Arroz**. Su objetivo es poder recuperar PostgreSQL aunque la API, el login o el frontend administrativo no estén disponibles.

La recuperación se opera desde **GitHub Actions + Railway**, no desde la aplicación. La base productiva nunca se sobrescribe durante la restauración: primero se crea una PostgreSQL aislada, se restaura y se valida; el cambio de producción es una acción separada y explícita.

## Arquitectura de respaldo vigente

- Base productiva: servicio Railway `MainDatabase`.
- Servicio de backup: `postgres-backup-cron`.
- Cron: `0 8 * * *` (08:00 UTC, 03:00 Colombia).
- Railway Bucket: `senor-arroz-postgres-backups`.
  - `pg_dump` diario en formato custom.
  - Retención: 90 días.
  - Objetos: `backup-YYYYMMDD-HHMMSS.dump`.
- Cloudflare R2: `senor-arroz-postgres-backups-external`.
  - Copia externa cada domingo.
  - Retención: 84 días.
  - Objetos: `weekly/backup-YYYYMMDD-HHMMSS.dump`.
- PITR: no forma parte de este plan mientras no esté habilitado en Railway.

El 2026-09-23 se hizo una restauración real satisfactoria desde `backup-20260923-171724.dump`. La verificación devolvió 1 tenant, 2 sucursales, 15.303 clientes y 6.988 pedidos.

## Objetivos de recuperación

Con los backups diarios de Railway, el RPO máximo teórico es de aproximadamente 24 horas si la pérdida ocurre justo antes del siguiente backup. La copia externa de R2 protege además contra la pérdida del proyecto/bucket de Railway, pero por sí sola puede tener hasta aproximadamente 7 días de antigüedad.

El RTO depende principalmente del tiempo de crear el PostgreSQL de recuperación, descargar/restaurar el dump y desplegar nuevamente el backend. La base actual es pequeña; el runbook automatiza las tareas manuales que más tiempo consumen.

## Principios de seguridad

1. **Nunca ejecutar `pg_restore` sobre `MainDatabase`.**
2. La restauración siempre va a una PostgreSQL nueva y vacía.
3. La promoción a producción es un workflow distinto y requiere escribir `PROMOTE`.
4. La base anterior nunca se elimina automáticamente.
5. El backend nunca usa el rol owner de PostgreSQL. Antes de promover se crea/verifica `senorarroz_runtime` con `NOSUPERUSER NOBYPASSRLS` y RLS forzado.
6. Los workflows usan un túnel SSH privado de Railway; no es necesario habilitar Public Access en la base de recuperación.
7. No borrar una base anterior o de recuperación hasta terminar smoke tests y conservarla varios días cuando la contingencia sea real.

## Preparación única de GitHub Actions

En GitHub, abrir **Settings → Secrets and variables → Actions**.

### Secret requerido

Crear:

- `RAILWAY_API_TOKEN`: token de Railway con acceso suficiente al proyecto para crear servicios PostgreSQL, leer/escribir variables y consultar deployments. No guardar este token en archivos del repo.

### Variables requeridas

Crear:

- `RAILWAY_PROJECT_ID`: ID del proyecto Railway de producción.
- `RAILWAY_ENVIRONMENT`: normalmente `production`.
- `RAILWAY_BACKUP_SERVICE`: `postgres-backup-cron`.
- `RAILWAY_BACKEND_SERVICE`: nombre o ID del servicio Railway que ejecuta `SenorArroz.API`.
- `DR_API_HEALTH_URL`: opcional pero recomendado; URL pública estable que responda HTTP 2xx cuando el backend está sano.

El workflow de recuperación reutiliza las credenciales S3/R2 guardadas en `postgres-backup-cron`; no se duplican en GitHub. Railway no permite recuperar por CLI el valor de una variable sellada. Si en el futuro se sellan `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `BUCKET`, `ENDPOINT`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET` o `R2_ENDPOINT`, este mecanismo deberá adaptarse o esas credenciales deberán proveerse por otro secreto de GitHub.

## Workflow 1: Database Recovery

Archivo: `.github/workflows/database-recovery.yml`.

Este workflow **no cambia producción**.

### Uso normal

1. Abrir GitHub → **Actions**.
2. Elegir **Database Recovery**.
3. Pulsar **Run workflow**.
4. Elegir `source`:
   - `railway`: backup diario del Railway Bucket.
   - `r2`: copia externa semanal en Cloudflare R2.
5. En `backup_key`:
   - dejar vacío para recuperar el backup más reciente de la fuente elegida; o
   - escribir un objeto exacto para regresar a un punto anterior.
6. En `confirmation`, escribir exactamente `RESTORE`.
7. Ejecutar.

Ejemplos de `backup_key`:

```text
backup-20260923-171724.dump
weekly/backup-20260920-080000.dump
```

No usar el prefijo `weekly/` cuando `source=railway`. Sí usarlo cuando `source=r2`.

### Qué hace automáticamente

1. Se autentica en Railway y enlaza el proyecto/ambiente configurado.
2. Lee las credenciales de backup del servicio `postgres-backup-cron`.
3. Crea **una nueva PostgreSQL** en Railway.
4. Espera a que la nueva base quede `SUCCESS`.
5. Abre un túnel privado Railway → GitHub Runner.
6. Verifica que la base destino no tenga tablas de usuario.
7. Descarga el dump solicitado o el más reciente.
8. Ejecuta `pg_restore --single-transaction --exit-on-error --no-owner --no-acl`.
9. Comprueba conteos de `tenant`, `branch`, `customer` y `order`.
10. Crea/endurece el rol `senorarroz_runtime`.
11. Ejecuta `SenorArroz.Infrastructure/Scripts/verify_multitenant_runtime_role.sql`.
12. Marca el servicio con `DR_VERIFIED=YES` y registra fuente, objeto restaurado y workflow de origen.
13. Deja un resumen en GitHub Actions con el nombre del servicio de recuperación y los conteos.

Si algo falla después de crear PostgreSQL, el workflow deja esa base intacta para inspección. No toca `MainDatabase` y no hace limpieza destructiva automática.

## Workflow 2: Promote Recovery Database

Archivo: `.github/workflows/promote-recovery.yml`.

Sólo se ejecuta cuando **Database Recovery terminó satisfactoriamente** y se decidió usar esa copia como producción.

### Antes de promover

- Revisar el resumen de `Database Recovery`.
- Confirmar que el backup seleccionado corresponde al momento deseado.
- Si la aplicación todavía acepta pedidos en otra base, detener operacionalmente nuevas escrituras durante el cutover para evitar divergencias.

### Ejecución

1. GitHub → Actions → **Promote Recovery Database**.
2. Copiar en `recovery_service` el nombre exacto mostrado por `Database Recovery`.
3. Escribir exactamente `PROMOTE`.
4. Ejecutar.

### Qué valida y cambia

1. Exige `DR_VERIFIED=YES`.
2. Recupera el runtime role/password creado durante la restauración.
3. Abre un túnel privado y vuelve a ejecutar `verify_multitenant_runtime_role.sql` justo antes del cutover.
4. Lee y guarda las conexiones actuales del backend y del backup cron como estado de rollback.
5. Si no puede guardar el valor previo, **aborta antes de cambiar producción**.
6. Cambia `ConnectionStrings__DefaultConnection` del backend al rol runtime de la base recuperada.
7. Espera un nuevo deployment `SUCCESS` del backend.
8. Si existe `DR_API_HEALTH_URL`, exige que responda correctamente.
9. Sólo después cambia `DATABASE_URL` de `postgres-backup-cron` a la base promovida.
10. Nunca elimina la base anterior.

### Smoke test obligatorio después de promover

Antes de reanudar operación normal, comprobar manualmente como mínimo:

- login;
- búsqueda/edición de cliente;
- creación de pedido POS;
- cocina;
- storefront;
- WhatsApp/Flow;
- impresión;
- domiciliarios/rutas;
- Wompi y Rappi si aplican en ese momento.

El workflow comprueba infraestructura y backend, pero no puede sustituir estas pruebas funcionales.

## Workflow 3: Rollback Recovery Database

Archivo: `.github/workflows/rollback-recovery.yml`.

Sirve para revertir el **cutover de conexiones** si la base recuperada no funciona correctamente.

1. GitHub → Actions → **Rollback Recovery Database**.
2. Escribir el mismo `recovery_service` que se promovió.
3. Escribir exactamente `ROLLBACK`.
4. Ejecutar.

El workflow verifica que ese servicio coincida con `DR_ACTIVE_RECOVERY_SERVICE`, restaura la conexión anterior del backend, espera deployment exitoso y después restaura el `DATABASE_URL` anterior del backup cron.

### Advertencia sobre escrituras después de promover

Rollback **no fusiona datos** entre bases. Si después de promover se crean pedidos, pagos, clientes u otras escrituras en la base recuperada y luego se vuelve a la base anterior, esas nuevas filas permanecerán únicamente en la base recuperada. Por eso el rollback debe decidirse durante la ventana de validación o las escrituras posteriores deberán reconciliarse manualmente.

## Elegir qué backup restaurar

Regla general:

- Si la base desapareció o quedó inaccesible: usar el backup más reciente.
- Si se detectó corrupción lógica o un borrado que ocurrió horas/días antes: seleccionar explícitamente un dump anterior al incidente.
- Si el problema incluye pérdida del proyecto/bucket de Railway: usar `source=r2`.

No elegir automáticamente "el más reciente" cuando el incidente es corrupción lógica: el dump más reciente puede contener el mismo problema.

## Recuperación si la aplicación no permite login

No es un bloqueo. El flujo de DR no depende de la API, de JWT ni de usuarios almacenados en PostgreSQL:

```text
Aplicación / login caídos
        ↓
GitHub Actions sigue disponible
        ↓
Railway + Railway Bucket / Cloudflare R2
        ↓
Database Recovery
        ↓
PostgreSQL nueva y validada
        ↓
Promote Recovery Database
```

## Limpieza después de una contingencia

No hacer limpieza el mismo día salvo que sea estrictamente necesario.

Después de varios días de operación estable:

1. Confirmar backups nuevos desde la base promovida.
2. Confirmar que el backup diario de Railway y el semanal de R2 siguen funcionando.
3. Conservar evidencia/logs del incidente.
4. Eliminar manualmente servicios PostgreSQL de recuperación fallidos que ya no se necesiten.
5. Eliminar la base anterior sólo cuando exista certeza de que no contiene información útil pendiente de reconciliar.

Los workflows deliberadamente **no borran bases ni volúmenes**.

## Simulacro periódico

Cada 2–3 meses:

1. Ejecutar `Database Recovery` con el backup más reciente, preferiblemente alternando Railway y R2.
2. Confirmar conteos y verificación RLS.
3. No ejecutar `PROMOTE` durante un simulacro normal.
4. Eliminar manualmente la PostgreSQL temporal después de validar.
5. Registrar cualquier cambio necesario en este documento.

## Archivos relacionados

- `ops/postgres-backup/backup.sh`: creación, retención y copia externa de dumps.
- `ops/postgres-restore/restore.sh`: restauración segura en base vacía; soporta último backup o `RESTORE_BACKUP_KEY` exacto.
- `ops/postgres-recovery/prepare-runtime.sh`: crea/endurece el runtime role y verifica RLS.
- `ops/postgres-recovery/build-database-url.py`: construye URLs PostgreSQL para el runtime/túnel sin hardcodear credenciales.
- `ops/postgres-recovery/build-npgsql-connection-string.py`: construye el `ConnectionStrings__DefaultConnection` de Npgsql para la API usando el rol runtime.
- `SenorArroz.Infrastructure/Scripts/verify_multitenant_runtime_role.sql`: verificación de rol y RLS multitenant.
- `docs/MULTITENANT_DEPLOYMENT.md`: despliegue de la fundación multitenant.

## Regla final

Ante una emergencia real, si hay dudas sobre qué backup seleccionar o sobre el estado de las dos bases, **no borrar ni sobrescribir ninguna**. Crear/restaurar primero, validar, promover después y conservar la base anterior hasta cerrar la contingencia.
