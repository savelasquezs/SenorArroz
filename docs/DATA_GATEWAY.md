# Data Gateway de diagnóstico — código preparado, configuración pendiente

## Estado y límites de seguridad

El gateway está integrado en la API .NET 9, **desactivado por defecto**.\nIncluye endpoint MCP HTTP JSON-RPC sin sesiones en `POST /api/internal/data-gateway/mcp`,\nprotegido por el mismo JWT Superadmin. La autenticación OAuth y el registro\ncomo conector remoto de ChatGPT requieren configuración/implementación\nposterior; no está conectado automáticamente.
No acepta SQL libre. Solo construye consultas SELECT sobre vistas explícitamente permitidas
del esquema `diagnostics`. Este diseño evita que el cliente llame `set_config()`,
cambie el tenant, ejecute funciones arbitrarias o envíe sentencias DDL/DML por SQL.

Endpoints:
- `GET /api/internal/data-gateway/schema`
- `POST /api/internal/data-gateway/query`
- `POST /api/internal/data-gateway/explain` (sin ANALYZE)

El usuario necesita JWT válido de rol `Superadmin` y tenant válido.
Se aplican transacción READ ONLY, `app.current_tenant_id` y `app.system_scope=off`
por transacción, timeout de 3 s, hasta 500 filas, salida de cadenas limitada, 2
consultas concurrentes por instancia y pool de máximo 3 conexiones.

## Formato de consulta

```json
{
  "view": "diagnostics.orders_summary",
  "columns": ["branch_id", "total"],
  "filters": [{"column": "branch_id", "op": "eq", "value": 1}],
  "orderBy": "total",
  "descending": true,
  "maxRows": 100
}
```

Las columnas y la vista deben existir en el catálogo autorizado. Los operadores
aceptados son eq/ne/gt/gte/lt/lte. El servidor parametriza todos los valores.
La API NO permite joins, SQL arbitrario, UPDATE, DELETE o transacciones externas.
Para nuevas relaciones complejas, agregar una vista de diagnóstico revisada,
no un endpoint por tabla.

## Configuración manual pendiente (no realizada)

1. Auditar RLS y los datos sensibles. Crear un rol PostgreSQL único,
   `LOGIN NOSUPERUSER NOBYPASSRLS`, sin propiedad de tablas, sin CREATE,
   sin membresía del dueño ni BYPASSRLS, y permisos SELECT **solo sobre
   vistas autorizadas**. Verificar que las vistas de diagnóstico filtren
   por tenant y no revelen PII/secretos. Para vistas `security_invoker`,
   configurar cuidadosamente las concesiones de tablas base y FORCE RLS.
   Para vistas con propietario, documentar y probar el contexto de ejecución,
   porque un propietario que omite RLS puede revelar tenants cruzados.
2. Preparar y probar inicialmente `diagnostics.orders_summary` o el nombre
   real de la primera vista autorizada en una BD de pruebas. No existe todavía
   esa vista en el código y NO se aplicó DDL en producción.
3. Establecer en Railway (servicio API) secretos/variables:
   - `ConnectionStrings__DataGatewayReader`: cadena PostgreSQL del lector
     restringido; usar hostname interno de Railway si se aloja allí.
   - `DataGateway__AllowedViews__0=diagnostics.orders_summary`
     (sustituir por la vista realmente creada).
   - `DataGateway__Enabled=true` **solo tras aprobar las pruebas**.
4. Probar aislamiento entre tenants, lectura indebida, latencia, datos sensibles,
   reconexión y apagado seguro (`Enabled=false`).
5. Instalar/conectar el adaptador MCP según cliente elegido. No confundir un
   servidor MCP local con un conector MCP remoto compatible con ChatGPT.

## Clientes en código

- `tools/data-gateway/client.ts`: SDK TypeScript con `schema()`,
  `query()`, `explain()`. Consume HTTP autenticado. Código ejecutado
  por agentes no debe poder leer secretos del servidor.
- `tools/data-gateway/mcp_stdio.py`: puente MCP stdio local
  (Python estándar), útil para Codex local o clientes con servidores stdio.
  Requiere variables `SENOR_ARROZ_GATEWAY_URL` y
  `SENOR_ARROZ_GATEWAY_TOKEN`, este último un JWT Superadmin vigente;
  al vencer hay que renovarlo. Nunca versionar credenciales.
- Para ChatGPT remoto hace falta construir/operar un punto MCP HTTPS
  autenticado, idealmente OAuth o autorización de usuarios, más la
  configuración manual de conexión. El bridge stdio **no está expuesto
  directamente a ChatGPT**.

## Verificaciones

Pruebas unitarias iniciales: gateway apagado, tenant inválido y vista prohibida.
Antes de activar producción se requieren pruebas PostgreSQL con vistas reales,
RLS, privilegios, límites, truncamiento y rechazo de operaciones peligrosas.

El gateway puede desplegarse en `main` inerte; no se necesita modificar
la configuración de producción hasta completar la lista anterior.
