# Señor Arroz Backend - Business Rules

Este documento resume reglas funcionales que Codex debe respetar antes de modificar pedidos, pagos, cocina, domicilios, impresión, caja o módulos relacionados.

## Regla general

El sistema ya opera con clientes reales. Todo cambio debe ser pequeño, seguro y compatible con datos existentes.

## Reproducción histórica de recorridos

- Solo Admin y Superadmin pueden consultar recorridos históricos.
- Admin queda limitado a su sucursal; Superadmin debe operar con una sucursal efectiva.
- El eje temporal es `RecordedAt`; `SyncedAt` solo informa recepción tardía.
- Cada consulta admite máximo 24 horas y 20.000 ubicaciones, sin truncamiento silencioso.
- Las estadías del recorrido provienen de `delivery_stay`; el cliente no debe reconstruirlas desde puntos GPS.
- Una estadía está activa únicamente si su jornada permanece activa y su último punto sigue siendo el último punto registrado de la jornada. En ese caso el contrato expone `endedAt = null` y duración calculada hasta el reloj del servidor.
- El contexto de una estadía incluye todos los pedidos de su ruta, aunque todavía no estén entregados. Si la ruta asociada no existe o no tiene paradas, se usa la ruta inmediatamente anterior del mismo domiciliario y sucursal. El pedido espacialmente más cercano se identifica dentro de ese conjunto y se deduplica por pedido.

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
- Admin, Superadmin y Cajero pueden cancelar pedidos mediante el flujo de cancelación. El Cajero solo puede cancelar pedidos de su propia sucursal.
- Toda cancelación requiere motivo, no admite pedidos ya cancelados y aplica la gestión vigente de pagos, fidelización, rutas y notificaciones asociadas.

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
- Cajero, Admin y Superadmin pueden liquidar pagos por apps, de forma individual o por lote, únicamente dentro de la sucursal efectiva. La liquidación crea el ingreso en el banco asociado y, cuando existe neto esperado de Rappi, exige registrar el valor real consignado.
- El pago, el pedido, la app y el banco deben corresponder a la misma sucursal; un pago revertido no puede liquidarse.
- Desliquidar pagos por apps continúa reservado a Admin y Superadmin.
- Vouchers/cupones de apps deben conservarse como parte de la lógica de pago si la feature los usa.
- Cajero, Admin y Superadmin pueden verificar pagos bancarios de pedidos dentro de la sucursal efectiva; solo Admin y Superadmin pueden desverificarlos.
- En la cola de verificación de `/orders`, las transferencias pendientes se ordenan por monto ascendente.
- Los movimientos banco↔banco y banco↔efectivo se consultan y registran desde el cuadre de caja. Cajero, Admin y Superadmin pueden operar únicamente sobre la sucursal efectiva.

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
- El cajero puede crear comprobantes y nuevos conceptos del catálogo de gastos, pero no puede editar ni eliminar conceptos existentes. También puede crear barrios únicamente en su propia sucursal; la edición y eliminación de barrios continúa reservada a Admin y Superadmin.
- Solo Admin y Superadmin pueden eliminar un comprobante de gasto.
- El IVA 19 % de un comprobante puede aplicarse a todas las líneas o individualmente. `ExpenseHeader.VatAmount` es la suma calculada sobre la base gravable formada exclusivamente por las líneas marcadas; la selección se conserva en cada `ExpenseDetail` para futuras ediciones.
- Antes del borrado en cascada, el trigger de auditoría conserva un snapshot del comprobante con proveedor, domiciliario, total, IVA, notas, líneas, pagos bancarios y abonos de domiciliario vinculados. Si existe un abono `ExpenseOffset` ligado exclusivamente a la factura, se elimina atómicamente con ella y también queda identificado en el snapshot. Los gastos eliminados durante el periodo se incluyen con ese detalle en el correo de auditoría del cierre de caja.
- Las vistas tipo Excel pueden filtrar en frontend para agilidad, pero cambios de rango de fechas deben consultar backend si esa es la regla vigente.
- El backend debe soportar filtros por fechas, sucursal y tenant.

## Promoción del día

- Admin y Superadmin conservan la administración completa de la promoción diaria de la sucursal autorizada.
- El cajero puede crear una promoción únicamente para el día calendario actual de Colombia cuando no exista otra promoción activa que cubra ese día. El backend normaliza su vigencia a la ventana fija de 05:00 a 23:59:59 (hora Colombia), sin confiar en las fechas enviadas por el cliente.
- Si la promoción activa de hoy fue creada por el mismo cajero, puede modificarla o desactivarla. Una promoción creada por otro usuario es de solo lectura para él.
- `DailyPromotion.CreatedByUserId` conserva al autor. Los registros anteriores sin autor no pueden ser modificados por cajeros.
- `StartsAt` y `EndsAt` se conservan en el contrato y la tabla para soportar una futura interfaz de programación; el modal actual no los expone y siempre trabaja sobre el día vigente.

## Clientes y direcciones

Reglas:

- Clientes pertenecen al tenant.
- Direcciones pertenecen al tenant.
- Barrios pertenecen al tenant.
- Teléfonos de clientes pueden repetirse entre tenants.
- Si hay índice único por teléfono, debe ser compuesto por tenant.
- Un cliente debe tener al menos `Phone1` o `WhatsAppUsername`; `Phone2` solo es válido cuando existe `Phone1`.
- El username de WhatsApp se normaliza en minúsculas y con `@`, puede corregirse y no es único. El BSUID (`WhatsAppUserId`) es técnico, estable y solo lo administra el backend.
- La búsqueda de clientes acepta un término combinado por nombre, cualquiera de los dos teléfonos o username, con o sin `@`.

## Productos

Reglas:

- Productos y categorías pertenecen al tenant.
- Nombres pueden repetirse entre tenants.
- No crear catálogos globales compartidos por ahora.
- Cualquier configuración de producto usada en cocina/impresión debe mantenerse compatible.

## Domiciliarios y app móvil

Reglas:

- La app de domiciliarios se distribuye exclusivamente mediante Google Play.
- Todo `Deliveryman` debe informar el package oficial, la versión visible exacta y un build igual o mayor al mínimo configurado. La política se valida antes de efectos de login, antes de rotar refresh tokens y en cada request autenticado; una incompatibilidad responde HTTP 426.
- Admin, Superadmin, Cashier y Kitchen conservan acceso web y no están sujetos al control de versión Flutter.
- Un `Deliveryman` tiene el acceso web deshabilitado por defecto. Solo Superadmin puede habilitarlo individualmente; el permiso se valida en login, refresh y cada request web autenticado, y su revocación responde HTTP 403 desde la siguiente solicitud.
- El frontend web se identifica con `X-Senor-Arroz-Client: web`; SignalR usa `client=web`. Un domiciliario no identificado como cliente web debe cumplir siempre la versión Flutter exigida.

- El enrutador dinámico V1 agrupa por sucursal pedidos internos Delivery y reservas con dirección en Tomado, En preparación o Listo, sin domiciliario ni ruta asignados.
- La matriz de optimización V1 es local y aproximada (Haversine, factor vial y velocidad configurables); Google Route Matrix no es una dependencia de esta versión.
- OR-Tools decide orden, agrupación y visitas opcionales sin imponer un máximo fijo de paradas. La capacidad se modela con domiciliarios libres ahora o próximos a regresar.
- Google Compute Routes solo valida propuestas finales. Si Google falla, la propuesta conserva métricas aproximadas, queda marcada como degradada y nunca presenta ceros como validación exitosa.
- Solo los pedidos actualmente Listos de una propuesta pueden reclamarse. Tomado y En preparación son informativos y se mantienen como espera sugerida.
- La toma desde una propuesta exige versión de plan y se ejecuta en una transacción serializable; ante una carrera responde `ROUTING_PLAN_STALE` sin asignación parcial.
- La consulta del plan es idempotente para una misma huella operativa: reutiliza la versión activa y no publica `DeliveryRoutingPlanChanged` si pedidos elegibles, coordenadas y capacidad permanecen iguales. El recálculo explícito conserva su semántica forzada.
- La selección manual de pedidos continúa disponible como respaldo operativo.

- Domiciliarios son usuarios o entidades asociadas al tenant.
- La app móvil no debe operar fuera del tenant y sucursal autorizados.
- Los domicilios en estado Tomado o En preparación solo se muestran al domiciliario cuando una ubicación GPS actual está dentro de `DeliveryTrackingAllowedDistanceMeters` respecto a las coordenadas de su sucursal.
- La app puede hacer una validación previa para la experiencia de usuario, pero el backend debe volver a calcular la distancia antes de devolver esos pedidos.
- Cuando un pedido propio de tipo Delivery pasa a Listo, la notificación push solo se dirige a domiciliarios de la misma sucursal que no tengan pedidos activos asignados y cuya última ubicación reciente de una jornada activa esté dentro de `DeliveryTrackingAllowedDistanceMeters`.
- Ubicaciones, avances, estados diarios, rutas y paradas pertenecen al tenant.
- Cuando admin o cajero asigna un domicilio, el pedido pasa a En camino, queda vinculado al seguimiento y se notifica a la app. Una ruta `Open` o `InProgress` solo recibe la nueva parada si conserva al menos un pedido `OnTheWay`; las rutas mixtas con entregas previas y pedidos todavía en camino siguen siendo compatibles y no reinician su hora de inicio.
- La app conserva en pantalla los pedidos en camino al volver desde segundo plano y reconcilia la ruta de forma silenciosa. Las modificaciones, reasignaciones, entregas o cancelaciones administrativas de un pedido en camino se publican al grupo SignalR de domiciliarios para actualizar únicamente el pedido afectado.
- Entregar el último pedido normalmente no completa la ruta: comienza el regreso a la sucursal y se conserva el seguimiento GPS activo. La ruta se completa con el primer punto GPS dentro del radio `DeliveryTrackingAllowedDistanceMeters`; ese instante determina el tiempo real y solo entonces se compara con la meta, cuyo tiempo de conducción incluye también el regreso calculado por Google Maps.
- Como excepción, si se asigna un nuevo domicilio cuando todos los pedidos de la ruta anterior ya están entregados o cancelados, la ruta anterior se finaliza en el instante de la nueva asignación y el pedido nuevo inicia otra ruta `Open`.
- La planificación consulta Google Routes una sola vez para el circuito `sucursal -> paradas -> sucursal`, con `TRAFFIC_AWARE_OPTIMAL`; la meta suma esa duración con tráfico, 4 minutos por pedido y el margen adicional de acceso complejo cuando aplique.
- Tokens de dispositivo pertenecen al usuario y tenant.
- Las frecuencias, tolerancias y retenciones del seguimiento se configuran por sucursal.
- La autoentrega GPS se decide exclusivamente en backend y queda activa por sucursal de forma predeterminada. Solo considera puntos con GPS habilitado, precision informada de 0 a 50 metros y captura dentro de dos minutos del reloj del servidor.
- Una parada en camino requiere dos puntos confiables dentro del radio de llegada, separados al menos por la permanencia configurada. Despues de confirmar la visita, el primer punto confiable fuera del radio de salida ejecuta el mismo flujo de negocio de una entrega manual.
- Los radios de llegada y salida usan histeresis: llegada entre 10 y 150 metros, salida entre 20 y 500 metros y siempre mayor que llegada. La permanencia admite de 5 a 300 segundos.
- La evidencia de llegada y autoentrega se persiste por `DeliveryRouteStop`. Una sola parada, priorizada por `StopSequence`, puede acumular evidencia simultaneamente para evitar entregar varios pedidos cercanos con la misma presencia.
- El procesamiento de autoentrega se serializa por ruta mediante un bloqueo transaccional de PostgreSQL; dos puntos concurrentes no pueden duplicar la evidencia ni la transicion oficial de entrega.
- Los pedidos entregados automaticamente exponen esa marca en el contrato de pedidos para mostrar un indicador discreto en `/orders`.
- Una ubicacion offline antigua se conserva en el recorrido, pero nunca cambia automaticamente el estado de un pedido.
- La hora de cierre del seguimiento es una hora local de Colombia; los instantes de ubicación y sesión se persisten en UTC.
- Un domiciliario solo puede tener una sesión laboral activa; un cambio de dispositivo cierra la anterior.
- Un domiciliario solo puede tener una sesión autenticada vigente. Cada login nuevo reemplaza la anterior, invalida inmediatamente sus JWT/refresh tokens por `session_id` y cierra cualquier jornada laboral que estuviera activa.
- Los usuarios distintos de domiciliario conservan el comportamiento de autenticación existente; la exclusividad por dispositivo aplica a la app de domiciliarios.
- Un token previo al despliegue, sin `session_id`, se acepta únicamente mientras el domiciliario no haya realizado su primer login con el nuevo esquema. Esto permite desplegar el cambio sin cerrar todas las sesiones a la vez.
- El cierre de sesión solo puede limpiar la sesión autenticada y la jornada pertenecientes al dispositivo que hace la solicitud; una sesión reemplazada no puede cerrar la jornada del dispositivo vigente.
- Cuando el API responde `SESSION_REPLACED`, la app anterior debe detener el servicio GPS, descartar las colas locales de esa jornada y volver al login mostrando el motivo.
- No se abre una nueva sesión laboral después de la hora de cierre configurada para la sucursal.
- El backend rechaza ubicaciones que no correspondan a la sesión laboral activa del dispositivo.
- Apagar el GPS o retirar el permiso de ubicación durante una jornada crea un registro que no se resuelve automáticamente al recuperar el servicio. La recuperación completa el registro con hora, duración, última ubicación anterior al corte y primera ubicación posterior; un administrador puede cerrarlo manualmente después de revisarlo.
- Un evento confirmado `gps_disabled` también crea un caso pendiente de tipo `location_disabled` en Revisión de seguimiento, porque corresponde a un incumplimiento expresamente revisable. El caso permanece pendiente aunque el GPS se recupere; solo una decisión administrativa cambia su estado.
- La falta de comunicación durante una entrega activa crea una advertencia operativa después de dos minutos; en seguimiento liviano se detecta a los diez minutos. Si el corte genérico dura menos de diez minutos, queda resuelto automáticamente al recuperarse y no genera FCM ni caso administrativo.
- Escala a revisión si dura diez minutos o más, sincroniza al menos quince ubicaciones offline, la evidencia offline cubre al menos siete minutos o Android confirma GPS apagado, permiso retirado, modo avión, app/servicio detenido o Wi-Fi apagado sin otra red.
- El backend conserva causa y certeza separadas. `internet_lost`, datos móviles indisponibles, teléfono apagado o batería agotada quedan como evidencia técnica o causa no determinable cuando el dispositivo no puede confirmarlos. No molestar no genera alerta.
- Las permanencias no esperadas registran domiciliario, hora inicial/final, duración y coordenada central.
- El correo de auditoría diaria identifica al domiciliario e incluye GPS apagado, permiso de ubicación retirado, interrupciones de comunicación con severidad `requires_review` y permanencias que requieren revisión, con enlaces de Google Maps cuando existe evidencia. Las ubicaciones offline breves y la jornada posterior al cierre permanecen disponibles en la app, pero no entran al correo. Antes de construirlo, el cierre de caja procesa los eventos pendientes para no competir con el trabajador periódico.
- Al crear o escalar una alerta activa de los mismos tipos incluidos en la auditoría (`gps_disabled`, `location_permission_revoked`, `no_communication` con `requires_review` o `unexpected_stay`), el backend envía una sola vez un aviso FCM exclusivamente a los dispositivos registrados del domiciliario afectado. El aviso es informativo, advierte sobre una posible falta disciplinaria, permite omitirlo si existía permiso y remite al administrador; no reemplaza la revisión ni registra una decisión disciplinaria.
- El fallo o la ausencia de FCM no revierte ni altera la alerta administrativa. Solo se intenta notificar al crear una alerta revisable o al escalar una advertencia a revisión; reprocesar el seguimiento no vuelve a enviar avisos por la misma alerta.
- Las modificaciones de pedidos se consolidan por edición completa. Una sustitución o conjunto de cambios solo entra como reducción monetaria cuando el total final es menor al inicial; el detalle informa hora Colombia, actor, productos y cantidades afectados. Los pasos intermedios negativos de una edición cuyo total final aumentó o quedó igual no se reportan como merma.
- El identificador transaccional y los productos anterior/nuevo del audit log se habilitan con el script idempotente `SenorArroz.Infrastructure/Scripts/improve_order_monetary_audit_operations.sql`, que debe ejecutarse antes de desplegar el backend; los logs anteriores siguen agrupándose por su marca temporal transaccional.

## WhatsApp y horarios de atención

- Los webhooks resuelven primero por BSUID (`from_user_id`/`contacts[].user_id`) y después por teléfono (`from`/`wa_id`); `contacts[].profile.username` se conserva como dato visible.
- Una conversación puede operar sin teléfono cuando tiene BSUID. Los envíos prefieren teléfono y usan BSUID como alternativa; las plantillas de autenticación continúan exigiendo teléfono.
- Una asociación manual de cliente nunca se reemplaza por inferencia del webhook. Ante conflicto entre historial por BSUID e historial por teléfono se conservan ambos y no se fusionan mensajes.
- Los clientes y conversaciones existentes por teléfono no reciben un BSUID inventado: se enriquecen únicamente con datos entregados por Meta.
- Cada sucursal puede activar y personalizar su mensaje de ausencia.
- La disponibilidad usa el horario semanal de la sucursal y la hora local de Colombia; apertura es inclusiva y cierre es exclusivo.
- Fuera del horario, el mensaje entrante queda no leído, se excluye de la cola de IA y recibe como máximo un aviso durante el periodo continuo de cierre.
- El aviso no cambia el modo de atención de la conversación y se aplica tanto a atención humana como a IA.
- Si el horario está ausente o es inválido, se conserva el flujo normal de atención automática y se registra una advertencia.
- Los mensajes ignorados por cierre no se reprocesan ni reciben una respuesta retroactiva al abrir.

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
## Integración Rappi API v2

Reglas:

- La integración Rappi pertenece a Santander, usa credenciales globales del ambiente y nunca persiste `client_id` ni `client_secret`.
- La tienda `900173116` es padre y la `900173117` hereda su menú. Solo se publica el menú al padre.
- En sandbox `store_integration_id` es `900173116` para la tienda padre y `900173117` para la hija; en POS Tester se selecciona `POS=SeñorArrozDevV2` e `INTEGRACIÓN=SENORARROZDEVV2`.
- Cada webhook tiene un secreto distinto, cifrado en base de datos. La firma usa HMAC-SHA256 sobre `timestamp.rawPayload` y comparación en tiempo constante.
- El Sandbox Tester de Integrations Manager firma una representación heredada que convierte valores booleanos, numéricos y `null` a cadenas; la validación intenta primero el payload crudo y acepta esa representación exacta solo como fallback.
- Si Rappi ya tiene un webhook activo pero el secreto local falta, se rota mediante `reset-secret` y se guarda cifrado antes de continuar; no se intenta crear un duplicado.
- `STORE_CONNECTIVITY` acepta tanto el contrato documentado (`external_store_id`, `enabled`) como el contrato real del Sandbox Tester (`store_id`, `online`, `checked_at`); solo actualiza el estado operativo de la tienda.
- Todo webhook se persiste antes de procesarse y es idempotente por integración y evento.
- El catálogo Rappi es una selección uno a uno de productos internos. Los SKU `product-{ProductId}` y categorías `category-{ProductCategoryId}` son inmutables.
- El Sprint 1 publica únicamente productos simples, sin toppings ni modificadores.
- La disponibilidad deriva del producto seleccionado, activo y disponible según las reglas internas, y se sincroniza en ambas tiendas.
- Solo se admiten órdenes `delivery` con courier Rappi.
- Una orden válida se toma automáticamente y se crea en estado `Taken`. SKU, precio, stock, modificadores o totales inconsistentes la dejan retenida.
- La orden Rappi no genera impresión al ingresar. La comanda de cocina se encola cuando el pedido cambia por primera vez a `Ready`.
- Las órdenes retenidas solo permiten revalidar y aceptar o rechazar; no se permiten sustituciones particulares.
- La recuperación de `SENT` consulta la ventana oficial de 10 minutos y deduplica por conexión y `order_id`.
- `total_order` es el total autoritativo. Descuentos Rappi, descuentos del aliado, cargos, comisión estimada, neto esperado, consignación real y diferencia se conservan por separado.
- Los importes monetarios de Rappi se aceptan como enteros JSON o como decimales sin fracción (`31000` y `31000.00`); nunca se redondean valores fraccionarios.
- En POS Tester, una orden `delivery` gestionada por courier Rappi puede traer `delivery_information=null`. La ausencia de dirección no bloquea la orden; si Rappi envía dirección, se conserva en el snapshot externo.
- Revalidar una incidencia vuelve a interpretar el payload crudo y refresca líneas, descuentos y totales antes de crear el pedido interno.
- El rechazo de Rappi solo aplica mientras la orden está en `SENT`. Una orden `delivery` ya aceptada (`TAKEN`) no se cancela desde el POS de Señor Arroz.
- Las cancelaciones posteriores a la aceptación se originan en Rappi y llegan por `ORDER_EVENT_CANCEL`. El webhook cancela el pedido interno, revierte el pago por app no liquidado, actualiza cocina y rutas y es idempotente.
- Una cancelación Rappi de una orden entregada o con pago ya liquidado crea una incidencia financiera y no altera silenciosamente los movimientos conciliados.
- La consignación real de un lote se prorratea por neto esperado; el residuo monetario se asigna a la última orden.
- `READY_FOR_PICKUP` está habilitado exclusivamente en las tiendas sandbox `900173116` y `900173117` para homologación. En producción permanece deshabilitado hasta confirmación expresa de Rappi. Cambiar el pedido a `Ready` crea automáticamente el envío idempotente y el worker recupera envíos faltantes tras reinicios.
- La información personal y el payload crudo se anonimizan después de 90 días.
- Deshabilitar la integración conserva configuración, eventos, pedidos e historial financiero.
