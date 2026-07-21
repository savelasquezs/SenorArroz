# Señor Arroz Backend - Business Rules

Este documento resume reglas funcionales que Codex debe respetar antes de modificar pedidos, pagos, cocina, domicilios, impresión, caja o módulos relacionados.

## Regla general

El sistema ya opera con clientes reales. Todo cambio debe ser pequeño, seguro y compatible con datos existentes.

Antes de tocar código:

1. Leer `AGENTS.md`.
2. Leer `docs/PROJECT_MAP.md`.
3. Leer `docs/DATABASE_MAP.md` si toca entidades o persistencia.
4. Leer `docs/MULTITENANT_PLAN.md` si toca sucursales, usuarios, queries, permisos o datos compartidos.
5. Leer este archivo si toca flujo funcional.

## Multitenancy funcional

Decisión confirmada:

```text
Una sola base de datos compartida
+ todas las tablas del negocio tenant-owned
+ TenantId obligatorio progresivamente
```

Reglas:

- Cada restaurante/empresa será un `Tenant`.
- Cada sucursal pertenece a un tenant.
- Los usuarios pertenecen a un tenant.
- Los pedidos, clientes, productos, pagos, gastos, rutas e impresión pertenecen a un tenant.
- El frontend no debe decidir el tenant.
- El backend debe resolver tenant desde contexto seguro.
- `BranchId` sigue siendo importante, pero siempre dentro de `TenantId`.

## Pedidos

Reglas esperadas:

- Un pedido pertenece a una sucursal.
- En SaaS, un pedido también debe pertenecer a un tenant.
- Los usuarios no superadmin solo deben consultar/modificar pedidos de su sucursal autorizada.
- En el modelo multitenant, incluso superadmin debe operar con tenant explícito o contexto administrativo controlado.
- Los detalles del pedido deben copiar `TenantId` desde el pedido, no desde el frontend.
- Los cambios de estado deben conservar trazabilidad cuando aplique.

### Estados de pedido

Respetar el flujo existente del sistema. No renombrar ni eliminar estados sin revisar frontend, app móvil e impresión.

Estados funcionales mencionados en el proyecto:

- Tomado
- En preparación
- Listo
- Asignado / en domicilio cuando aplique
- Entregado / finalizado cuando aplique
- Cancelado cuando aplique

Reglas:

- Cocina trabaja principalmente con pedidos tomados/en preparación/listos.
- No cambiar comportamiento de estados sin revisar pantalla de cocina y app de domiciliarios.
- Si un pedido en estado tomado se procesa desde cocina, puede pasar internamente por preparación antes de listo si la regla actual lo requiere.

## Cocina

Reglas:

- La pantalla de cocina agrupa productos por categoría.
- Debe mantenerse compacta y responsive.
- Puede existir modo combinado para ver juntos pedidos tomados y en preparación.
- En modo combinado, pedidos tomados y en preparación pueden mostrarse en la misma columna por categoría.
- Al seleccionar productos/pedidos y marcarlos listos, los que estaban tomados deben avanzar correctamente sin saltarse reglas internas.
- No romper la agrupación por categoría.
- No eliminar información operacional visible necesaria para cocina.

Codex debe revisar frontend y backend si cambia:

- Estados visibles en cocina.
- Agrupación por categoría.
- Transiciones de estado.
- Reglas de selección masiva.
- Impresión o notificaciones asociadas.

## Domicilios

Reglas:

- Los domicilios pueden tener valor propio.
- Existe regla de domicilio gratis/subsidio configurable por sucursal.
- Si el domicilio es menor o igual al valor subsidiado, se descuenta ese valor.
- Si el domicilio supera el valor subsidiado, solo se descuenta hasta el tope configurado.
- El descuento puede aplicarse al primer producto o distribuirse según cantidad, de acuerdo con la implementación definida.
- El valor configurable debe vivir en configuración de sucursal o entidad equivalente, no hardcodeado.

Regla multitenant:

- Configuraciones de domicilio pertenecen al tenant y sucursal.

## Pagos

Medios contemplados:

- Efectivo
- Transferencia bancaria
- Pagos por aplicaciones de comida
- Tarjeta desde apps cuando aplique

Reglas:

- Bancos pertenecen al tenant.
- Apps de comida pertenecen al tenant.
- Pagos pertenecen al tenant.
- No compartir bancos/apps entre restaurantes.
- Un pago por app puede quedar retenido antes de liberarse a banco.
- El sistema debe poder reflejar saldo retenido por app.
- Cuando dinero retenido se libera, debe impactar el banco correspondiente según regla actual.
- Vouchers/cupones de apps deben conservarse como parte de la lógica de pago si la feature los usa.

Codex debe tener cuidado al tocar:

- `AppPayment`
- `BankPayment`
- `BankTransfer`
- `ReservationDeposit`
- `CashRegisterClosure`
- `CashVaultMovement`

## Caja y cierres

Reglas:

- Los cierres de caja pertenecen a una sucursal y tenant.
- Conciliaciones bancarias deben respetar tenant.
- Préstamos informales por sucursal deben respetar tenant.
- Movimientos de caja/bóveda deben respetar tenant.

No modificar cierres de caja sin revisar efectos sobre:

- Pagos
- Bancos
- Gastos
- Préstamos informales
- Reportes

## Gastos

Reglas:

- Gastos, encabezados, detalles, categorías, proveedores y pagos bancarios de gastos pertenecen al tenant.
- Las vistas tipo Excel pueden filtrar en frontend para agilidad, pero cambios de rango de fechas deben consultar backend si esa es la regla vigente.
- El backend debe soportar filtros por fechas, sucursal y tenant.

## Clientes y direcciones

Reglas:

- Clientes pertenecen al tenant.
- Direcciones pertenecen al tenant.
- Barrios pertenecen al tenant.
- Teléfonos de clientes pueden repetirse entre tenants.
- Si hay índice único por teléfono, debe ser compuesto por tenant.

## Productos

Reglas:

- Productos y categorías pertenecen al tenant.
- Nombres pueden repetirse entre tenants.
- No crear catálogos globales compartidos por ahora.
- Cualquier configuración de producto usada en cocina/impresión debe mantenerse compatible.

## Domiciliarios y app móvil

Reglas:

- Domiciliarios son usuarios o entidades asociadas al tenant.
- La app móvil no debe operar fuera del tenant y sucursal autorizados.
- Los domicilios en estado Tomado o En preparación solo se muestran al domiciliario cuando una ubicación GPS actual está dentro de `DeliveryTrackingAllowedDistanceMeters` respecto a las coordenadas de su sucursal.
- La app puede hacer una validación previa para la experiencia de usuario, pero el backend debe volver a calcular la distancia antes de devolver esos pedidos.
- Cuando un pedido propio de tipo Delivery pasa a Listo, la notificación push solo se dirige a domiciliarios de la misma sucursal que no tengan pedidos activos asignados y cuya última ubicación reciente de una jornada activa esté dentro de `DeliveryTrackingAllowedDistanceMeters`.
- Ubicaciones, avances, estados diarios, rutas y paradas pertenecen al tenant.
- Tokens de dispositivo pertenecen al usuario y tenant.
- Las frecuencias, tolerancias y retenciones del seguimiento se configuran por sucursal.
- La hora de cierre del seguimiento es una hora local de Colombia; los instantes de ubicación y sesión se persisten en UTC.
- Un domiciliario solo puede tener una sesión laboral activa; un cambio de dispositivo cierra la anterior.
- No se abre una nueva sesión laboral después de la hora de cierre configurada para la sucursal.
- El backend rechaza ubicaciones que no correspondan a la sesión laboral activa del dispositivo.

## Impresión POS

Reglas:

- Configuración de impresión pertenece a sucursal y tenant.
- Trabajos de impresión pertenecen a sucursal y tenant.
- El agente de impresión debe autenticarse de forma segura.
- El token del agente nunca debe exponerse ni guardarse en repositorio.
- El agente solo debe leer trabajos de su sucursal/tenant.
- No cambiar payload de impresión sin revisar `senorArrozPrintAgent`.

## Seguridad

Reglas obligatorias:

- No confiar en `TenantId` enviado desde frontend.
- No confiar en `BranchId` enviado desde frontend sin validar permisos.
- Validar rol, sucursal y tenant antes de operaciones sensibles.
- No exponer entidades de dominio directamente en API si el patrón actual usa DTOs.
- No registrar tokens ni datos sensibles en logs.

## Reglas para Codex

Cuando una tarea toque una regla funcional:

1. Identificar módulo exacto en `docs/PROJECT_MAP.md`.
2. Identificar tablas afectadas en `docs/DATABASE_MAP.md`.
3. Confirmar si hay impacto en frontend, app móvil o print agent.
4. Proponer plan antes de editar código.
5. Mantener cambios pequeños.
6. No mezclar refactor con cambio funcional grande.
7. Actualizar documentación si cambia una regla de negocio.

## Checklist antes de merge

- ¿Se mantiene aislamiento por sucursal actual?
- ¿La feature queda preparada para tenant?
- ¿No se rompió cocina?
- ¿No se rompió impresión?
- ¿No se rompió app móvil?
- ¿No se rompieron pagos/caja?
- ¿Los filtros de fecha y sucursal siguen funcionando?
- ¿Los índices únicos futuros deben ser por tenant?
