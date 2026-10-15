# Incidente POS — pedidos repetidos y creación atómica (15-oct-2026)

**Estado:** corrección en PR independiente; no fusionada ni desplegada. SQL pendiente.
**Ámbito:** POS Vue + `SenorArroz` API / PostgreSQL (solo creación de pedidos).
**Alcance fuera del PR:** limpieza o rectificación de los pedidos históricos 8394–8397, conciliación de caja, cambios en workflows de cocina/Flutter/Rappi/Wompi.

## Evidencia: lectura, no escritura
- [Consulta histórica privada](https://github.com/savelasquezs/senorarroz-db-assistant/issues/38): cuatro órdenes 8394–8397 en Santander, entre 12:35:55 y 12:36:46 Colombia, mismo cajero, mismas líneas/cantidad/precio. Tres órdenes se crearon aunque la API devolvió 409; la cuarta obtuvo 201. Tres acabaron canceladas y una entregada.
- [Triggers verificados](https://github.com/savelasquezs/senorarroz-db-assistant/issues/39) y [funciones reales](https://github.com/savelasquezs/senorarroz-db-assistant/issues/40): el INSERT en `order_detail` llama `update_order_totals`, que ejecuta UPDATE a `order`; el trigger `guard_order_management` incrementa `management_version`. Esto invalida el snapshot inicial de EF.
- Railway: tres `DbUpdateConcurrencyException` entre 12:35 y 12:36 al invocar `CreateOrderHandler` en `SaveChangesAsync` del marcador de efectivo. La creación de padre/detalles/inventario se confirmaba *antes* de los pagos y del efectivo. El error 409 animaba a reintentar.
- `OrderSidebar.vue` deshabilita reenvíos mientras se espera una respuesta, pero después de recibir un error permite un nuevo envío del mismo borrador; previamente no había clave idempotente.

## Cambio propuesto
1. Iniciar una transacción **una vez** para `order`, `order_detail`, asignaciones/reservas de inventario, `bank_payment`, `app_payment`, `paid_in_store_cash` y lectura final. Una excepción antes del commit revierte la operación completa.
2. Volver a cargar `management_version` de la fila guardada **después** de los triggers de totales (y pagos) y **antes** de guardar el efectivo. Se preserva el control optimista y no se deshabilitan triggers.
3. Generar una clave UUID v4 por borrador, guardarla localmente **antes** del envío HTTP y reutilizarla tras error ambiguo, pérdida de red o recarga. El servidor persiste la clave y el fingerprint SHA-256 del DTO original; un segundo POST idéntico devuelve el mismo pedido. Clave reutilizada con contenido diferente se rechaza sin crear otro.
4. Índice único por `tenant_id` y `pos_creation_key`, además de comprobación del cajero y sucursal de la petición autenticada. El tenant nunca viene del frontend.
5. Envío a cocina/cola de impresión tras confirmar el commit; un fallo de notificación no hará que el POS interprete como fallida la creación ya confirmada. No se prometen transacciones distribuidas con SignalR/impresión.

## Script, backup y despliegue
**Antes de fusionar o desplegar el backend**:
1. Confirmar respaldo restaurable de MainDatabase (PostgreSQL/R2) y acceso de operador a una base aislada.
2. Probar en PostgreSQL de prueba la aplicación **y re-aplicación** del archivo
   `SenorArroz.Infrastructure/Scripts/add_pos_creation_idempotency.sql`.
3. Revisar seguridad y ejecutar ese **script aprobado** en MainDatabase desde `SenorArroz` en Bash:
   `railway connect MainDatabase`, luego dentro de psql
   `\\i SenorArroz.Infrastructure/Scripts/add_pos_creation_idempotency.sql`
   y `\\q`. Se requiere rol propietario del esquema; no ejecutar con rol runtime.
4. Verificar `pos_creation_key`, `pos_creation_fingerprint` y el índice único, sin introducir datos de prueba en producción.
5. Solo entonces fusionar/desplegar backend, verificar `SUCCESS`, y luego fusionar/desplegar el frontend. Mantener compatibilidad temporal con clientes POS antiguos (campo opcional).
6. Verificar en Santander/Manrique y con cajero/admin/superadmin: pedido normal, pago mixto, efectivo, pérdida de respuesta/reintento, varias líneas, permisos por tenant y pedido entre sucursales. Confirmar que la clave se reutiliza y que la cocina recibe una sola notificación/comanda.
7. **Rollback:** revertir frontend y backend a versiones anteriores. Las columnas aditivas/índice pueden dejarse instaladas sin afectar versiones antiguas. Solo si es necesario, y con otro respaldo y autorización, ejecutar el rollback SQL indicado **como comentarios** en el script. No borra pedidos históricos.

## Pruebas y límites
- Testcontainers PostgreSQL: reproduce los triggers de subtotal/actualización de total/versión; verifica pago mixto+efectivo y reintento; pago inválido e inventario fallido revierten padre/líneas/pagos; requests concurrentes mismo UUID no crean dos; cambios de payload con mismo UUID se rechazan; error SignalR después de commit no altera el resultado.
- Vue/Vitest: dos borradores producen UUID distintos y el mismo borrador, incluso tras error HTTP, conserva y persiste la clave.
- **CI verificado:** [Backend CI](https://github.com/savelasquezs/SenorArroz/actions/runs/38804211264) **SUCCESS, 883 pruebas aprobadas** (incluidas las 6 pruebas PostgreSQL de esta corrección). [Remote order checks](https://github.com/savelasquezs/SenorArroz/actions/runs/38804211822) **SUCCESS**. [Admin Frontend CI](https://github.com/savelasquezs/senorArrozFront/actions/runs/38803388410) **SUCCESS**, build/typecheck y **495 pruebas aprobadas en 90 archivos**.
- **Límite:** todos los SQL y triggers de prueba corrieron en PostgreSQL aislado Testcontainers; no hubo conexión de escritura a MainDatabase ni prueba funcional del POS productivo.
- **Sin medidas** de p50/p95, bytes, CPU/RAM/egress; esta corrección es de integridad y fiabilidad, no una optimización especulativa.
- La creación sin UUID de clientes POS antiguos permanece atómica pero no idempotente ante pérdida de respuesta. A futuro considerar política gradual de campo obligatorio después de actualizar clientes.
- Si un proceso externo modifica la fila mientras se crea el pedido, el guardado transaccional puede seguir respondiendo 409 y hacer rollback; la clave permite reintentar con seguridad.

**No hay merge, migración ni cambios productivos en este PR.**
