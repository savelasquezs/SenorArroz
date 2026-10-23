# Rappi: cierre de preparación interna — 2026-10-21

## Alcance

Continuación de PR #52 sobre `main` `e2d52f39af80aca5e3a4c6f3cff944ec9af81668`.
Es una corrección de software y cobertura aislada, **no homologación, activación
productiva ni prueba contra Santander**. No cambia esquemas, dependencias,
credenciales, webhooks remotos ni política de asignación manual de domiciliarios.
El procedimiento del harness está en [RAPPI_ISOLATED_TESTING.md](RAPPI_ISOLATED_TESTING.md).

## Fallos que se cierran

### Cancelación y orden de eventos

- La reversa del pago, liberación de reserva y cambio del pedido/orden externa se
  confirman en una sola transacción. La cancelación usa el mismo bloqueo de pedido
  que `OrderMutationBehavior` y recarga pedido/pagos después de adquirirlo. Así una
  liquidación concurrente se vuelve a evaluar antes de liberar stock o reversar el
  pago; el control optimista de `ManagementVersion` por sí solo llegaba demasiado
  tarde para revertir el `SaveChanges` interno de inventario.
- `CANCEL` anterior a `NEW_ORDER` conserva un marcador terminal en la entidad
  externa existente, sin crear pedido/pago/reserva. La aceptación y sus escrituras
  de error no pueden reabrir una cancelación concurrente.
- `close_order` asegura consumo idempotente antes de marcar entregado, incluso si
  se perdió o llegó después el handoff. Repetir/reordenar esos eventos no duplica
  consumo ni reescribe la fecha histórica del estado.
- Los efectos de rutas y avisos se ejecutan después del commit y conservan su
  contexto en la misma fila de inbox. No se introduce un outbox genérico: el
  checkpoint interno guarda estado anterior, visibilidad en cocina y ruta original.
  Finalizar una ruta puede consultar Google; ni esa operación ni SignalR quedan
  dentro de la transacción financiera. El evento interno `LOCAL_RAPPI_CANCEL_EFFECTS`
  conserva la clave/hash originales y usa `cancel_effects_pending` /
  `cancel_effects_leased`, invisibles para el worker anterior. No se acepta este
  tipo de evento en HTTP. Un rollback deja esa fase pendiente hasta volver a una
  versión que la entienda; nunca convertirla manualmente a `received`.

### Catálogo y disponibilidad

- Publicar reserva primero una única publicación pendiente por tienda; otro
  publicador concurrente no envía un segundo menú. Un resultado remoto incierto
  mantiene el bloqueo y un error visible; no se interpreta como rechazo seguro.
- Las consultas locales de aprobación llevan la identidad de su publicación. Un
  callback sin correlación documentada no aplica un estado a la publicación más
  reciente por suposición. Varias publicaciones históricas pendientes requieren
  revisión, no selección arbitraria de la última. Un rechazo sin correlación pone
  las publicaciones pendientes en `approval_review_required`: no se declara cuál
  fue rechazada ni se permite que un poll tardío borre la incidencia. Es una retención
  conservadora que también puede activarse con un rechazo antiguo retrasado.
  El estado de publicación es token de concurrencia EF (sin columna nueva): una
  respuesta o poll obsoleto revierte sus snapshots/diagnósticos, no pisa la revisión.
- La configuración valida la colección final de tiendas y sus identificadores
  normalizados, conserva filas históricas y rechaza padres/identidades ambiguas.
  Un bloqueo transaccional breve por tenant serializa la configuración entre
  sucursales; cambiar identidades de ruteo exige verificar de nuevo la conexión,
  mientras editar nombres/opciones no invalida innecesariamente la verificación.
- La cola de disponibilidad verifica conexión activa/verificada, selección del
  producto, sucursal activa y pertenencia de tenant/conexión antes de enviar. No
  revive una sincronización pendiente de una integración deshabilitada.
- Growth conserva importes autoritativos y financiadores separados. Las pruebas
  usan el contrato de producción: `items.price` y `total_products` son brutos;
  **no** se relaja validación de precio para acomodar diferencias del tester DEV.

### Firma y salud por tienda

- La firma se analiza una sola vez. El timestamp autenticado es el utilizado en la
  clave idempotente. Campos `t`/`sign` duplicados, vacíos o múltiples cabeceras se
  rechazan; no pueden elegir un timestamp distinto para la deduplicación.
- La normalización heredada del tester queda limitada a `Environment=sandbox`.
  Una conexión configurada como `production` exige el payload crudo exacto. Al
  activar producción debe verificarse expresamente ese campo persistido: el host
  o las credenciales no lo cambian automáticamente y Upsert crea `sandbox`.
  No se inventa una ventana temporal
  que la documentación de Rappi no especifica.
- PING exige una tienda identificada sin ambigüedad en esa conexión y un
  tenant/sucursal locales activos, incluso al repetir un PING antes exitoso.
  `ConnectivityEnabled` no es condición de salud local, para no impedir una
  reconexión del POS. Un PING no llama al proveedor ni crea pedidos/pagos/impresión.

## Evidencia y límites de pruebas

La auditoría previa caracterizó 18 casos aislados, incluidos controles; eso no
significa 18 defectos. Las regresiones nuevas cubren los mecanismos de fallo, no
solo las respuestas felices. PostgreSQL usa bases efímeras de datos sintéticos en
loopback, servicios reales de inventario/repositorio/impresión y transportes falsos.
El slice HTTP usa TestServer explícito sin arrancar `Program`, workers ajenos,
configuración de despliegue ni clientes externos.

Resultado local del árbol final: build Release **0 errores**; **221/221** casos
dirigidos aprobados, sin omisiones (incluye 15 del harness de negocio/HTTP, 11 de
cancelación/ciclo de vida y 55 de catálogo). La ejecución amplia aprobó **1194**
casos y omitió **102** por las puertas PostgreSQL; se excluyeron además los 3 tests
que arrancan el host completo de rate limiting. La unión de las dos ejecuciones
cubre **1263 casos distintos aprobados** (identidad `testId` del TRX). Se ejecutaron también **6/6** pruebas Python
del exporter de routing. Las omisiones locales no se presentan como aprobadas;
la CI completa debe verificarse sobre el mismo árbol antes de considerar merge.

Estas pruebas verifican transacciones y filtros EF; no certifican RLS productivo,
JWT real, temporización de Rappi, impresoras físicas ni recepción del proveedor.
Los fixtures RLS y el resto de la suite del repositorio siguen siendo verificaciones
separadas. Una suite verde anterior a estos casos no cubría estos fallos.

## Límites que no deben ocultarse

- Rutas/avisos postcommit son **at-least-once**: una caída tras el envío y antes de
  confirmar inbox puede repetir un aviso. No se promete exactly-once de SignalR,
  Google ni de solicitudes Rappi (incluido READY aceptado antes de guardar resultado).
- Las cancelaciones legadas ya confirmadas sin snapshot de efectos no se barren ni
  se reproducen automáticamente. Los eventos legacy `processing` siguen reservados
  para revisión manual, como en PR #52.
- La asignación manual de un domiciliario interno a Rappi permanece sin cambios.
  Debe confirmarse si es una excepción operativa permitida; no se introduce una
  nueva regla de negocio de forma silenciosa.
- No se implementa reconciliación histórica ilimitada de `/events`. Ya está
  documentada su respuesta raíz array con `event`, `event_time` y
  `additional_information`, pero retención, orden y política de recuperación
  requieren alcance propio. Los callbacks de menú no tienen correlador publicado.
- Durante un despliegue futuro no publicar/reconfigurar menús con instancias
  antiguas todavía sirviendo esas operaciones: su modelo no conoce el nuevo token
  de concurrencia. La protección no puede retroactivamente corregir código viejo.
- Menú enviado con resultado incierto y publicaciones históricas ambiguas necesitan
  revisión operacional antes de otro envío; no se agrega una vía automática de
  desbloqueo que pueda duplicar publicaciones.
- DEV está enlazado a una sucursal real y `Environment=sandbox` **no es dry-run**.
  Un pedido puede tocar inventario, pagos, cocina e impresión según configuración.
  No usar la API publicada como destino de pruebas de negocio.

## Matriz de preparación

| Área del Sprint 1 | Implementación y prueba interna de esta revisión | Pendiente externo/operativo |
| --- | --- | --- |
| Inyección | Slice HTTP firmado → inbox → servicios reales PostgreSQL, pago/reserva/impresión únicos y dos sucursales/tenants sintéticos | DEV conectado a API/base aisladas y aceptación de ambas tiendas |
| Growth | Fixtures de producción con financiadores no cero, importes brutos y preservación de totales; regresiones de liquidación | Campaña real, cupón/fees y confirmación de conciliación con Rappi |
| Menú/mapeo | Padre único, IDs sin ambigüedad, concurrencia de publicación, correlación local de poll y retención conservadora | Semántica/correlador de aprobación-rechazo y herencia del hijo visibles en Rappi |
| Disponibilidad | Consulta PostgreSQL y actualización de cola; on/off simulado en padre/hijo, exclusión al deshabilitar | Cambio off/on visible en tienda aislada |
| Health/HMAC | Firma cruda estricta fuera de sandbox, deduplicación sin ambigüedad, PING por tienda y tenant/sucursal activos | Recepción real y latencia exigida por homologador |
| Estados obligatorios | Cancelación atómica/ordenada, cierre con consumo, recuperación de efectos y READY local | Cancelación cliente/soporte, handoff/cierre y READY explícito certificados |

## Cierre pendiente con Rappi

La prueba real debe usar API/base/sucursales de prueba y transportes operativos
separados. Solo después se configura el callback DEV de forma autorizada y se
valida: inyección en ambas tiendas, Growth real no cero, menú padre/herencia,
stock off/on, PING, cancelación cliente/soporte, handoff/cierre y READY explícito
cuando corresponda. READY puede ser automático por defecto: el historial de una
orden no demuestra que el POST salió de este POS. Aún falta aceptación formal de
homologación y configuración productiva por sucursal.

## Contratos oficiales consultados

- [Órdenes y eventos](https://dev-portal.rappi.com/en/api-reference/orders/).
- [Ciclo de vida](https://dev-portal.rappi.com/en/managing-user-orders/).
- [Diferencias sandbox/producción](https://dev-portal.rappi.com/en/api-reference/sandbox/).
- [Menús](https://dev-portal.rappi.com/en/managing-store-menus/) y
  [consulta de aprobación](https://dev-portal.rappi.com/en/api-reference/menus/#get-menu-approved-store-id).
- [Firma de webhooks](https://dev-portal.rappi.com/webhook-events/) y
  [salud por tienda](https://dev-portal.rappi.com/en/managing-availability/).

## Continuación con la plataforma de integración (18:23 UTC)

Se aprovecharon el validador de menú del portal DEV y su verificador HMAC con
entradas sintéticas, sin enviar menú, generar pedidos ni configurar callbacks.
El validador de menú puede comprobar imágenes: no se presenta como completamente
local. Rechazó menú vacío, precio negativo, SKU duplicado y URL con espacios
literales; aceptó la estructura mínima y `%20`, este último con advertencia de
accesibilidad/CORS. Validar no equivale a publicar, aprobar ni homologar.

- El preflight del menú ahora rechaza whitespace literal en imágenes (también una
  imagen formada solo por espacios) antes del envío; admite ausencia y `%20`.
  Nuevas regresiones usan BuildMenu real: solo padre, precio completo, sin rebajar
  catálogo por Growth. Verifican fees/tip sin sumarlos al pago autoritativo y
  mantienen toppings fuera del alcance simple, retenidos sin pedido/pago.
- El vector sintético comprobado en el HMAC 100% client-side del portal se reproduce
  como regresión local: la firma de JSON con number/bool/null debe corresponder a
  esos bytes, no a una conversión a strings. No elimina la compatibilidad histórica
  explícita del tester sandbox ni acredita un webhook real.
- `/events` ahora usa el array oficial de objetos tipados, nombre exacto y fecha
  UTC; rechaza wrappers, claves duplicadas y fechas inválidas. Conserva información
  adicional sin interpretarla como estado. El ejemplo público de cancelación
  posterior a aceptación reprodujo una falsa aceptación local con el código previo.
- Tras un PUT fallido ya no se buscan palabras en JSON ni se interpreta 400 como
  expiración. Solo historial coherente de TAKEN (y reasignación posterior con TAKEN)
  permite recuperar aceptación; cancelación, avance, evento desconocido, fecha futura
  o empate conflictivo quedan en `SyncError` con diagnóstico, sin crear Taken.
- Cada intento previo queda marcado con `LastAttemptAt` existente. En revalidación
  o NEW repetido se vuelve a consultar el historial **antes** de reservar/crear o
  repetir Accept. Un historial incierto/avanzado/terminal no dispara otro PUT; uno
  vacío permite intentar, pero solo 200/TAKEN inequívoco confirma el negocio. Una
  consulta coherente posterior puede recuperarse por la ruta existente, sin estado
  permanentemente bloqueado ni endpoint nuevo. Los errores previos sin marca se
  protegen también. El timestamp se actualiza para no monopolizar la selección de
  reintentos existente. Fallos ordinarios de transporte/OAuth/timeout también
  conservan el marcador tras rollback. Una caída del proceso o cancelación del host
  antes de persistir el fallo sigue siendo una ventana preexistente: no se promete
  detectar todo PUT remoto confirmado antes de un crash. Esto no agrega barrido/replay
  ni reconciliación histórica.

Evidencia pública: [eventos y errores de aceptación](https://dev-portal.rappi.com/en/api-reference/orders/),
[ciclo de vida](https://dev-portal.rappi.com/en/managing-user-orders/),
[validaciones de menú](https://dev-portal.rappi.com/en/managing-store-menus/).
Las pruebas contra el portal son estructurales con datos sintéticos, no aprobación
operativa de tiendas ni confirmación del estado real de una orden.
