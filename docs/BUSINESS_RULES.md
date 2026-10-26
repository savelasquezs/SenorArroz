# Señor Arroz Backend - Business Rules

## Fundación multitenant terminada

- Las 108 entidades operativas tienen filtro EF, write guard, `tenant_id NOT NULL`, FK y RLS forzado.
- PostgreSQL recibe `app.current_tenant_id` y `app.system_scope` en cada conexión/comando; el pool limpia el contexto y prohíbe `No Reset On Close`.
- El rol runtime de producción debe ser `NOSUPERUSER NOBYPASSRLS`; el backend aborta el arranque cuando el rol o la cobertura RLS son inseguros.
- SignalR, almacenamiento y agente de impresión usan namespaces de tenant. El token del agente resuelve su tenant persistido antes de procesar.
- Cada JWT se contrasta con `Tenant.Status`, `IsActive` y `AccessVersion`; suspensión, cancelación o incremento de versión revocan tokens emitidos.
- Tenant 1 se mantiene por configuración explícita del entorno actual. Tenant 2 sólo se usa en pruebas/local hasta que exista onboarding de producto.
- Sólo quedan pendientes funciones SaaS de producto: control plane, planes, suscripciones, metering, invitaciones y portal `/platform`.

Este documento resume reglas funcionales que Codex debe respetar antes de modificar pedidos, pagos, cocina, domicilios, impresión, caja o módulos relacionados.

Los timestamps de estados con `Z` u offset explícito se normalizan a UTC al leerlos;
la zona horaria del servidor no modifica los instantes ni los límites de espera.

## Inventario

- Registrar una compra inventariable confirma que el insumo ya llegó: convierte la presentación a unidad base, aumenta existencia y actualiza el costo promedio en la misma transacción.
- No existen recepciones separadas, compras en tránsito ni recepciones parciales.
- Las recetas `Estimated` descuentan al iniciar preparación, admiten saldo teórico negativo y nunca bloquean un pedido.
- Las recetas `Strict` reservan al quedar el pedido tomado, consumen al iniciar preparación y nunca admiten sobreventa. Inicialmente este modo corresponde a bebidas.
- Las reservas programadas se realizan al llegar `PrepareAt`; si no hay existencia, el pedido conserva una alerta administrativa y no entra en preparación.
- Editar un pedido tomado libera su reserva anterior y reserva el snapshot nuevo dentro de una sola transacción. Cancelar antes de preparación libera reservas; después de consumir no repone automáticamente.
- Cada pedido conserva su snapshot de receta. Cambiar una receta no modifica pedidos históricos.
- Un producto sólo activa inventario después de guardar una receta válida y confirmar conteo de apertura de todos sus insumos.
- Los conteos físicos ajustan mediante ledger; no sobrescriben saldos sin movimiento. En insumos estrictos nunca pueden dejar menos existencia que la reservada.
- Un conteo abierto puede guardar cantidades físicas parciales en servidor; el borrador no genera movimientos ni diferencias definitivas y solo puede modificarse dentro de su sucursal y tenant.
- La configuración de inventario puede copiarse entre insumos del mismo tenant en una sola transacción. Solo replica control de inventario, estado activo, unidad base y presentaciones; conserva nombre, categoría, unidad contable e imputaciones de cada destino.
- Los movimientos son inmutables e idempotentes. Compras corregidas o eliminadas generan reversión, no edición del ledger.
- Las transferencias siguen únicamente `Draft -> Dispatched -> Received/ReceivedWithDifference`; cualquier diferencia exige motivo y nunca cruza tenants.
- Productos sin inventario activo continúan con `Product.Stock`; los arroces legacy sin stock permanecen ilimitados.

## Clientes, direcciones y sucursales

- Un celular normalizado identifica un solo cliente dentro de un tenant, pero puede repetirse en tenants diferentes.
- `Customer.BranchId` es únicamente la sucursal de origen; no limita identidad, compras ni direcciones.
- La dirección física se comparte entre sucursales. Barrio, tarifa y cobertura se leen/escriben mediante `AddressBranch` para la sucursal operativa.
- Desde el POS, la sucursal que atiende el pedido se selecciona una sola vez en `OrderSidebar`. El formulario de dirección utiliza ese destino y no ofrece un segundo selector. La sesión, el JWT y el contexto global del cajero permanecen en su sucursal de origen.
- Los pedidos `Onsite` enviados desde una sede para recogida en otra permiten `CustomerId` y `AddressId` nulos, pero requieren `GuestName` no vacío para identificar a quien recoge. Para pedidos `Onsite` locales ese nombre sigue siendo opcional; `Delivery` y `Reservation` conservan sus requisitos actuales.
- La sucursal que atenderá una dirección se deriva del `NeighborhoodId`; `AddressRepository.CreateAsync()` crea el `AddressBranch` correspondiente. No se persiste un segundo selector de sucursal en `Address` y `Customer.BranchId` conserva el origen legacy.
- Si al crear un cliente el teléfono ya existe dentro del tenant, se reutiliza el cliente sin cambiar su sucursal de origen, pero la dirección inicial enviada debe crearse si no existe para permitir continuar el pedido en la sucursal operativa elegida.
- Una sucursal nueva crea `AddressBranch` bajo demanda; nunca copia clientes o direcciones.
- `Order.DeliveryFee` es histórico y no cambia cuando se actualiza la tarifa vigente.
- Fidelización cuenta pedidos entregados globalmente por `CustomerId`; el premio aplicable sigue usando la configuración de la sucursal del pedido actual.

## Núcleo multitenant de identidad y autenticación

- Todo usuario pertenece directamente a un tenant y a una sucursal del mismo tenant.
- El tenant nunca se toma de ruta, query, body ni headers enviados por el frontend. Login, refresh, tokens de credenciales y device tokens lo derivan de entidades persistidas.
- Un tenant puede autenticarse únicamente cuando `Status = Active` e `IsActive = true`.
- El JWT conserva los claims operativos existentes y agrega `tenant_id`, `tenant_public_id` y `tenant_access_version` desde el tenant cargado por backend.
- Un refresh token sólo puede renovarse cuando su `TenantId`, el claim expirado y el `TenantId` actual del usuario coinciden.
- `RefreshToken`, `PasswordResetToken` y `UserDeviceToken` deben conservar el mismo tenant que su usuario.
- `X-Branch-Id` sólo selecciona contexto operativo dentro del tenant autenticado; no concede autoridad sobre otro tenant.
- Requests autenticados resuelven tenant desde JWT; procesos públicos y background deben resolverlo desde configuración o datos persistidos del backend.
- `IsActive` es compatibilidad temporal con código legacy; cualquier suspensión o cancelación debe sincronizarlo con `Status`.

Pendiente de producto SaaS: capabilities, control plane, planes, add-ons, suscripciones, cobro, metering, invitaciones y portal `/platform`.

## Aislamiento multitenant v2

- Requests autenticados resuelven tenant desde JWT. Procesos públicos resuelven un tenant configurado o una integración persistida; workers multiempresa iteran tenants activos dentro de scopes independientes. No existe fallback operativo implícito a `TenantId = 1`.
- Toda entidad operativa implementa `ITenantOwned`; `Tenant` es la única entidad global del modelo EF.
- Un request sin tenant no puede leer ni escribir filas tenant-owned.
- Los scopes de sistema sólo se permiten para resolver tenant, seleccionar trabajo global o ejecutar limpiezas explícitas; el procesamiento funcional vuelve a `BeginTenantScope`.
- Un alta sin `TenantId` recibe el tenant actual. Un alta, cambio o borrado cross-tenant se rechaza antes de guardar.
- La matriz vinculante de entidades, workers y excepciones está en `docs/MULTITENANT_ISOLATION_AUDIT.md`.
- La matriz vinculante incluye RLS, contexto PostgreSQL, unicidades, tiempo real, archivos e impresión.

## Regla general

- SignalR de pedidos valida la misma versión de app que HTTP. En `/hubs/orders` admite versión, compilación y paquete desde query para clientes WebSocket de navegador; los endpoints HTTP operativos siguen exigiendo cabeceras.

El sistema ya opera con clientes reales. Todo cambio debe ser pequeño, seguro y compatible con datos existentes.

## Reproducción histórica de recorridos

- Solo Admin y Superadmin pueden consultar recorridos históricos.
- Admin queda limitado a su sucursal; Superadmin debe operar con una sucursal efectiva.
- El eje temporal es `RecordedAt`; `SyncedAt` solo informa recepción tardía.
- Cada consulta admite máximo 24 horas y 20.000 ubicaciones, sin truncamiento silencioso.
- Las estadías del recorrido provienen de `delivery_stay`; el cliente no debe reconstruirlas desde puntos GPS.
- Una estadía puede estar activa solo si la jornada sigue abierta, su último punto es reciente para su cadencia y sigue siendo el último punto de la jornada. Su duración y `endedAt` describen exclusivamente muestras confirmadas; el reloj del servidor no añade tiempo sin evidencia. Las reglas de alertas y reconstrucción se documentan en `docs/TRACKING_ALERT_EVIDENCE.md`.
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

### Pedidos desde la web pública

- El storefront se mantiene single-tenant hasta que todas las consultas públicas estén aisladas por tenant; no se habilita para un segundo negocio con las consultas globales actuales.
- Productos y categorías conforman un catálogo compartido entre sucursales del negocio; el carrito conserva los mismos `ProductId` al cambiar la sede sugerida.
- El catálogo web clasifica las categorías con un rol explícito: arroz, combo, bebida, adición u oculto. Nunca determina el uso público analizando nombres durante una solicitud.
- Las fichas comerciales agrupan presentaciones del mismo producto; la presentación y el orden web son metadatos del producto. Sin ficha, el producto se expone como grupo individual.
- Las opciones de cada ficha pública se entregan de menor a mayor precio; el orden web resuelve empates.
- Una solicitud web debe contener al menos un arroz o combo. Categorías ocultas, productos inactivos y productos agotados se rechazan aunque el navegador envíe directamente su `ProductId`.
- La web pública nunca envía precios, tenant, stock ni sucursal como fuente de verdad. El backend vuelve a resolver producto, precio, estado, disponibilidad y promociones antes de generar el mensaje.
- El catálogo público expone únicamente `available`, `lowStock` o `unavailable`; nunca publica el inventario exacto. Las promociones solo se entregan después de determinar la sucursal mediante una cotización.
- El navegador accede al backend únicamente mediante el BFF de Next.js. Los endpoints públicos del backend exigen `X-Storefront-Key-Id` y `X-Storefront-Key`; el backend conserva únicamente el hash SHA-256 de la clave y lo compara en tiempo constante.
- El cliente elige entre domicilio y recogida. Domicilio exige nombre, teléfono, ciudad y ubicación confirmada; recogida exige nombre, teléfono y una sucursal habilitada, pero nunca dirección del cliente. Las ciudades de domicilio habilitadas inicialmente son Medellín, Bello y Copacabana. Los datos internos de entrega (torre, piso, apartamento, interior o bloque) son opcionales, se separan de la dirección geocodificable y admiten hasta 160 caracteres. La web pública no solicita notas por producto.
- El checkout informa que al hacer el pedido el cliente acepta el tratamiento de datos bajo la política versión `2026-08-24`; no usa checks de aceptación ni solicita autorización promocional.
- Google busca direcciones y lugares conocidos para que el cliente confirme visualmente el punto antes de cotizar. El pin puede ajustarse sin modificar el texto que el cliente escribió; la cotización usa las coordenadas confirmadas y calcula distancia y tiempo desde todas las sucursales activas que tengan coordenadas y teléfono principal. Geocodificación y rutas se cachean durante cinco minutos sin usar datos personales legibles como clave.
- La web pública muestra y contacta todas las sedes habilitadas usando `Branch.Phone1` y, si está vacío, `Branch.Phone2`. La configuración de WhatsApp/IA no decide la elegibilidad de la sede ni recibe pedidos o contactos originados en la landing.
- Para domicilio, el backend asigna automáticamente la sede cubierta más conveniente; el cliente no debe resolver decisiones logísticas internas. Para recogida sí elige la sede.
- Dentro de cobertura, el compromiso mostrado al cliente para domicilio es un rango fijo de 35 a 45 minutos. Google continúa determinando cobertura y tarifa internamente; para recogida se muestran 20 minutos de preparación.
- La cobertura exige cumplir simultáneamente una distancia de ruta de Google de máximo 5 km y un desplazamiento de máximo 30 minutos. El mapa mantiene una casita y un círculo azul de 5 km por sucursal aunque cambie la dirección, dibuja la ruta real después de validar y el carrito reutiliza un único mapa para sucursales, coberturas, cliente y ruta. El backend conserva la ruta de Google como fuente de verdad para la cotización.
- El teléfono del pedido web se normaliza a los últimos diez dígitos y debe cumplir `^3\d{9}$`; la misma normalización se aplica antes de validar y antes de construir el mensaje de WhatsApp. Esta regla no modifica los teléfonos de contacto de las sucursales.
- El domicilio estimado cuesta `$3.000` hasta 2 km inclusive. Por cada kilómetro adicional iniciado suma `$1.000`: 3 km cuestan `$4.000`, 4 km `$5.000`, y así sucesivamente. La sucursal confirma siempre el valor final.
- La cotización contiene cliente, teléfono, dirección confirmada, sucursal, carrito, precios vigentes, tiempo, tarifa, subtotal y total. Para recogida no exige ni persiste una dirección del cliente.
- Cualquier cambio en productos, cantidades, dirección, datos adicionales, cliente, modalidad o sucursal invalida la cotización. Antes de confirmar se revalidan precio, disponibilidad, promoción y, para domicilio, cobertura y tarifa.
- Dentro de cobertura, efectivo crea directamente una orden operativa idempotente después de verificar el celular. Pago en línea crea primero un checkout temporal y solo materializa el pedido después de la aprobación de Wompi. WhatsApp se conserva únicamente como alternativa de consulta manual cuando la entrega requiere revisión.

Reglas esperadas:

- Un pedido pertenece a una sucursal.
- Admin, Cajero y Superadmin pueden crear un pedido para cualquier sucursal activa de su mismo tenant sin cambiar su sucursal de sesión. `Order.BranchId` identifica la sucursal operativa y `Order.TakenById` conserva al usuario autenticado que lo tomó.
- En pedidos con dirección, `AddressBranch` de la sucursal operativa es la autoridad de cobertura y tarifa. La creación se rechaza si no existe, no está cubierta o pertenece a otro tenant; la tarifa enviada por el cliente siempre se sustituye por la configurada.
- Cliente, dirección, bancos y apps usados al crear el pedido deben pertenecer al mismo tenant y, donde corresponda, a la sucursal operativa. `Customer.BranchId` sigue siendo únicamente el origen histórico del cliente.
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
- Las notas de cada línea se muestran bajo su producto en pedidos activos y listos. Los anuncios de pedidos nuevos, reservas y traslados leen la nota junto al nombre completo y la cantidad de su producto; las notas generales se identifican aparte.
- Cada línea anuncia su producto una sola vez y lee inmediatamente su nota antes del siguiente producto. Los avisos de edición también nombran el producto una sola vez.
- Las modificaciones de notas de productos en pedidos visibles de cocina se notifican por OrderModified, identificando producto, cantidad y si la nota se agregó, cambió o eliminó. Una nota nueva o modificada se lee completa hasta el límite de voz vigente de 400 caracteres por nota; una eliminación no vuelve a leer instrucciones descartadas. Los avisos de productos agregados o sustituidos incluyen sus notas actuales. Se conservan elegibilidad por estado/horario, tenant, sucursal y activación manual de voz.

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
- `BranchInformalLoan.BankId = null` representa efectivo. Un banco de origen debe ser normal, activo y de la misma sucursal; el origen no se edita y los anticipos de domiciliarios siempre son de efectivo.
- Cada abono conserva un historial inmutable con saldo anterior y posterior. Un abono directo solo reduce el préstamo y, si su origen es banco, recupera ese valor en el esperado bancario; nunca crea movimientos bancarios ni de caja.
- Un abono convertido en gasto crea el `ExpenseHeader` y el abono dentro de la misma transacción, sin pago bancario ni compra de inventario. El total del gasto no puede superar el saldo y el préstamo se da de baja automáticamente al llegar a cero.
- El esperado del banco descuenta el saldo activo más los abonos convertidos en gasto, comparado contra la instantánea del último cierre. La baja manual recupera el saldo restante. Este ajuste no modifica el saldo ni el historial general de Bancos.
- Solo Admin y Superadmin consultan el historial completo; el acceso mantiene el filtro de sucursal para Admin y la sucursal efectiva para Superadmin.
- Movimientos de caja/bóveda deben respetar tenant.
- El cierre, el despacho de auditoría diaria y el mensaje de correo en la cola se confirman atómicamente. Un fallo previo al encolado revierte el cierre para que un reintento no cree registros duplicados ni recorte el período auditado.

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
- Admin, Superadmin y Cashier conservan acceso web; Kitchen solo con WebAccessEnabled. No están sujetos al control de versión Flutter.
- Un `Deliveryman` tiene el acceso web deshabilitado por defecto. Solo Superadmin puede habilitarlo individualmente; el permiso se valida en login, refresh y cada request web autenticado, y su revocación responde HTTP 403 desde la siguiente solicitud.
- El frontend web se identifica con `X-Senor-Arroz-Client: web`; SignalR usa `client=web`. Un domiciliario no identificado como cliente web debe cumplir siempre la versión Flutter exigida.

- El enrutador dinámico V1 agrupa por sucursal pedidos internos Delivery y reservas con dirección en Tomado, En preparación o Listo, sin domiciliario ni ruta asignados.
- La matriz de optimización V1 es local y aproximada (Haversine, factor vial y velocidad configurables); Google Route Matrix no es una dependencia de esta versión.
- OR-Tools decide orden, agrupación y visitas opcionales sin imponer un máximo fijo de paradas. La capacidad se modela con domiciliarios libres ahora o próximos a regresar.
- Las rutas respetan la dispersión direccional configurada. La llegada a cada destino se limita a `MaximumDeliveryAfterDepartureSeconds` desde la salida (35 minutos inicialmente), incluyendo atención en paradas anteriores. La antigüedad aumenta la prioridad; no reduce a cero el presupuesto de viaje de un pedido atrasado.
- El reagrupamiento geográfico y la reinserción de pendientes conservan los pedidos seleccionados y no pueden retrasar sus llegadas más de `MaximumAdditionalDelaySeconds` (180 segundos inicialmente) respecto al cálculo inicial. El límite es acumulado entre fases, no se renueva con cada inserción. Se conservan ventanas de preparación, certeza de cocina y disponibilidad del domiciliario.
- Los pendientes se evalúan en todas las posiciones de las rutas compatibles, priorizando pedidos listos, antigüedad y cercanía al corredor. `SoftDetourReferenceSeconds` penaliza el incremento de conducción por encima de su referencia. Los pedidos que excedan los límites permanecen pendientes.
- La app muestra conducción y llegada estimada al último destino por separado; esta última incluye atención de las paradas anteriores y se mide desde la salida, sin añadir la atención posterior a la última llegada.
- Google Compute Routes solo valida propuestas finales. Si Google falla, la propuesta conserva métricas aproximadas, queda marcada como degradada y nunca presenta ceros como validación exitosa.
- Solo los pedidos actualmente Listos de una propuesta pueden reclamarse. Tomado y En preparación son informativos y se mantienen como espera sugerida.
- La toma desde una propuesta exige versión de plan y se ejecuta en una transacción serializable; ante una carrera responde `ROUTING_PLAN_STALE` sin asignación parcial.
- La consulta del plan es idempotente para una misma huella operativa: reutiliza la versión activa y no publica `DeliveryRoutingPlanChanged` si pedidos elegibles, coordenadas y capacidad permanecen iguales. El recálculo explícito conserva su semántica forzada.
- El envejecimiento de una misma evidencia GPS no cambia por sí solo la huella de disponibilidad; sí la cambian nueva evidencia, ruta, pedidos pendientes, estimación base, clasificación o pérdida de vigencia. El resultado de Google para un regreso se reutiliza mientras esa evidencia siga vigente, incluyendo el respaldo aproximado si Google falla.
- El plan expone `expiresAtUtc`. La app renueva automáticamente al vencer, reconectar o volver al primer plano, ignora avisos de versiones ya conocidas y acepta la versión anunciada aunque el aviso llegue antes de su respuesta HTTP. Mantiene una sola consulta simultánea y hasta tres reintentos transitorios, a 1, 2 y 4 segundos, sin temporizadores de renovación en segundo plano. Las rutas aceptadas no se modifican.
- La selección manual de pedidos continúa disponible como respaldo operativo.

- Domiciliarios son usuarios o entidades asociadas al tenant.
- La app móvil no debe operar fuera del tenant y sucursal autorizados.
- Los domicilios en estado Tomado o En preparación solo se muestran al domiciliario cuando una ubicación GPS actual está dentro de `DeliveryTrackingAllowedDistanceMeters` respecto a las coordenadas de su sucursal.
- La app puede hacer una validación previa para la experiencia de usuario, pero el backend debe volver a calcular la distancia antes de devolver esos pedidos.
- Cuando un pedido propio de tipo Delivery pasa a Listo, la notificación push solo se dirige a domiciliarios de la misma sucursal que no tengan pedidos activos asignados y cuya última ubicación reciente de una jornada activa esté dentro de `DeliveryTrackingAllowedDistanceMeters`.
- Ubicaciones, avances, estados diarios, rutas y paradas pertenecen al tenant.
- Cuando admin o cajero asigna un domicilio, el pedido pasa a En camino, queda vinculado al seguimiento y se notifica a la app. Una ruta `Open` o `InProgress` solo recibe la nueva parada si conserva al menos un pedido `OnTheWay`; las rutas mixtas con entregas previas y pedidos todavía en camino siguen siendo compatibles y no reinician su hora de inicio.
- La app conserva en pantalla los pedidos en camino al volver desde segundo plano y reconcilia la ruta de forma silenciosa. Las modificaciones, reasignaciones, entregas o cancelaciones administrativas de un pedido en camino se publican al grupo SignalR de domiciliarios para actualizar únicamente el pedido afectado.
- Entregar el último pedido normalmente no completa la ruta: comienza el regreso a la sucursal y se conserva el seguimiento GPS activo. La ruta se completa al confirmar un regreso GPS válido con la evidencia del backend; ese instante determina el tiempo observado. Una toma siguiente válida también la cierra, con origen inferido y sin atribuirle un regreso físico.
- Una toma válida desde la app cierra transaccionalmente la ruta anterior y sus pendientes propios antes de abrir la nueva. Una asignación administrativa sigue agregando paradas a la ruta compatible y puede finalizar una ruta anterior cuyos pedidos ya sean terminales.
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
- Una interrupción escala a revisión si dura diez minutos o más, o si la evidencia offline cubre al menos siete minutos. La cantidad de ubicaciones sincronizadas no omite el tiempo mínimo. Android conserva como evidencia técnica la confirmación de modo avión, app/servicio detenido, reinicio del dispositivo o Wi-Fi apagado sin otra red, pero esas acciones tampoco omiten los umbrales. GPS apagado y permiso retirado mantienen sus alertas específicas.
- El backend conserva causa y certeza separadas. `internet_lost`, datos móviles indisponibles, teléfono apagado o batería agotada quedan como evidencia técnica o causa no determinable cuando el dispositivo no puede confirmarlos. No molestar no genera alerta.
- Las permanencias no esperadas registran domiciliario, hora inicial/final, duración y coordenada central.
- El correo de auditoría diaria identifica al domiciliario e incluye GPS apagado, permiso de ubicación retirado, interrupciones de comunicación con severidad `requires_review` y permanencias que requieren revisión, con enlaces de Google Maps cuando existe evidencia. Las ubicaciones offline breves y la jornada posterior al cierre permanecen disponibles en la app, pero no entran al correo. Antes de construirlo, el cierre de caja procesa los eventos pendientes para no competir con el trabajador periódico.
- Al crear o escalar una alerta activa de los mismos tipos incluidos en la auditoría (`gps_disabled`, `location_permission_revoked`, `no_communication` con `requires_review` o `unexpected_stay`), el backend envía una sola vez un aviso FCM exclusivamente a los dispositivos registrados del domiciliario afectado. El aviso es informativo, advierte sobre una posible falta disciplinaria, permite omitirlo si existía permiso y remite al administrador; no reemplaza la revisión ni registra una decisión disciplinaria.
- El fallo o la ausencia de FCM no revierte ni altera la alerta administrativa. Solo se intenta notificar al crear una alerta revisable o al escalar una advertencia a revisión; reprocesar el seguimiento no vuelve a enviar avisos por la misma alerta.
- `app_stopped` y el `location_service_restarted` inmediatamente posterior se consolidan en una sola interrupción revisable. Un incidente usa exclusivamente el evento de origen vinculado a su alerta; la evidencia contextual puede compartirse, pero nunca se reutiliza ese origen para crear otra revisión.
- El cierre de la jornada completa la recuperación conocida, pero no resuelve alertas o incidentes pendientes de revisión administrativa.
- Las modificaciones de pedidos se consolidan por edición completa. Una sustitución o conjunto de cambios solo entra como reducción monetaria cuando el total final es menor al inicial; el detalle informa hora Colombia, actor, productos y cantidades afectados. Los pasos intermedios negativos de una edición cuyo total final aumentó o quedó igual no se reportan como merma.
- El identificador transaccional y los productos anterior/nuevo del audit log se habilitan con el script idempotente `SenorArroz.Infrastructure/Scripts/improve_order_monetary_audit_operations.sql`, que debe ejecutarse antes de desplegar el backend; los logs anteriores siguen agrupándose por su marca temporal transaccional.

## WhatsApp Flow comercial central

- La v2 resuelve tenant y canal en backend desde configuración persistida y nunca desde datos editables del Flow.
- El canal central es independiente de la sucursal histórica. Las conversaciones nacen sin sede operativa y el Flow puede asignarla al cotizar domicilio o elegir recogida.
- Admin, Cajero y Superadmin ven la cola central sin sede. Después de la asignación, la conversación es visible para la sede asignada, Superadmin y el asesor asignado aunque pertenezca a otra sede; ningún usuario de sucursal recibe las demás conversaciones de otra sede.
- `OperationalBranchId` define la sede operativa y `AssignedUserId` define quién atiende: tomar una conversación no asigna sede. Al transferir una conversación de una sede operativa a otra, se libera el asesor asignado.
- Admin y Cajero pueden fijar una sede activa desde una conversación central que ya pueden atender. El Cajero que tomó una conversación central sin sede puede continuar únicamente esa conversación y crear su pedido en la sede operativa seleccionada, aunque sea distinta de su sede de usuario. Esta excepción no concede acceso general entre sedes.
- Todo cambio de sede operativa, incluido el asignado automáticamente por el Flow, notifica tanto la cola anterior como la nueva mediante SignalR. El asesor asignado recibe además los eventos por un grupo personal; una transferencia real notifica una última vez al asesor liberado y lo excluye de mensajes posteriores.
- Los comandos normalizados `pedido`, `pedir`, `comprar`, `hacer pedido` y `ver menú` abren el Flow independientemente de la IA y del modo de atención. También funciona durante atención humana o IA pausada, sin cambiar el modo ni el asesor asignado. No se introduce un modo de conversación adicional.
- Un saludo simple (`hola`, `buenas`, `buenos días`, `buenas tardes` o `buenas noches`) responde con una bienvenida y el botón del Flow. Saludos repetidos durante una sesión vigente no duplican la invitación ni reinician el carrito; `pedido` genera otro botón para la misma sesión.
- Cada token del Flow es aleatorio, dura hasta dos horas y solo se persiste como hash. Los botones vigentes comparten una sesión y una única clave de idempotencia. El estado puede guardar una cotización cacheada con su huella, pero nunca se acepta como autoridad al confirmar.
- La ruta comercial es categoría, grupo/receta, variante/tamaño, carrito y luego logística. Beneficios se omite si el storefront no devuelve opciones reales.
- La cotización solo se solicita con carrito y logística completos, se invalida al cambiar carrito, dirección o beneficio y siempre se vuelve a calcular al confirmar.
- Cada confirmación vuelve a validar producto, stock, precio, beneficio, cobertura, sede y total mediante el motor del storefront.
- Una identidad telefónica ambigua no recibe direcciones guardadas y debe pasar a atención humana.
- Fuera de cobertura no se confirma un domicilio. El cliente puede cambiar dirección, elegir recogida o solicitar asesor.
- Efectivo materializa el pedido idempotente. Wompi materializa el pedido únicamente tras una aprobación válida del servicio existente.
- Enlaces, aprobaciones, pedidos y fallos posteriores se envían mediante outbox idempotente.
- Un envío de resultado incierto requiere conciliación; no se repite automáticamente un mensaje que Meta pudo haber entregado.
- `reintentar pago` opera únicamente sobre el checkout propio de la conversación y conserva su vencimiento original de quince minutos.
- Logs y métricas del Flow no incluyen nombres, teléfonos, direcciones, tokens, mensajes ni contenido del carrito.
- Errores comerciales, expiración y concurrencia responden cifrados con HTTP 200 hacia `RECOVERY`; HTTP 421 se reserva para fallos criptográficos.
- `FlowEnabled` permanece apagado hasta validar cifrado, endpoint, allowlist y publicación en Meta.

## WhatsApp y horarios de atención

- La configuración de WhatsApp rechaza identificadores de app y otros valores con formato incorrecto en `AppSecret`; exige la clave secreta hexadecimal de 32 caracteres de Meta. Dejar el campo vacío al editar conserva la clave existente. La verificación del token de Cloud API no certifica la firma del webhook.
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

### Storefront público

- El catálogo público expone el horario semanal real de cada sede activa elegible.
- Las páginas públicas de sedes muestran los siete días del horario y resaltan el día actual en Colombia. Cuando la sede está abierta informan la hora de cierre de hoy; cuando el día está cerrado lo indican explícitamente. El estado no sustituye el detalle semanal.
- Las sedes cerradas siguen siendo visibles, pero solo las sedes abiertas con los siete días válidamente configurados participan en cotizaciones de domicilio o recogida. Al entrar a recogida se consulta el estado operativo sin caché; solo son seleccionables las sedes abiertas con un usuario técnico activo para pedidos web y, si existe una sola, se selecciona automáticamente. Durante los últimos 60 minutos de atención se muestra la hora exacta de cierre.
- La apertura es inclusiva y el cierre exclusivo según la hora local de Colombia. No existe un cierre anticipado basado en el tiempo de preparación.
- Un horario ausente, incompleto, inválido o con todos los días cerrados bloquea los pedidos web para esa sede.
- Todo cliente debe verificar su celular con un OTP de seis dígitos antes de consultar datos privados o confirmar un pedido, exista o no previamente en `customer`.
- El OTP se envía exclusivamente mediante la plantilla aprobada `customers_web_authentication` y la configuración activa y verificada de WhatsApp de `StorefrontCustomerAuth:AuthenticationBranchId`; el código dura 10 minutos, permite máximo cinco intentos, se usa una sola vez y admite reenvío después de 60 segundos.
- Los códigos se generan con RNG criptográfico y solo se persiste su HMAC. Los tokens de sesión se guardan únicamente como hash y el BFF los conserva en una cookie `HttpOnly`, `Secure` y `SameSite=Lax`.
- Después de verificar se buscan clientes activos globalmente por `Phone1` o `Phone2`. Una coincidencia única expone solo nombre y direcciones; una coincidencia ambigua no expone datos y bloquea el checkout hasta corregir el duplicado.
- El cliente existente conserva su `BranchId` histórico. El cliente nuevo se crea únicamente al confirmar el pedido y toma como `BranchId` la sucursal que atenderá la orden.
- Las direcciones pueden tener una etiqueta libre corta y `NeighborhoodId` opcional. Una dirección guardada conserva su `delivery_fee`; Google valida ubicación y cobertura, pero no reemplaza esa tarifa. Las direcciones nuevas usan la tarifa calculada por el storefront.
- El backend revalida productos, precios, disponibilidad, promoción, cobertura y horario al cotizar y nuevamente antes de crear el pedido. Si la sede cerró, bloquea la confirmación e informa la próxima apertura cuando exista.
- La confirmación es idempotente, deriva cliente y dirección de la sesión verificada y calcula los totales en servidor. Efectivo crea el pedido con origen `web`, usuario técnico configurado en `Branch.StorefrontTakenByUserId` y estado `Taken`. Pago en línea crea un `storefront_checkout` temporal y no crea cliente, dirección ni pedido mientras el pago esté pendiente o rechazado. La recogida web se persiste operacionalmente como `Onsite`.
- El panel administrativo identifica los pedidos con origen `web` mediante una insignia visible en listados, detalle, cocina e historial del cliente.
- Los pedidos heredados en `AwaitingPayment` se excluyen de ventas, ticket promedio, productos vendidos, métricas del cliente y bloqueos de cierre de caja.
- El pedido en efectivo y el pedido pagado se notifican a cocina mediante outbox después del commit; los reintentos no duplican el evento operativo.
- Las promociones diarias elegibles se aplican automáticamente en servidor. Si un cliente verificado también tiene un premio de fidelización disponible, el checkout exige escoger uno; nunca acumula ambos. La selección, el descuento y el beneficio quedan congelados en el checkout/pedido. Un premio pendiente queda reservado hasta aprobación, rechazo o expiración para impedir doble uso concurrente.
- El storefront sigue siendo single-tenant en esta etapa; `TenantId` se resuelve desde configuración del servidor y nunca desde el navegador.

### Pago en línea Wompi

- El cliente ve únicamente `Efectivo` y `Pago en línea` con tarjeta o PSE; el nombre Wompi se reserva a la configuración administrativa.
- La integración pertenece a tenant y sucursal, y referencia una App financiera activa cuyo banco pertenece a la misma sucursal. No se usa un ID de App fijo.
- Admin solo configura su sucursal; Superadmin puede configurar la sucursal seleccionada. Cada ambiente conserva llave pública, secreto de integridad y secreto de eventos separados. Los secretos se cifran antes de persistirse y nunca se devuelven.
- Una sucursal solo ofrece pago en línea si la integración, la App y el banco están activos y las tres credenciales del ambiente activo están completas.
- Cada intento usa referencia opaca única, total en centavos calculado por backend, moneda COP, firma SHA-256 y expiración exacta de 15 minutos. Reintentar crea una referencia nueva cuando la anterior ya venció o terminó.
- El webhook firmado es la autoridad. Se valida firma en tiempo constante, ambiente, referencia, monto y moneda; eventos y transacciones son idempotentes. Webhook y sincronización de retorno se serializan por referencia dentro de una transacción PostgreSQL para que una aprobación concurrente produzca un solo pedido, pago y evento de cocina.
- Un webhook con JSON inválido o estructura incompleta se rechaza con `400` sin exponer detalles internos; los fallos inesperados conservan `500` para permitir el reintento del proveedor.
- Antes de abrir Wompi se cierra el carrito. Se usa Web Checkout de página completa y retorna a `/pago/{checkoutId}`; la página sincroniza el `id` de transacción recibido y mantiene webhook y consulta periódica como respaldo.
- Un pago aprobado dentro de la ventana crea atómicamente cliente/dirección si hacen falta, pedido `Taken`, un único `app_payment` bruto con comisión y neto esperado, y la notificación outbox a cocina. Un pago pendiente o rechazado nunca crea pedido ni afecta caja.
- Una aprobación posterior a 15 minutos, o asociada a un pedido que ya no espera pago, queda en revisión manual, alerta a Admin/Superadmin y no entra a cocina automáticamente.
- Cancelaciones, anulaciones y reembolsos de Wompi quedan fuera de esta primera entrega.
- Al eliminar un pedido autorizado se eliminan primero sus líneas auditadas, sus intentos y transacciones Wompi y sus referencias en propuestas temporales de ruta; ninguna de estas relaciones puede bloquear la eliminación del pedido.

## Impresión POS

Reglas:

- Configuración de impresión pertenece a sucursal y tenant.
- Trabajos de impresión pertenecen a sucursal y tenant.
- El agente de impresión debe autenticarse de forma segura.
- El token del agente nunca debe exponerse ni guardarse en repositorio.
- El agente solo debe leer trabajos de su sucursal/tenant.
- No cambiar payload de impresión sin revisar `senorArrozPrintAgent`.

## Planificación de rutas

### Rendimiento de domiciliarios

- El dashboard permite comparar el equipo por sucursal o consultar un domiciliario, incluidos empleados inactivos con historia autorizada. Admin conserva su sucursal; Superadmin permanece dentro del tenant.
- En el rendimiento por domiciliario se incluyen también pedidos de tipo Onsite con domiciliario asignado, como en el historial operativo del empleado. Las entregas Delivery sin domiciliario se conservan en el volumen global y se identifican aparte de los asignados. Un pedido Onsite sin domiciliario no entra en este reporte.
- Únicamente una consulta administrativa con domiciliario individual incluye referencia y serie del equipo para tiempo por ruta, por pedido, minutos/km y SLA. El equipo de referencia excluye al seleccionado dentro del mismo alcance de sucursal, fechas y exclusión de miércoles. Los promedios usan todas las muestras válidas, SLA usa totales evaluables y minutos/km usa minutos y kilómetros emparejados; nunca se promedian promedios de domiciliarios. La consulta personal del domiciliario no expone agregados del equipo.
- La exclusión del seleccionado se aplica a pedidos y rutas antes de resumir el periodo o sus grupos temporales. Si no hay muestras válidas de otros domiciliarios, la métrica de referencia es NULL; no se usa al seleccionado como respaldo.
- Los pedidos entregados y sus series se agrupan por `status_times.delivered`, en calendario Colombia. Los pendientes asignados usan su propio `ontheway`, con respaldo de `delivery_man_assigned` y creación únicamente para ubicar su actividad, sin inventar tiempos de entrega. Se distinguen pedidos asignados, entregados, En camino y otros pendientes; asignados cuenta entregados con domiciliario más todos los pendientes asignados, sin duplicarlos. OnTheWayCount exige estado OnTheWay actual; un pedido que volvió a preparación sigue pendiente y no se etiqueta En camino. Las rutas cerradas se agrupan por regreso observado (`CompletedAtUtc`); las rutas sin regreso, por salida registrada o primer En camino, con respaldo del inicio operativo/asignación. La comparación anterior tiene la misma duración y termina inmediatamente antes del rango actual.
- El historial y sus conteos incluyen rutas no canceladas del periodo, cerradas y abiertas con paradas. Una ruta sin regreso con todos sus pedidos entregados/cancelados y al menos una entrega se identifica como `returnPending`; con pedidos pendientes se identifica como `inProgress`. Incluso un cierre administrativo sin regreso observado conserva la indicación de regreso pendiente. Los contadores exponen total, cerradas, en curso y regreso pendiente. Una ruta sin regreso nunca aporta duración completa, SLA, minutos/km, etapas ni pedidos por ruta cerrada. Sus pedidos entregados sí aportan volumen y tiempo propio, independientemente del estado de la ruta. Si falta el cierre y comienza la siguiente ruta del mismo tenant, sucursal y domiciliario, su inicio completa la anterior. Solo se aplica a rutas con pedidos terminales y al menos uno entregado, sin entregas posteriores a ese inicio; conserva cierres ya registrados. Se permite la misma jornada o el mismo día Colombia, aunque se haya cerrado e iniciado otra sesión de trabajo. Un cambio de sesión dentro del día no impide cerrar la ruta anterior; ese cierre resuelto por la siguiente salida no se invalida por el fin de la sesión anterior. Los cierres originales siguen sujetos a la validación de regresos inconsistentes. Las nuevas asignaciones persisten el cierre y su auditoría; el dashboard resuelve también el historial sin modificar registros, con el mismo resultado en KPIs, gráficas y detalle. La última ruta sin sucesora conserva regreso pendiente.
- El detalle solo expone hora de entrega si el pedido está actualmente entregado. Una marca histórica `delivered` de un pedido que volvió a preparación o En camino no representa su entrega vigente ni se usa para reconstruir paradas entregadas.
- Todas las etiquetas y agrupaciones temporales de domicilios usan `America/Bogota`, incluidas horas, días, semanas y meses. Una entrega del martes por la noche no pertenece al miércoles por su representación UTC.
- El dashboard ofrece excluir miércoles, días de cierre del negocio. El filtro se aplica por fecha Colombia al periodo actual, anterior, KPIs, series y rutas; no modifica registros. La API conserva inclusión de todos los días cuando no se solicita el filtro.
- La asignación que cambia pedidos a En camino registra una salida real para el dashboard. Su duración usa `DepartedAtUtc` y regreso observado o, si falta salida registrada, el primer En camino válido de sus pedidos. El SLA persistido conserva su definición y su muestra evaluable.
- Para el KPI y las series de duración, si falta `DepartedAtUtc`, se usa el primer `status_times.ontheway` válido entre pedidos de la ruta, con entrega posterior o igual y En camino anterior o igual al regreso. Es una salida real, sin persistir fechas derivadas. `AvgRouteMinutes` y `RouteSampleCount` incluyen todas las muestras válidas en una sola serie. El mismo criterio rige periodos, domiciliarios, resto del equipo y rutas recientes. Sin fechas válidas queda NULL; cero es válido. No modifica registros GPS, SLA ni tiempos propios de los pedidos.
- El detalle de paradas expone `OnTheWayAtUtc` de cada pedido desde `status_times.ontheway`, normalizado a UTC y NULL si falta o es inválido. El tiempo por pedido en detalle, marcadores, gráficas y KPIs usa siempre ese En camino hasta su entrega, aunque exista salida registrada de ruta. El tiempo total de la ruta usa la salida registrada o el primer En camino válido hasta el regreso; no sobrescribe `DepartedAtUtc`, GPS ni SLA.
- Cada tiempo por pedido usa exclusivamente `status_times.ontheway → status_times.delivered`, incluso sin ruta asociada y sin sustituir fechas por la salida de una ruta; P90 usa rango más cercano sobre esas mismas muestras. Una fecha ausente o una entrega anterior a En camino se excluye de la muestra de tiempo, conservando el pedido entregado en el volumen. Los tiempos de ruta usan registros propios de la ruta. Se publican las muestras válidas; ausencia de evidencia es NULL, nunca cero ni un promedio ajeno.
- Los regresos fuera de la jornada se marcan inconsistentes y no aportan duración de ruta, minutos/km ni etapas, en periodo actual/anterior, evolución y comparación del equipo. Se valida contra la jornada de la ruta o, en rutas antiguas sin vínculo, la jornada del mismo tenant, sucursal y domiciliario que contiene la salida. El regreso no puede superar el cierre real ni el límite de esa jornada. Sin jornada identificable, salida y cierre deben pertenecer al mismo día Colombia. Una jornada válida puede cruzar medianoche; no se descartan tiempos por un umbral arbitrario de duración. El historial y detalle conservan el cierre original, muestran la inconsistencia y no dibujan un regreso estimado sin confirmar. Los conteos de rutas, pedidos, tiempos propios de pedidos y SLA persistido se conservan; se publica la cantidad excluida y no se corrigen fechas en la base de datos.
- Pedidos por ruta usa pedidos entregados de las mismas rutas completadas del periodo. Las etapas usan la salida registrada o, si falta, el primer En camino válido de la ruta como salida real. Exigen todas las fechas válidas y ordenadas: último pedido listo → salida, salida → última entrega, última entrega → regreso. Cero es válido. No se inventan ni corrigen fechas para completar las etapas.
- Los kilómetros GPS son una estimación entre puntos de la misma ruta, domiciliario y jornada, dentro de salida y regreso. Se exige precisión de hasta 50 m y GPS no reportado como apagado. Se separan huecos mayores que el máximo de 180 s y tres intervalos activos, puntos inválidos y saltos superiores a 120 km/h; no se interpolan esos tramos.
- Minutos/km prioriza GPS con al menos 80 % de cobertura temporal, sin interrupciones detectadas y distancia positiva. Sin GPS utilizable usa `PlannedDistanceMeters + ReturnToBranchMeters`, con ida positiva y regreso conocido no negativo, como estimación. El tiempo se toma de la salida registrada o el primer En camino válido hasta el regreso; sin tiempo válido no hay muestra. `MinutesPerKm` divide suma de minutos / suma de kilómetros elegidos por ruta; `GpsMinutesPerKm` y `EstimatedMinutesPerKm` separan ambos grupos, con `DistanceSampleCount` y `EstimatedDistanceSampleCount`. El mismo criterio se aplica a periodos, evolución y comparación del equipo. No se duplica una ruta ni se promedian ratios; `TotalGpsKm` conserva únicamente kilómetros GPS. Incluye servicio y regreso; no mide velocidad.
- El historial muestra hasta 80 rutas recientes del periodo, paginadas visualmente, incluidas las rutas en curso y con regreso pendiente. El detalle autorizado admite esos estados y conserva el regreso nulo; sus pedidos pendientes se identifican sin tiempos de entrega ficticios. El GPS de una ruta abierta se limita a los puntos observados hasta el momento de consulta, de la misma ruta, domiciliario y jornada; no confirma el regreso ni agrega una duración a los promedios. El detalle respeta tenant/sucursal/propietario y muestra segmentos GPS separados, sucursal y destinos numerados. Si los puntos expiraron según retención, conserva destinos y muestra ausencia de recorrido.

- El cálculo usa una tubería de selección cronológica, refinamiento geográfico y redistribución con un presupuesto global (`SolverTimeLimitMs`). Se conserva la última solución válida. La comida lista se selecciona desde los pedidos más antiguos; antes de saltar uno se intenta redistribuir el conjunto anterior más ese candidato, sin sustituir pedidos antiguos por recientes. Fechas iguales no crean una prioridad artificial por ID. El número de pedido es solo un desempate.
- Para un domicilio normal, la prioridad proviene de `CreatedAt`; para una reserva, de `ReservedFor` menos la meta de entrega, con respaldo en `PrepareAt` y `CreatedAt`. Las propuestas se numeran por el pedido más urgente que contienen. La entrega dentro de cada ruta optimiza recorrido y llegada ponderada por antigüedad, sin imponer FIFO rígido que provoque devolverse. Las reservas penalizan atraso y no admiten retraso adicional si ya estaban puntuales.
- La meta normal es 35 minutos desde salida; el techo de inclusión es 40 minutos. Tras construir el conjunto inicial se fija una sola vez un margen acumulado máximo de cinco minutos por entrega. Refinamiento, inserciones y movimientos entre rutas comparten ese límite; no se reinicia al mover un pedido. La antigüedad decide la prioridad pero no consume el presupuesto de viaje: se distinguen minutos desde toma y minutos desde salida.
- Un viaje directo que supera el límite normal recibe automáticamente un límite individual de viaje directo más diez minutos, con tope técnico de un día. No necesita autorización, columnas de excepción ni una primera falla; se publica su motivo en plan, recarga y vista previa. No amplía los límites de sus compañeros ni desplaza pedidos más antiguos. Si no existe capacidad o compatibilidad, sigue pendiente con un motivo real; no se inventa un domiciliario ni se altera una ruta aceptada.
- La redistribución conserva todos los seleccionados y evalúa completar con pendientes, trasladar, reordenar e intercambiar paradas. Prioriza pendientes antiguos, atraso de reservas y evitar rutas de un solo pedido; después compara cambios de corredor, retrasos, llegada ponderada y ocupación hasta regresar. Dentro de un corredor manda el tiempo, no ordenar los ángulos. Puede liberar una salida al fusionar una ruta individual o dividir un corredor entre varios domiciliarios. Mantiene una ruta individual cuando no hay una alternativa compatible dentro de los límites; no exige una salida por cada domiciliario ni una ruta exclusiva por sector.
- Los destinos a 100 m geodésicos o menos de la sucursal no restringen el rumbo de una salida, pero conservan sus tiempos y servicio. Nunca permiten encadenar otros destinos opuestos. Este radio central no es el radio operativo de 600 m de los domiciliarios. Coordenadas y sectores son una aproximación geométrica: sin una matriz vial no se infieren cruces, autopistas ni sentidos de calles. No se presenta una ruta aproximada como validación vial de Google.
- Los tres paquetes de veinte direcciones se conservan en `scripts/delivery-routing-addresses.json`. El escenario local prepara las mismas veinte órdenes, todas listas, sin reservas y con edades correlativas; reporta coordenadas/direcciones del paquete elegido, tiempos desde salida y pendientes. Se prueba con 3 y 5 domiciliarios, sin crear ni aceptar rutas manualmente.

- Antes de agrupar automáticamente, cada destino se clasifica en ocho sectores de 45° respecto a la sucursal (N, NE, E, SE, S, SO, O, NO). Una propuesta puede abarcar sectores contiguos dentro del arco configurado (60° por defecto, nunca más de 90°); no admite destinos opuestos aunque la distancia o la cantidad hagan rentable juntarlos. La comparación es circular alrededor del norte y se aplica entre todas las paradas, no solo entre paradas consecutivas. Esto no modifica rutas ya aceptadas.
- La optimización conserva primero el conjunto de pedidos seleccionados con las restricciones de capacidad, cocina y espera. Sobre ese conjunto genera corredores dinámicos con centros en destinos reales, considerando ángulo, cercanía y desvío lateral respecto a la sucursal, sin IDs ni barrios predefinidos. El barrido circular empieza después del mayor hueco angular; no impone límites rígidos en los puntos cardinales ni un máximo de paradas.
- La cercanía del agrupador se normaliza por el segundo vecino de cada destino (o el único vecino disponible), con una referencia mínima de 100 metros. Se usa la menor escala local de cada pareja para no absorber un núcleo compacto con pedidos alejados solo por compartir ángulo. Los cálculos de afinidad conservan decimales; el redondeo de costos del solver no decide la pertenencia geográfica. Se comparan hasta ocho inicializaciones distribuidas sobre el barrido circular y se conserva la de menor costo geográfico, evitando depender de un único centro inicial.
- Una segunda búsqueda prioriza evitar cambios de corredor y usa el desvío lateral para distinguir cruces alternativos; dentro de un mismo corredor el orden se optimiza exclusivamente por los costos de tiempo. Los costos geográficos no se suman al tiempo de conducción. La cobertura seleccionada no se reduce en esa búsqueda; si no produce una solución válida dentro del presupuesto, se conserva la primera solución. La matriz aproximada no reconoce cruces, sentidos viales ni barreras; no se afirma compatibilidad vial real sin una matriz de calles.

- Descartar comida lista tiene el doble de penalización base que descartar preparación prevista; la antigüedad sigue sumando prioridad. La meta de entrega continúa siendo un costo suave y se conservan la espera máxima y la separación de preparaciones inciertas.
- La disponibilidad GPS liviana respeta dos intervalos de su cadencia configurada más treinta segundos; el modo de entrega conserva el límite activo más estricto. Precisión, jornada, sucursal y GPS habilitado siguen siendo obligatorios.
- Una historia insuficiente no puede anunciar más del 50% de probabilidad de preparación; los tiempos de respaldo vencidos avanzan en ventanas ancladas a la fase, evitando publicar una nueva versión por cada consulta.
- Un pedido descartado por costo no se presenta como inviable. Las exclusiones conservan su confianza y probabilidad reales; la validación Google desactivada en desarrollo se muestra como métricas aproximadas, no como fallo de red.

- Una propuesta permite quitar, añadir pedidos listos y cambiar el orden únicamente antes de aceptarla. La aceptación confirma la selección en una transacción; el domiciliario no puede editar después la ruta aceptada.
- En el editor móvil/web, la dirección es la identificación principal y barrio/número son secundarios. La selección de añadidos incluye listos sin propuesta y listos de otras propuestas aún disponibles; tomar de otra propuesta y quitar de la selección piden confirmación. Solo la aceptación oficial modifica asignaciones y deja obsoleto el plan anterior; editar una selección no reserva ni transfiere pedidos.
- La vista previa se calcula al abrir y tras cada edición, agrupando cambios rápidos. La aceptación requiere la última respuesta correspondiente al orden y selección vigentes; respuestas anteriores no restauran una estimación ni habilitan el botón. No hay botón permanente de actualizar estimación; solo se ofrece reintentar ante un error. Un plan vencido exige volver a revisar las propuestas actualizadas. Se conservan GPS, jornada y validaciones concurrentes del flujo oficial.
- Las propuestas y su editor distinguen última entrega y regreso a la sucursal. Distancia y duración son acumuladas desde la salida prevista, sin sumar otra vez la espera de cocina; la duración incluye el servicio de todas las paradas, incluida la última. El regreso añade exclusivamente el tramo dirigido desde la última parada hasta la sucursal, no duplica la ida. Se muestran horas locales aproximadas y se señala si el regreso cruza de día. El tramo de regreso se calcula con la matriz disponible y se identifica como aproximado; no exige nuevas consultas a Google ni modifica agrupación, límites o asignaciones.
- La vista previa de una selección editada entrega una huella del conjunto ordenado; aceptar exige la misma versión de plan, pedidos listos sin asignar y la misma huella. Una versión vencida o un conflicto concurrente devuelve `409 ROUTING_PLAN_STALE`.
- La espera por complementos vence en la hora de Listo más `KitchenWaitReferenceSeconds`. Si falta ese timestamp se usa la creación del pedido como referencia conservadora. El recálculo no reinicia el límite; una capacidad futura ya posterior al límite no añade más espera por cocina.
- El solver carga todos los pedidos en sucursal: la salida del vehículo debe ser posterior a la preparación de todos sus pedidos. La comida lista no comparte salida con complementos cuya probabilidad estimada de estar listos pronto sea menor al 60%.
- La estimación de cocina usa historia de la sucursal, fase actual, tiempo transcurrido y coincidencias de cantidad/productos/franja cuando hay muestra suficiente. La falta de historia o una preparación fuera de todo el historial mantiene confianza baja y tiempo restante positivo hasta que cocina confirme Listo.
- Los recálculos mantienen pedidos y orden de rutas aceptadas. Caja y administración conservan sus flujos autorizados, auditados y notificados.
- Para el dashboard, la asignación que cambia los pedidos a En camino registra una salida real. La evidencia GPS de alejamiento se verifica con puntos confiables de la jornada actual; los cierres administrativos no inventan regreso ni cumplimiento SLA.
- Los puntos offline conservan evidencia histórica, pero nunca disparan autoentrega ni cambian relojes operativos de una jornada cerrada. La asociación a ruta exige tenant, sucursal, domiciliario e intervalo compatibles con la jornada.

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

## Gestión de pedidos originados para otra sucursal

`OriginBranchId` registra en el servidor la sucursal de trabajo al crear el pedido; no cambia al trasladar al empleado. `BranchId` conserva preparación, inventario, rutas, impresión, venta y cobro. No se duplica el pedido ni se cambia el usuario/tenant/sucursal global. Pedidos antiguos con origen desconocido permanecen NULL y no aparecen como remotos; no reconstruir el origen usando la sucursal actual de TakenBy.

Admin, Cashier y Superadmin del origen pueden gestionar únicamente esos pedidos, manteniendo las restricciones normales de rol/fecha/estado/proveedor. Kitchen y Deliveryman no reciben acceso remoto. La autorización es por recurso y tenant, antes de escribir; no se amplía BranchContext ni el acceso general a usuarios, bancos, caja, liquidaciones o GPS.

- `GET /api/orders/remote-branches`: destinos con pedidos originados aquí, con sampleOrderId autorizado.
- `POST /api/orders/search` con `remoteOnly: true, branchId: destino`: origen derivado del servidor, filtro anterior a conteo/paginación.
- `GET /api/orders/{id}/operation-options`: bancos normales/apps y domiciliarios activos del destino, sin saldos/perfiles.
- `GET /api/orders/{id}/tracking`: solo ruta y jornada actuales del pedido en camino. No devuelve historial ni usa un punto anterior de otra ruta; identifica GPS sin señal reciente.
- `POST /api/orders/{id}/print`: encola exclusivamente en destino; estado consultable por pedido/jobId.

Mutaciones reutilizan endpoints existentes y reciben `X-Order-Id` + `X-Order-Version` del formulario. Son obligatorios al gestionar desde origen. `409 ORDER_VERSION_CONFLICT` exige cerrar y recargar; nunca reintentar con una versión nueva silenciosamente. Transacción y bloqueo ordenado del pedido en PostgreSQL protegen cambios y pagos; ManagementVersion aumenta también en cambios a hijos. La respuesta expone cabeceras de versión. Se registra actor/comando/sucursal de trabajo/destino/versión en entity_audit_log, además de la auditoría monetaria existente. SignalR invalida únicamente grupos administrativos de origen y destino, aislados por tenant. El frontend refresca la lista visible y conserva el filtro/pestaña; no carga GPS hasta abrir seguimiento.

**Despliegue manual posterior a revisión:** aplicar primero `SenorArroz.Infrastructure/Scripts/add_remote_order_management.sql` con cuenta de mantenimiento; después backend y frontend. SQL aditivo/idempotente, sin backfill, sin migraciones EF. No se ejecuta desde CI ni se modifica producción en esta rama. Cobrar desde origen no traslada dinero entre cajas: el registro pertenece al destino; transferencias físicas entre sedes continúan por los procesos existentes.

## Facturación electrónica

- Los pagos bancarios identificados usan transferencia (47), efectivo puro usa efectivo (10) y los pagos mixtos o de instrumento desconocido usan 1. Los anticipos trasladados a pagos bancarios se cuentan una sola vez para determinar contado/crédito. Este mapeo se aplica al preparar documentos nuevos o rechazados; nunca modifica una factura validada.

- Para el tratamiento sin IVA ni INC, `invoiceBaseTotal` suma las líneas netas y el domicilio; `invoiceTaxExclusiveTotal` es cero al no existir bases tributarias. Los descuentos ya discriminados por línea no se repiten en `allowanceTotal` global. NIT y DV se envían separados; el DV se calcula y valida en backend, sin pedir otro campo al cajero. Los rechazos DIAN muestran sus reglas concretas y un reintento limpia el error anterior, conserva el consecutivo y exige nueva validación antes de enviar correo o habilitar archivos.

- En documentos nuevos y reintentos explícitos de rechazados, los descuentos comerciales se concentran primero en el producto no regalado de mayor importe bruto, con desempate por producto y línea. Si no alcanza, continúan en las líneas siguientes. Se envían precios netos sin porcentajes fraccionarios. Regalos y líneas cuyo neto sea cero se omiten de la factura y de las líneas fiscales del snapshot; permanecen intactos en el pedido y sus líneas operativas históricas. No se inventan precios referenciales ni se modifica el total. El domicilio permanece separado. Se conserva cantidad base cuando el precio unitario exacto no cabe en dos decimales. El snapshot guarda las líneas operativas y fiscales con política net-prices-v2; no cambia pedidos ni documentos aceptados. Un rechazo estructurado de Plemsi por datos inválidos se registra como Rechazada aunque llegue con HTTP 500; un error genérico o timeout conserva Por verificar. Los documentos existentes con ese rechazo se reclasifican tras consultar sin resultado el mismo consecutivo, sin retransmitirlos automáticamente. El reintento explícito reconstruye los datos y conserva el número reservado.

- El historial fiscal autorizado por pedido expone fechas de solicitud/transmisión/validación y el snapshot sin credenciales. La transmisión a Plemsi no prueba recepción DIAN y el correo aceptado por Plemsi no acredita entrega al buzón. Los snapshots identifican pagos bancarios originados por anticipos para evitar contarlos dos veces en su representación.

- Si Plemsi devuelve el último consecutivo nulo, Superadmin puede confirmar expresamente un número disponible tras verificar DIAN y el proveedor anterior. La activación registra actor, fecha y respuesta. No permite reutilizar números conocidos ni omitir rango o vigencia; la numeración debe dedicarse al sistema.

- La indisponibilidad de facturación ofrece continuar con Solo pedido, sin emisión silenciosa. La activación vuelve a consultar Plemsi y valida resolución, prefijo, rango, vigencia y un consecutivo confirmado; no se infiere desde el inicio del rango.
- La emisión real requiere además ElectronicInvoicing__ProductionEnabled=true en el servidor; el valor por defecto es false. El detalle fiscal muestra el destinatario histórico del documento y el reenvío conserva ese correo.

- Configuración y correo empresarial pertenecen al tenant, compartidos por sucursales y administrados por Superadmin. Plemsi es el primer proveedor; emisión real desactivada hasta completar y validar la cuenta.
- Los destinatarios fiscales pertenecen al negocio, se identifican por tipo e identificación normalizada y se pueden asociar a varios clientes. Cada cliente admite varios y un predeterminado. El modal compartido se abre desde el cliente, OrderSidebar y detalle del pedido. Guardar datos no emite facturas ni modifica documentos históricos.
- Sin datos fiscales completos, Enviar pedido conserva el flujo actual; con datos se sugiere y exige confirmación explícita. No hay emisión automática a consumidor final.
- Pedido y solicitud se guardan atómicamente. Las llamadas a Plemsi ocurren después del commit. Una falla fiscal no revierte un pedido creado. Reservas/programados emiten al entregarse.
- Emitida exige validación DIAN y CUFE/CUDE. Un timeout se reconcilia consultando el mismo consecutivo, nunca generando otro documento. El estado del correo es independiente.
- Solo Admin/Superadmin autorizan nota crédito total. La cancelación operativa puede dejar anulación fiscal pendiente. Una factura aceptada necesita nota crédito antes de corregir importes; los documentos impiden eliminar definitivamente el pedido.
- Se conserva tratamiento inicial sin IVA ni INC, importes persistidos y descuentos/domicilio. Los archivos fiscales se guardan privados bajo aislamiento tenant y se descargan con autorización de pedido.
- El correo de facturación del tenant se conserva como Reply-To. La aplicación genera el PDF desde el XML validado y envía un único mensaje con PDF y XML mediante la cola Resend existente. Plemsi siempre recibe send_email=false, incluso para notas crédito y payloads anteriores; no existe reenvío por Plemsi. From usa el nombre fiscal y una dirección verificada del sistema. Los archivos y destinatarios quedan congelados por mensaje. Envíos históricos aceptados por Plemsi no se reenvían automáticamente.
- Detalles de instalación, endpoints y pruebas: docs/ELECTRONIC_INVOICING.md del backend.
### PDF y entrega fiscal propia

- La búsqueda por NIT consulta primero el directorio tenant-owned. Si no existe, consulta DIAN mediante Plemsi y completa únicamente los campos disponibles; la consulta no guarda datos. Al confirmar se registra el destinatario para el negocio y, opcionalmente, su asociación al cliente. Solo para este pedido conserva la selección sin persistir un perfil ni editar el compartido. Nombre comercial y dirección fiscal son opcionales y distintos de la dirección de entrega.
- Los destinatarios guardados se encuentran por nombre comercial, razón social o identificación sin depender del proveedor activo ni del cliente actual. El buscador de clientes encuentra personas asociadas a esa empresa conservando su filtro operativo por sucursal. No se convierte una empresa en cliente ni se comparten datos entre tenants.
- Editar datos compartidos exige confirmación y versión vigente. Solicitar una factura con un perfil compartido comprueba su pertenencia al tenant, versión y datos fiscales; si cambiaron, exige volver a seleccionarlo. La identificación de un perfil existente no cambia; otra identificación requiere otro registro. Dos guardados concurrentes de la misma identificación se consolidan bajo bloqueo transaccional del tenant. Al migrar se mantienen IDs, predeterminados, datos anteriores y snapshots; diferencias de nombre/correo para la misma identificación quedan marcadas para revisión, sin escoger silenciosamente un destinatario de correo.

- El PDF identifica factura/nota crédito, emisor, comprador, fechas, forma y medio de pago por separado, líneas, descuentos históricos, domicilio, moneda y totales del XML. Incluye CUFE/CUDE, resolución, rango, vigencia y QR original en cada página. No modifica XML firmado ni corrige silenciosamente datos aceptados. Conserva el PDF anterior en provider_pdf.
- La entrega usa outbox tenant-owned con clave única, bloqueo transaccional del documento y lease de concurrencia. El estado fiscal es independiente de queued/sent/failed/verifying del correo. Sent indica aceptación de Resend, no entrega al buzón; se conserva su identificador.
- Un timeout de correo reintenta el mismo mensaje y clave dentro de 23 horas. Fuera de esa ventana o agotados intentos ambiguos, queda verifying, bloqueando reenvío hasta verificar externamente. HTTP 4xx definitivos quedan failed. Reenvíos explícitos exigen confirmación y reutilizan cualquier trabajo pendiente; doble clic y reenvíos dentro del minuto posterior al envío se consolidan.
- Descarga de copia histórica puede generar el diseño propio sin emitir ni enviar correo. La generación valida consecutivo, CUFE/CUDE, emisor y total contra el documento persistido; XML con DTD o entidades externas se rechaza.

- La identidad visual del restaurante es tenant-owned y compartida por sucursales y proveedores. Superadmin configura nombre comercial y logo PNG/JPG de hasta 500 KB y 3000 × 3000 píxeles sin desactivar la integración. Se almacena una versión inmutable en electronic_invoice_branding y cada documento conserva su BrandingId; no se duplica el logo en cada snapshot.
- El encabezado de cada página muestra logo proporcionado y nombre comercial como identidad principal; la razón social, NIT y datos fiscales permanecen visibles desde el XML. El correo usa el mismo nombre y logo inline CID, adjuntado dentro del único mensaje. Nuevas solicitudes requieren identidad configurada.
- Las copias previas sin entrega propia en cola pueden adoptar el diseño con logo sin reenviar correo ni cambiar XML. Si existe un outbox propio, el PDF se conserva para no alterar los adjuntos congelados de sus reintentos.
## Almuerzos del personal

- Cada empleado registra salida y regreso con su contraseña personal; las horas oficiales son UTC del servidor. Un intervalo abierto determina el descanso, persiste entre sesiones y no se cierra automáticamente.
- Cocina admite varios empleados activos sin acceso web y cuentas de pantalla compartida con acceso web. Solo las primeras registran almuerzos, desde la pantalla de su sucursal. Se conservan IDs, gastos y nómina; no se reactivan ni convierten cuentas automáticamente.
- Una cuenta personal almorzando no puede realizar mutaciones operativas. Puede consultar, finalizar el almuerzo, mantener seguimiento/dispositivo/jornada y cerrar sesión. Admin/Superadmin no corrigen registros mientras su propio almuerzo esté abierto.
- Un domiciliario no puede iniciar almuerzo con pedidos pendientes ni recibir pedidos mientras almuerza. La disponibilidad y los avisos de pedidos lo excluyen; PostgreSQL serializa almuerzo/asignación por empleado incluso ante propuestas antiguas.
- El almuerzo no cierra la jornada ni suspende GPS: los puntos se conservan y sincronizan. Su intervalo se excluye del análisis de permanencias y no cuenta como silencio de comunicación. Al regresar rigen los requisitos GPS existentes de disponibilidad.
- Historial en el menú Almuerzos, por fecha Colombia y sucursal dentro del tenant. Cajero y Admin consultan su sucursal; Superadmin consulta la sucursal seleccionada. Cocina solo consulta sus empleados personales de hoy desde su pantalla. Solo Admin/Superadmin pueden corregir, con motivo, versión vigente y auditoría antes/después, sin solapamientos ni fechas futuras.
- Script aditivo e idempotente: SenorArroz.Infrastructure/Scripts/add_employee_lunch.sql, antes del backend y las interfaces. Sin cambios automáticos de nómina ni dependencia de facturación electrónica.

- Resend confirma entrega, rebote, demora, fallo, supresión y queja mediante POST /api/webhooks/resend firmado (Svix HMAC-SHA256 y tolerancia de 5 minutos). Se registra el último evento por mensaje en el outbox, sin alterar su estado de envío ni reemitir documentos. Se valida destinatario e identificador; un evento anticipado requiere etiqueta outbox_id y primer intento persistido. El tenant se resuelve desde el mensaje registrado, nunca desde el request. Duplicados, eventos antiguos o regresivos no degradan el resultado. Solo el intento de correo más reciente aparece en la línea de avance. Entregado significa aceptación por el servidor del destinatario, no lectura; un rebote conserva DIAN y PDF. Demora bloquea reenvío mientras Resend siga intentando. Un envío aceptado sin confirmación permanece pendiente de entrega; no se inventan eventos históricos.
- Para correos anteriores al webhook, mantenimiento puede consolidar una confirmación comprobada en el panel autenticado de Resend, contrastando ID del proveedor, tenant, documento y destinatario. El origen se identifica como verified-dashboard:{provider_message_id}; fecha de evento queda nula si no está disponible con precisión. No se simula un webhook ni se reenvía el correo para recuperar estado.

## DELIVERY-2026-10: contexto de rutas y operación offline

- Login y restauración muestran la identidad local mientras verifican jornada y seguimiento. No habilitan tomas sin requisitos operativos. Reabrir consulta la jornada existente; no crea otra automáticamente.
- Una ruta activa por tenant/domiciliario, incluyendo Open e InProgress y la fase de regreso. PostgreSQL impone la unicidad después del preflight. El saneamiento cierra únicamente rutas históricas sustituidas y sin pedidos pendientes/ajenos, conserva un snapshot auditado y no inventa regreso ni duración; ante pendientes inconsistentes revierte todo. Los cierres legacy permanecen administrativos al consultar historia: no se reinfieren regreso, duración ni SLA.
- Regreso GPS válido o una toma siguiente válida completan pendientes propios y cierran la ruta en el núcleo común. La toma valida su selección antes de cerrar la anterior; todo es transaccional. Cancelados, pagos y pedidos reasignados se preservan. Cierre operativo y evidencia física tienen origen separado.
- GET /api/deliverymen/me/active-route devuelve contexto/revisión/última toma y pedidos ordenados. POST /api/deliverymen/me/operations admite únicamente take, deliver y address, con recibo durable por intención/payload.
- Direcciones y entregas pueden registrarse offline, por tenant/usuario, sin afirmar confirmación del servidor. Se conservan tras reinicio y logout. Una entrega local deja de ser próxima entrega y aparece pendiente de sincronizar.
- La corrección de dirección modifica solo campos explícitos; conserva principal/tarifas. Los conflictos guardan la corrección para revisión y no renuevan versiones ni sobrescriben datos silenciosamente.
- ACKs y respuestas antiguas no retroceden el contexto. Los recibos sobreviven al cierre de rutas y no caducan automáticamente. No hay toma, pago ni asignación offline.
- Esquema: add_delivery_operations.sql y enforce_delivery_route_uniqueness.sql. Compatibilidad y build mínimo deben validarse antes de publicación.
- Cerrar sesión sin conexión finaliza el acceso local y conserva los pendientes del propietario. Las solicitudes de limpieza tienen plazo acotado, no renuevan credenciales y quedan ligadas a la sesión capturada; una respuesta tardía no borra ni cierra la sesión o jornada de un acceso posterior.

- El contexto móvil de rutas y su último cierre se limitan a la sucursal autenticada, también tras un nuevo login. El traslado de un usuario exige resolver antes sus rutas activas y pedidos pendientes mediante los flujos existentes; la validación y el cambio se serializan por usuario. No se infieren entregas ni retornos por cambiar de sucursal. OriginBranchId y la gestión autorizada desde origen conservan su semántica.
- Desasignar la última parada retira administrativamente una ruta Open o InProgress vacía como Cancelled, con ClosureSource=empty y auditoría; conserva la ruta y su evidencia, sin duración física ni SLA. La comprobación incluye paradas y pedidos vinculados, dentro de la transacción de la mutación existente.
