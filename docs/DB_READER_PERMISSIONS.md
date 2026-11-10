# Ampliación de lectura de `senorarroz_db_reader` (pendiente de aplicar)

Se inventariaron 129 tablas de producción mediante consultas de catálogo sin
extraer datos de clientes (Issues privados #79–#82 de `senorarroz-db-assistant`).
El lector tiene actualmente 14 objetos con SELECT. Muchas tablas de seguridad,
credenciales, respaldos y registros históricos no tienen RLS forzado y deben
permanecer excluidas.

## Alcance propuesto

Script: `SenorArroz.Infrastructure/Scripts/expand_db_reader_diagnostics_grants_20261110.sql`.

- SELECT de tabla completa en 15 tablas operativas de bajo riesgo: horarios,
  perfiles comerciales, promociones, categorías de gastos, conversión de
  unidades, recompensas, barrios, requisitos de productos, eventos de cocina,
  planeación de rutas y lugares autorizados.
- SELECT por columna (sin datos de costo ni identificadores operativos
  innecesarios) en 6 tablas de inventario, más analíticas agregadas.
- SELECT por columna en códigos de descuento: **sin revelar el código**.
- No se concede SELECT sobre clientes, teléfonos, direcciones, usuarios,
  claves, sesiones, tokens, bancos, pagos, facturación electrónica,
  webhooks, mensajes privados ni tablas de respaldo.

El script exige `FORCE ROW LEVEL SECURITY`, `tenant_id`, y verifica atributos
de seguridad de `senorarroz_db_reader` antes de modificar permisos. Crea
una política RESTRICTIVE por tabla específica al lector para fijarlo al
**tenant 1**: esto evita que el lector pueda obtener datos de otro tenant
mediante `SELECT set_config('app.current_tenant_id', ...)` o
`app.system_scope`. El modelo no permite compartir ese rol con futuros
tenants. Las tablas previamente otorgadas al lector también deben auditarse
para detectar permisos demasiado amplios.

## Para ejecutar más adelante

1. Confirmar que el tenant de operación sigue siendo 1 y auditar las
   14 tablas ya autorizadas (y sus políticas).
2. Respaldar la base y **verificar restaurabilidad**, no solo existencia
   de archivo. La migración no cambia datos, pero sí políticas de seguridad.
3. Pasar el script por el importador del repositorio privado
   `savelasquezs/senorarroz-db-assistant`, revisar PR y SHA-256.
4. Aplicar únicamente mediante el flujo de migraciones que exige
   `respaldo_verificado: SI` y el comentario exacto de autorización.
5. Verificar `has_table_privilege`, `has_column_privilege`, la separación
   entre tenants y ausencia de acceso a contraseñas, datos personales y
   costos internos.
6. Configurar por separado las vistas `diagnostics` y permitirlas en el
   gateway. Los nuevos GRANT **no** activan el MCP ni permiten SQL libre en
   el gateway.

**No ejecutar `GRANT SELECT ON ALL TABLES`**: expondría datos que no
corresponden y tablas de mantenimiento sin protección RLS.
