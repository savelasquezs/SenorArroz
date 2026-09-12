# Migración de clientes globales por tenant

Esta migración es aditiva y manual. No elimine `customer.branch_id`, `phone1`, `phone2`, `address.neighborhood_id` ni `address.delivery_fee`.

## Despliegue

1. Tome un backup verificable de PostgreSQL y habilite `ON_ERROR_STOP` en `psql`.
2. Ejecute `customer_tenant_01_additive_schema.sql` y `customer_tenant_02_backfill_tenant.sql`.
3. Ejecute `customer_tenant_03_backfill_phones.sql` y `customer_tenant_04_backfill_address_branches.sql`.
4. Ejecute `customer_tenant_05_prechecks.sql`, archive su salida y resuelva teléfonos no soportados, tenants cruzados, FKs desconocidas y conflictos de direcciones. Los fijos `604` son contactos administrativos válidos, permanecen en `phone1`/`phone2` y no participan en identidad, OTP, WhatsApp ni merge.
5. Para las decisiones aprobadas de direcciones en producción, ejecute `customer_tenant_05a_apply_confirmed_address_resolutions.sql` y vuelva a ejecutar los prechecks.
6. Despliegue el backend dual-compatible. Compruebe creación y lectura de `customer_phone` y `address_branch` sin activar todavía el índice telefónico único.
7. En una ventana sin escrituras de clientes, ejecute `psql --set=ON_ERROR_STOP=1 --set=tenant_id=1 -f SenorArroz.Infrastructure/Scripts/customer_tenant_06_merge.sql`.
8. Ejecute `customer_tenant_07_postchecks.sql`. Cualquier excepción bloquea el despliegue.
9. Ejecute `customer_tenant_08_enable_constraints.sql`.
10. Despliegue Storefront, WhatsApp Flow y POS; realice smoke tests de OTP, pedido y edición de tarifa en dos sucursales.

En Railway, abra `railway connect MainDatabase` desde el repositorio y use `\i SenorArroz.Infrastructure/Scripts/<archivo>.sql`. No despliegue código que lea las tablas nuevas antes de completar los pasos 1–4.

## Recuperación

Los scripts 01–04 son idempotentes. El script 06 usa una transacción y advisory lock por tenant; si falla, PostgreSQL revierte todas sus reasignaciones. Conserve el backup hasta completar los postchecks y los smoke tests.

Cada cliente absorbido queda inactivo y registrado en `customer_merge_history`. No se hace hard-delete. Las discrepancias de identidad se conservan en el JSON de auditoría y los conflictos de atención de direcciones deben resolverse antes del merge.
