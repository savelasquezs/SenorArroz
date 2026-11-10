# Data Gateway de diagnóstico (fase 1)

Estado: **código propuesto en PR, desactivado por defecto**. No es todavía un servidor MCP ni un sandbox de Code Mode. Estos tres endpoints HTTP internos son la base para un adaptador MCP posterior.

## API

Autorización: JWT vigente con rol `Superadmin`, identidad tenant válida y restricciones habituales de la API.

- `GET /api/internal/data-gateway/schema`: columnas de tablas/vistas sobre las que el usuario de base de datos tiene permiso SELECT.
- `POST /api/internal/data-gateway/query`: una consulta `SELECT` de solo lectura, con parámetros.
- `POST /api/internal/data-gateway/explain`: plan PostgreSQL con `EXPLAIN (FORMAT JSON)` **sin ANALYZE**.

Ejemplo:

```json
{
  "sql": "SELECT COUNT(*) AS total FROM public.orders WHERE branch_id = @branch",
  "parameters": {"branch": 1},
  "maxRows": 100
}
```

Los nombres reales de tablas deben obtenerse mediante `schema`; el ejemplo es ilustrativo.

## Configuración y puesta en servicio

1. Crear **otra cuenta PostgreSQL**: `LOGIN`, `NOSUPERUSER`, `NOBYPASSRLS`, **sin propiedad de tablas, sin pertenecer al rol propietario y sin privilegios de escritura**. No reutilizar la credencial de producción de la API. Confirmar RLS/FORCE ROW LEVEL SECURITY en tablas sensibles y que la cuenta no puede acceder a otros tenants con pruebas reales.
2. Priorizar un esquema `diagnostics` de vistas no sensibles y solo conceder `USAGE` de ese esquema más `SELECT` de las vistas expresamente revisadas. Evitar `SELECT` masivo en tablas con datos personales, tokens y secretos. Revocar `CREATE` en esquemas accesibles y revisar permisos `EXECUTE` sobre funciones, especialmente SECURITY DEFINER.
3. Configurar `ConnectionStrings__DataGatewayReader` mediante secreto en Railway. La credencial no debe pasar al cliente ni a GitHub Actions.
4. Verificar red privada de Railway, TLS si se usa proxy público y límites del pool. Configurar `DataGateway__Enabled=true` solo cuando la auditoría de permisos esté aprobada; el valor predeterminado es false.
5. Probar `SELECT` válida, SQL de escritura (rechazada), `SELECT` con función de efectos laterales (denegada por permisos), aislamiento entre tenants, parámetros inválidos, tiempos de espera, truncamiento, conexiones y concurrencia.
6. Integrar adaptador MCP autenticado para ChatGPT en una fase posterior. No publicar el endpoint HTTP con llaves de acceso sin protección adicional.

## Límites iniciales

Máximo 2 solicitudes concurrentes por instancia, 3 segundos de ejecución SQL, 500 filas, cadenas de salida truncadas, SQL máximo 8 KB, 30 parámetros y comandos `SELECT` simples. La sintaxis se filtra solo como defensa adicional: **no es un parser SQL ni garantiza ausencia de funciones con efectos secundarios**. La seguridad debe depender de permisos de PostgreSQL, RLS, transacción READ ONLY, autorización y supervisión.

## Verificación pendiente

No se ha compilado ni ejecutado un test de integración de PostgreSQL en esta propuesta. No desplegar directamente a producción: ejecutar compilación y pruebas en CI y preparar cuenta de lectura con permisos y datos de prueba primero.
