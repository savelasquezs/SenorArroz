# Rappi: preparación y pruebas aisladas — 2026-10-21

## Alcance y límites

Revisión sobre backend `main` `925db805526a91a73722f660455d19e33c30ae6e`.
Los cambios de esta rama son locales: no constituyen despliegue, homologación ni
autorización de producción. No se cambian tablas, credenciales, tiendas o webhooks
remotos. No se generan pedidos contra la API publicada.

### DEV del proveedor no significa aislamiento del restaurante

Las tiendas Rappi DEV están asociadas a la conexión de Santander. El procesador
actual no implementa un modo `dry-run` basado en `Environment`: un `NEW_ORDER`
válido crea pedido y pago por app, reserva inventario y notifica cocina. También
puede encolar impresión al crear si la sucursal tiene
`KitchenAutoPrintTrigger=whenOrderCreated`; la afirmación histórica de impresión
exclusivamente al marcar Listo no describe todas las configuraciones actuales.
Cancelación y otros estados pueden revertir pagos, liberar/consumir inventario y
cambiar pedidos reales. No usar esos eventos como una prueba inocua del portal.

Un `PING` firmado solo escribe diagnóstico (evento, última recepción y último
PING); no crea pedidos/pagos. Una firma inválida se rechaza antes de persistir.
`STORE_CONNECTIVITY` sí cambia estado operativo. Publicar menú, stockout,
habilitación de tienda y registrar/rotar webhooks son cambios remotos y se deben
autorizar/aislar por separado.

## Correcciones locales

1. **Inbox interrumpido:** una reclamación nueva `processing_leased` tiene lease de 15 minutos,
   renovada cada minuto por un contexto independiente con tenant explícito. A los
   5 minutos se solicita cancelación cooperativa; si el callback la ignora, la
   renovación continúa hasta que termine. Solo se recuperan reclamaciones
   vencidas de `NEW_ORDER`, `ORDER_EVENT_CANCEL` y `ORDER_OTHER_EVENT`. Menú y
   conectividad interrumpidos quedan `failed` para revisión manual: reproducirlos
   podría aplicar un estado antiguo sobre una publicación/conectividad nueva.
   Los eventos `received` nuevos conservan su flujo habitual. El reclamo es
   condicional y el número de intento protege la escritura final de un trabajador
   tardío. Los cambios pendientes de negocio y la confirmación del inbox se
   guardan en una transacción corta; perder el reclamo revierte esos cambios.
   Repetir handoff/cierre ya aplicados no reescribe la fecha histórica del estado.
   Tras cinco intentos queda `failed`, sin borrarse el evento ni ocultar el error.
   El estado legado `processing` se deja intacto: el worker anterior no actualizaba
   su timestamp al reclamar, por lo que antigüedad no demuestra abandono. Así el
   despliegue gradual no reproduce trabajo de una instancia anterior aún activa.
2. **READY agotado:** se hace explícito el fallo después de diez intentos y se
   añade diagnóstico y reintento autenticado de la misma fila, sin crear un evento
   nuevo. Se valida tenant/sucursal, pedido aceptado y actualmente Listo, conexión
   activa/verificada y capacidad READY habilitada al reclamar/reintentar. Se
   excluyen pedidos cancelados/entregados y capacidades deshabilitadas. El envío
   completo tiene timeout de 5 minutos y lease de 15; CAS por estado/intento/fecha
   evita sobrescribir una reclamación o reencolado concurrente.
3. **Pruebas de entrada:** HMAC sintético, payload alterado, PING repetido y
   encolado idempotente de NEW_ORDER se prueban sobre el controller con DB InMemory
   y proveedores falsos; el worker y el servidor web real no se arrancan.
4. **Órdenes terminales:** ingesta repetida y revalidación no vuelven a aceptar una
   orden sin pedido interno que ya esté cancelada, rechazada, vencida o requiera
   conciliación. Conservan
   estado, payload e historial, sin crear pedidos/pagos ni reservar inventario.

El inbox agotado no recibe en esta rama un endpoint genérico de reenvío. Su estado
`failed` es diagnóstico persistente; una herramienta operativa para revisión de
esos eventos sigue pendiente. No se promete exactly-once frente a componentes
externos que ignoren cancelación ni ante una partición de base de datos que impida
renovar la lease durante más de 15 minutos: las mutaciones deben mantener
idempotencia y deben validarse los cortes/reinicios en un entorno de integración
aislado. La renovación requiere el `IServiceScopeFactory` de DI; los constructores
directos de pruebas que lo omiten no validan trabajo prolongado.

### Despliegue gradual y rollback

No convertir masivamente `processing` a `received` o `processing_leased`. Los
eventos legados requieren confirmar que todas las instancias anteriores terminaron
y revisar el resultado de negocio antes de decidir su recuperación manual. Durante
convivencia ambas versiones reclaman condicionalmente `received`; solo la nueva
versión reconoce y renueva `processing_leased`. Un rollback no procesa ese estado
nuevo: debe terminarse/drenarse el trabajo de la nueva versión o revisarse después,
sin forzar replays. Esta rama no ejecuta esa revisión ni modifica filas remotas.

## Validación segura

Los tests dirigidos deben ejecutarse con entorno limpio, sin User Secrets ni
variables de conexión del despliegue, con `IRappiDeliveryProvider` simulado.

- `RappiWebhookRecoveryTests` / `RappiWebhookRecoveryPostgreSqlTests`.
- `RappiReadyRecoveryTests` / `RappiReadyRecoveryPostgreSqlTests`.
- `RappiWebhookEndpointTests`.
- Regresión existente: `RappiOrderProcessorTests`, `RappiDeliveryProviderTests`,
  `RappiWebhookSignatureTests`, `RappiSettlementTests`, cancelación y cambio de
  estado Rappi.

Las pruebas PostgreSQL usan `RAPPI_POSTGRES_TEST_CONNECTION` exclusivamente en
loopback y crean/borran sus propias bases `rappi_*_test_<guid>`; no inicializan la
base indicada en la conexión. Habilitarlas con
`RUN_POSTGRES_INTEGRATION_TESTS=true`. Sin la variable dedicada, el fixture utiliza
un contenedor efímero de PostgreSQL, nunca una conexión de producción.

### Resultado local verificado

Rama `fix/rappi-readiness-20261021`, sobre el SHA indicado arriba; SDK .NET
9.0.319 y PostgreSQL 17.11 efímeros. Build Release: **0 errores** (advertencias
preexistentes fuera de Rappi). Suite dirigida: **88/88 pasaron, 0 omitidas** el
2026-10-21, incluyendo 63 casos nuevos y 25 de regresión. De esos 88, 37 usan
PostgreSQL local y los restantes no requieren base relacional. Se ejecutaron
solo fixtures sintéticos y proveedores simulados, sin arrancar la API.

```sh
dotnet build SenorArroz.Tests/SenorArroz.Tests.csproj -c Release --no-restore -m:1 -nr:false
RUN_POSTGRES_INTEGRATION_TESTS=true \
  RAPPI_POSTGRES_TEST_CONNECTION='<conexión al PostgreSQL efímero en loopback>' \
  dotnet test SenorArroz.Tests/SenorArroz.Tests.csproj -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Rappi|FullyQualifiedName~CancelOrderHandlerTests|FullyQualifiedName~ChangeOrderStatusHandlerReservationTests'
```

Además, la ejecución amplia pasó **1089 pruebas** (69 omitidas por las puertas
PostgreSQL), y la ejecución ampliada con PostgreSQL pasó **153/153**. Ambas
ejecuciones cubren **1142 casos distintos aprobados**. Permanecen sin ejecutar
26 facts de 15 fixtures que exigen Docker (no disponible en este entorno) y los
3 tests de rate limiting que arrancan workers reales, separados por aislamiento.
No se cambiaron esos módulos ni se omitieron fallos de la funcionalidad Rappi.

Se validaron reclamos concurrentes, renovación por tenant, rollback al perder
propiedad, reencolado concurrente, timeout/shutdown y timestamps de precisión
submicrosegundo, compatibilidad de despliegue gradual y preservación de estados
terminales. La revisión independiente no dejó hallazgos bloqueantes. No se
ejecutó toda la suite del repositorio ni se certificó el proveedor. CI histórico
no sustituye estas pruebas locales ni la homologación Rappi.

## Evidencia del portal revisada sin pedidos nuevos

El historial existente muestra para DEV1 `1445166944` las transiciones SENT,
TAKEN y READY_FOR_PICKUP del 14 de agosto. Para DEV2 `937215368` muestra SENT y
TAKEN, sin timestamp READY. No se vieron eventos detallados para ninguno. Esto
completa evidencia histórica, no acredita el estado operativo actual.

El verificador HMAC client-side aceptó el vector sintético (sin secreto real):
`t=1792594800000`, secret de prueba `rappi-readiness-test-only`, payload exacto
`{"store_id":"audit-local"}` y firma hexadecimal
`35df645b382f2b38d85e93c08b2ac77326e5dbe281e738d259a1d22cb6164f1f`.
Agregar un espacio antes de `}` con la misma firma fue rechazado. No se envió
ningún webhook mediante esta prueba.

## Pendientes explícitos

- Confirmar contrato real y semántica de `GET orders/{id}/events` antes de
  implementar reconciliación de cancelaciones/handoff/cierre omitidos. No se
  agrega un camino especulativo o deshabilitado que aparente resolverlo.
- E2E aislado de Growth con aportes no cero, disponibilidad off/on, cancelación
  del cliente/soporte, cierre y reinicios, en ambas tiendas.
- Aclarar discrepancia de homologación/webhooks, habilitación y cuáles
  funcionalidades solo pueden certificarse en producción.
- Configuración de tiendas productivas por sucursal y autorización expresa de
  Rappi. La UI actual sigue centrada en Dev1/Dev2.

## Matriz de cierre antes de solicitar homologación

| Área | Evidencia local disponible | Evidencia que falta del proveedor |
| --- | --- | --- |
| Inyección | Pedido/pago único ante duplicado, reserva previa a aceptación, rechazo por precio y stock; entrada HTTP solo encola | E2E actual en las dos tiendas, sin inventario/caja/impresión reales |
| Growth | Fixture con aportes no cero de Rappi y comercio conserva totales y pago | Caso real de campaña con ambos aportes; descuentos en cero no lo acreditan |
| Menú/mapeo | Contrato HTTP simulado, SKU estable y validación de precio | Publicación/aprobación actual del padre e herencia del hijo; combos/modificadores requieren alcance explícito |
| Disponibilidad | Contrato off/on simulado y lógica de sincronización existente | Off/on visible en tienda, sin alterar productos de operación |
| Health check | PING local idempotente y HMAC sintético verificado en portal | Recepción real aislada y diagnóstico de integración/selector del portal |
| Estados obligatorios | Cancelación repetida/reversa y conciliación local; recuperación inbox y READY de esta rama | Cancelación cliente/soporte, handoff/cierre, reinicios y aceptación formal; contrato `/events` |

### Aislamiento requerido para el E2E pendiente

Usar una API de pruebas con base dedicada (datos sintéticos), sin colas/agentes de
impresión, sin caja/pagos reales y sin notificaciones a cocina/repartidores de
operación. La conexión, sucursal, app financiera y usuarios deben ser de prueba.
El callback Rappi DEV debe apuntar **solo** a esa API; registrar/cambiarlo es una
acción remota separada que aún no se ejecutó. Conservar IDs de pedido, payload
redactado, transiciones y resultado esperado por cada caso. No resolver el
aislamiento usando únicamente `Environment=sandbox` en la conexión actual.

La recuperación del inbox no reemplaza un outbox de efectos secundarios: una
cancelación guardada antes de una caída puede dejar pendiente su notificación o
finalización de ruta, y el replay actual retorna temprano al encontrarla ya
cancelada. Este límite preexistente debe cubrirse en pruebas de reinicio antes de
afirmar recuperación integral. READY tampoco garantiza exactly-once remoto si
Rappi aceptó la solicitud y el proceso cayó antes de guardar su confirmación.
El nuevo diagnóstico/reintento es API; no se añadió un botón al frontend.
