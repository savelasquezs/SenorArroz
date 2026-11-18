# WhatsApp: etapa 6, validación local

Fecha: 2026-11-05. Trabajo en feat/whatsapp-worker-stage01. Etapa 6 cerrada técnicamente en local: matriz funcional, presupuestos UI y regresión aprobados. Sin push, despliegue, SQL productivo ni mensajes reales. La aceptación real de coexistencia Meta/celular y el piloto permanecen en etapas 2/7.

## Implementación

- GET /api/whatsapp/conversations/page devuelve items, nextCursor y hasMore. Límite predeterminado 50, máximo 100. Permisos y filtros preceden el límite. Orden: requieren acción/borrador en progreso, esperando cliente, gestionados; actividad descendente e identificador descendente.
- El cursor pertenece a tenant, actor, rol, contexto de trabajo y filtros. Un cambio exige reiniciar la lista. No es una instantánea: eventos concurrentes se concilian al reconectar o actualizar. La segunda proyección omite elementos que dejaron de estar autorizados entre consultas.
- Dos consultas por página: claves y metadatos comerciales; proyección DTO autorizada. No se cargan tokens, estado IA completo ni historial. La resolución de ruta para notificaciones usa una sola consulta.
- Índices aditivos/idempotentes en add_whatsapp_inbox_pagination.sql: última sesión comercial, historial y último motivo de transferencia humana. No aplicados fuera de bases desechables.
- Frontend: ventana de 300 conversaciones, 12 historiales y 500 mensajes por historial, conversación activa retenida. Aborta lecturas obsoletas, deduplica peticiones y limita recuperación incremental a diez páginas. El historial antiguo conserva ancla y ofrece regresar a recientes sin huecos.
- Una conexión compartida por hub existente; reconciliación al reconectar y fallback condicionado por actividad/visibilidad. Se conserva el proveedor multimedia; imágenes diferidas con dimensiones reservadas y audio/video sin precarga.
- Reutiliza OrderSidebar, AddProductModal, componentes de cliente/dirección/pagos y BaseDialog. El ancho incrustado se adapta al panel sin modificar el ancho del POS. Las claves usan generatePosCreationKey existente. Borradores limpios desmontados se liberan; cambios sin guardar o en conflicto se retienen para no perder trabajo.

## Evidencia nueva

### Optimización posterior de interfaz

- Se reutilizan hasta ocho paneles del editor mediante KeepAlive. La caché y los stores quedan separados por usuario, rol, sucursal de trabajo y conversación; el destino de preparación sigue perteneciendo al pedido. Los borradores limpios expulsados se liberan y los cambios pendientes se conservan.
- Primera conversación y primera lectura comercial autorizada en paralelo a bandeja/estado; cliente y contexto operativo hidratados en paralelo. Se elimina el flujo antiguo de contexto que repetía consultas ya resueltas por OrderSidebar. El catálogo no se solicita si la edición persistida ya contiene nombres de productos.
- Activar un editor consultado hace menos de cinco segundos no reconstruye el pedido. Eventos y reconexión fuerzan conciliación; una actualización recibida durante otra lectura agenda la siguiente. Adquirir responsabilidad bloquea edición y confirmación hasta obtener la versión compartida. Las respuestas tardías conservan las ediciones locales.
- Las filas de conversación reutilizan el renderizado mientras sus campos, selección, agrupación y minuto permanecen iguales. Ruta y consulta de asesores se agrupan con el composable de debounce existente. Los formateadores numéricos y monetarios también se reutilizan.
- Horarios de local/domicilio usan BaseInput nativo de hora con los helpers y validaciones existentes. El calendario compartido de reservas, formularios de cliente/dirección, destinatario fiscal, selector de productos, detalle de cliente/pedido y diagnóstico IA se importan bajo demanda. El detalle del cliente y el selector de productos se montan solo cuando están abiertos. BaseDialog libera overlay, foco y bloqueo de scroll al desactivar un editor cacheado.
- La consulta de estado deja de bloquear la pantalla al completarse; mensajes, bandeja y editor continúan cargando en paralelo. Una selección manual más reciente invalida la resolución tardía del enlace inicial. El paquete inicial del harness pasó de 647.078 a 559.010 bytes antes de compresión; no equivale al chunk individual de la ruta en la aplicación completa.
- Pruebas nuevas cubren caché acotada, contexto de trabajo separado del destino, lectura anticipada, actualización consecutiva durante otra consulta, adquisición de responsabilidad, respuesta tardía y ciclo de vida de los modales. La revisión de navegador también comprueba reserva, sugerencia de entrega y persistencia de horarios en preparación remota.
- Regresión final después de todos los cambios: npx vitest run --maxWorkers=4, 1.032 pruebas aprobadas en 163 archivos, 479,91 s. Regresión afectada previa: 69 aprobadas en siete archivos. npm run build con vue-tsc/Vite aprobado. Las suites se solapan. Logs locales: .work-stage6-closed-full-tests.log y .work-stage6-closed-build.log en el frontend. No hubo cambios de código backend durante esta optimización; se conserva su aceptación PostgreSQL/HTTP previa.
- Recorrido final y nueva captura estable: enlace inicial retenido hasta elegir otro chat sin reemplazarlo, preview de bandeja actualizado después del envío y horarios nativos/calendario compartido operativos. Dos asesores, recuperación de red, destino remoto y un pedido origen 1/preparación 2 conservados; cero pageerror. La repetición para capturas se hizo durante la regresión y no se usa para aceptar tiempos.

### Validaciones de las etapas anteriores, conservadas

- General backend: dotnet test SenorArroz.Tests --no-restore --filter 'Category!=PostgreSqlIntegration': 1.450 aprobadas, 107 omitidas.
- Integración PostgreSQL completa sin el perfil de rendimiento: 31 aprobadas. Aceptación final de paginación, script de índices ejecutado dos veces, permisos HTTP y reglas de sucursal: 35 aprobadas; incluye escenarios ya contados. Compilación API/worker aprobada.
- Frontend completo: npx vitest run --maxWorkers=2, 163 archivos y 1.021 pruebas aprobadas. Recuperación de guardado y límites de caché finales: 12 aprobadas en dos archivos. Build con vue-tsc y Vite aprobado tras conservar scroll al cambiar tamaño/panel. No se suman suites solapadas.
- Tras los ajustes finales: 36 pruebas aprobadas en cinco archivos de formato, caché, borrador compartido y OrderSidebar incrustado. Se reutiliza el formateador de mensajes en useFormatting; el perfil de CPU identificó como coste repetido crear Intl.DateTimeFormat por mensaje. El detalle del pedido existente se importa al abrir su modal. La selección reutiliza mensajes consultados hace menos de 15 segundos; no leídos, datos vencidos y reconciliación siguen consultando al servidor.
- PostgreSQL 17 desechable en loopback: paginación de 262 conversaciones, pendiente antiguo visible primero, seis páginas sin duplicados, dos consultas por página sin secretos; rechazo de cursor ajeno, límite inválido y sucursal no autorizada.
- HTTP real mediante ASP.NET TestServer + controladores MVC + PostgreSQL: 401 anónimo, 403 rol/sucursal, 404 tenant ajeno, 409 antiguo responsable. Dos confirmaciones concurrentes reutilizan un pedido y un outbox: origen 1, preparación 2. El host usa autenticación sintética conocida; no acredita configuración JWT productiva.
- Navegadores Chromium aislados con componentes Vue reales y adaptadores BaseApi/SSE sintéticos: dos asesores, lectura sin propiedad, transferencia, antiguo responsable bloqueado, pérdida de red/reintento, aislamiento del mensaje por chat y destino remoto preservado. Un pedido sintético origen Santander/preparación Manrique. Cero pageerror.
- Capturas finales 390×844, 768×1024 y 1440×1000 inspeccionadas: sin desborde horizontal, confirmación accesible, revisión previa y sin error antiguo tras reintentar. Evidencia frontend: test-results/whatsapp-stage6/.
- Recorrido final ampliado: respuesta HTTP de envío retenida hasta cambiar de chat; al llegar no borra el texto de la otra conversación. HTTP y evento producen una sola burbuja. Historial sintético de 650 mensajes, ventana máxima de 500 y doce cargas con desplazamiento del ancla de 0 px. Regresar a recientes recupera el último mensaje; reentregar un evento deja una burbuja, un aviso y conserva el scroll. Cero pageerror. Se corrigió el falso aviso de nuevos mensajes al recortar el extremo reciente durante la carga de historial antiguo.

## Carga y límites

Perfil durable: 240 eventos, 32 conversaciones, dos tenants, ocho productores, cuatro consumidores y handler simulado de 10 ms; no Meta ni LLM. El perfil HTTP utiliza el handler POS existente sobre 100 pedidos sintéticos y 60 muestras por ruta. CPU/RAM corresponden al proceso de pruebas; conexiones y esperas SQL son muestras, no máximos continuos.

La ejecución final aislada y calentada, con el reloj ajustado antes de producir, dio POS p95 165,19 ms de base y 127,99 ms bajo carga; chat 91,59 ms, ingreso 45,17 ms, diez conexiones, cero esperas de bloqueo y edad de cola muestreada de 0,663 s. CPU del proceso: 5.218,75 ms; working set: 245.575.680 bytes; drenaje: 2,198 s. Presupuesto técnico aplicado: base + mayor de 50 ms o 25%; no es SLA acordado. La primera ejecución sin calentamiento falló: 28,76 → 185,49 ms y se conserva en http-load-cold-start-failed.json. Los valores menores bajo carga reflejan variabilidad del equipo compartido; no acreditan mejora ni capacidad productiva. El perfil intermedio tenía desfase del reloj fijo y no se usa para acreditar edad de cola.

La medición inicial de navegador, simultánea con otras suites, dio 7.863/1.849 ms. Además el harness servía el build con middleware de desarrollo: transformaba aproximadamente 1 MB compilado en una respuesta de 6 MB. Se cambió a Vite preview estático con compresión, sin cambiar el producto para compensar ese artefacto de prueba. El resultado inicial se conserva en browser-profile-initial.json.

Antes de esta optimización, el perfil dio primera apertura p95 2.115 ms en diez contextos nuevos con borrador remoto activo y cambio cacheado p95 298,7 ms en 30 cambios. Se conserva en browser-profile-before-speed.json. Los intentos intermedios también permanecen: el primero dio 1.188,6/166,9 ms; tras memoizar filas una serie de diez aperturas tuvo un máximo de 1.515,6 ms. La ampliación conservando las cuarenta muestras dio 1.393,3/85 ms. La comprobación posterior de los guards registró picos de apertura y falló con 1.709,3/99 ms; no se descartó para declarar aceptación.

Después de diferir los formularios/modales y permitir carga paralela de la pantalla, el build final dio primera apertura p95 1.480,8 ms en treinta contextos nuevos y cambio cacheado p95 86,8 ms en treinta cambios. Ambos cumplen los presupuestos locales propuestos de 1.500/150 ms. Se conserva la única apertura de 1.592,6 ms: no todas las muestras cumplen el umbral. La primera apertura exige cincuenta filas, mensajes, ambos campos de hora y confirmación habilitada, más dos frames; el cambio exige contenido del chat y editor, más dos frames. Los relojes se toman dentro de la página y excluyen latencia de los comandos Playwright. No hubo tests/build de esta tarea durante el perfil; se observaron pruebas de otro checkout Next.js en el mismo equipo, sin detenerlas ni alterar sus procesos. El entorno Chromium Docker/equipo compartido y el margen pequeño de apertura impiden convertir este resultado en SLA productivo.

No se agregó virtualización: una prueba anterior de diferir layout alteró el ancla del historial y se retiró. Se conserva el renderizado acotado correcto. Las capturas finales desactivan animaciones al tomarse para evitar guardar una transición de apertura a medio completar; esto no afecta la medición de rendimiento.

El controlador de navegador visible falló al iniciar. El usuario autorizó continuar por los medios disponibles; se completó la revisión automatizada y se inspeccionaron las capturas finales. No se afirmó operar la ventana del navegador integrado del usuario. La vista permanece disponible con datos sintéticos en el puerto 4179.

## Copia del laboratorio y pruebas persistidas — 2026-11-05

Se creó un respaldo pg_dump custom de `senor_arroz_whatsapp_dev` (13.237.285 bytes), se verificó SHA256 `CC25E022E454C6004C0005529F13E3393D4328239E1ECA5AFF46A576FE160AEB` y se restauró en `senor_arroz_coexistence_local`, contenedor `senorarroz-coexistence-db-20261105` y volumen independiente `senorarroz-coexistence-local-pgdata-20261105`. La fuente fue importada el 29 de octubre y tiene actividad local hasta el 31; no se actualizó desde producción. Respaldo, credenciales, chats y capturas con datos personales permanecen ignorados en `.work/coexistence-local/`.

Las cuatro migraciones de procesamiento, coexistencia/atención, borrador y paginación se aplicaron dos veces sin errores. Antes del índice de sesión activa se archivaron las filas originales y se marcaron vencidas 14 sesiones ya expiradas, sin pedido ni pago pendiente asociado. No se eliminaron sesiones ni se modificó la base fuente.

API real: `http://127.0.0.1:5089`, mediante gateway local. API y PostgreSQL están únicamente en una red Docker interna; PostgreSQL no publica un puerto accesible desde el host. El modo optativo `WhatsAppLocalReview:Enabled` exige Development, host local y el nombre exacto de esta copia; deshabilita consumidores y transporte HttpClientFactory. El comportamiento habitual permanece igual. El rol runtime no es superusuario ni BYPASSRLS: sin tenant ve cero clientes; con tenant 1, 15.615.

Aplicación Vue completa: `http://127.0.0.1:4182/whatsapp`, iniciada desde el frontend con `node scripts/whatsapp_local_review.mjs`, usando BaseApi/SignalR reales y proxies hacia 5089. Se reutilizan editor, catálogo, selector de sucursal, pagos, modales y detalle del pedido. Usuarios exclusivos: `coexistence.asesor1@example.invalid` y `coexistence.asesor2@example.invalid`; contraseña en el campo `QaPassword` del archivo privado `runtime.private.json`.

| Caso | Pedido | Total | Resultado |
| --- | --- | --- | --- |
| 02 | 9543 | $47.000 | Trío con chicharrón; sin las yucas retiradas |
| 11 | 9544 | $52.000 | Bancolombia, sin verificar; beneficio automático declinado para repetir la cotización archivada |
| 16 | 9547 | $179.000 | Tres cajas; origen Santander, preparación/recogida Manrique; transferido del asesor 29 al 30 |
| 18 | 9545 | $153.000 | Dos Súper; reserva 6-nov a las 17:00, preparación 16:00, America/Bogota |
| 20 | 9546 | $36.000 | Transferencia retirada, nota de cambio para $50.000; efectivo sin marcar cobrado |

Los cinco teléfonos resolvieron clientes/direcciones existentes, sin duplicarlos. Se conservaron 66 mensajes fuente en 44 filas históricas agrupadas y cinco mensajes nuevos explícitos de simulación. El caso 16 se adaptó a recogida: la dirección solo tiene cobertura Santander, sin inventar cobertura Manrique. El caso 20 usa la tarifa guardada de $3.000, sin recrear el derecho histórico a envío gratis.

El recorrido real detectó un rechazo incorrecto del catálogo compartido al cotizar para otra sucursal. La cotización ahora reutiliza la consulta del POS con `sharedCatalog=true`, conservando los saldos del destino. Dos regresiones prueban destino sin stock y con stock suficiente. Pasaron 56 pruebas de atención, pedidos entre sucursales, inventario y aislamiento local.

Chromium automatizado, login JWT normal, controladores MVC y PostgreSQL: cinco pedidos persistidos; responsable anterior bloqueado con 409 y editor deshabilitado; diez reintentos concurrentes retornaron esos mismos cinco pedidos. Detalle remoto abierto desde Santander; capturas 1600×1100 y 390×844 inspeccionadas sin desborde horizontal ni pageerror en la revisión final. Se observaron errores transitorios de conexión durante el reinicio intencional de la API. La apertura del panel del usuario respondió `queued`, por lo que no se afirma control de esa pestaña.

La copia tiene 8.644 pedidos. Las cantidades y huellas de todas las filas originales de clientes, direcciones, productos y pedidos coinciden con la fuente al terminar. Cinco confirmaciones siguen pendientes con cero intentos y sin `sent_at`. Evidencia sin datos personales: `artifacts/whatsapp-stage6/local-copy-validation.json`. El simulador 4179 se conserva y no acredita estas pruebas. No hubo push, despliegue, actualización desde producción ni envío real de WhatsApp.

## Pendientes de aceptación

### Corrección durante la simulación del operador — 2026-11-05

El envío de texto y del resumen previo encontró dos fallos: el cliente de Meta intentaba leer como objeto un error JSON de tipo string, y la notificación posterior a un envío comparaba una fecha histórica sin Kind con `last_customer_message_at` en UTC. El parser ahora valida los tipos y conserva la redacción de tokens; la comparación reutiliza `AsUtc`, sin cambiar el esquema ni la configuración global de Npgsql.

El modo local sigue sin consumidores ni acceso externo. Solo las peticiones de texto a la línea ficticia `local-coexistence-review`, host Graph esperado y token ficticio `local-disabled` reciben una respuesta simulada con identificador `wamid.local.simulation.sent.*`. Líneas reales, otro host/token/query, archivos y payloads inválidos permanecen bloqueados. Los recibos locales no acreditan entrega a WhatsApp ni activan el outbox automático.

Pasaron 91 pruebas, incluida una PostgreSQL desechable con `timestamp without time zone` que envía dos respuestas y conserva el pendiente si existe un mensaje del cliente posterior. La primera verificación en navegador persistió una respuesta antes del fallo de notificación; se conservó para recuperarla sin reenviarla. El borrador del operador sigue separado del pedido confirmado anterior y su confirmación queda en manos del usuario.

En el navegador visible se recuperó la respuesta 12372 sin volver a enviarla y el resumen previo devolvió HTTP 200 con recibo local. El texto técnico desapareció, el compositor quedó limpio y se conservó el borrador de $33.000 con destino Manrique. La interfaz posterior reorganiza su columna sin cambiar contratos de API, esquema ni reglas comerciales; seguimiento en el plan del frontend y `test-results/whatsapp-stage6/order-sidebar-layout.json`.

### Aceptación externa

- Objetivos de apertura/cambio cumplidos en el perfil local final. Repetirlos en dispositivos y red del piloto antes de fijar presupuestos productivos.
- Aceptación real de coexistencia Meta, celular Business, línea piloto y transporte SignalR de extremo a extremo con dos navegadores reales.
- Piloto, aplicación SQL/configuración y despliegue gradual pertenecen a la etapa 7.
- El cluster PostgreSQL desechable quedó detenido; la vista local con datos sintéticos continúa disponible en el puerto 4179 para revisión del usuario.

Los tests locales de IA/Flow/ecos cubren versiones, deduplicación y fencing; no sustituyen la aceptación de una línea real. Línea, asesor, sucursal de trabajo, ruta del chat y destino del pedido permanecen separados.
