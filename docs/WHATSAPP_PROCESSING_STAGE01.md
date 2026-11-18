# WhatsApp — contratos y extracción del procesamiento

Fecha: 2026-10-30. Rama: feat/whatsapp-worker-stage01, desde origin/main d4435e7. Frontend asociado: misma rama, base e2d1087. Los checkouts originales y el laboratorio no se usan como fuente de código para esta entrega.

## Límites de esta entrega

Se implementa la base de procesamiento externo y se documentan los contratos que usarán las etapas posteriores del chat. La selección de destino del borrador y las nuevas bandejas no se presentan como terminadas. No se habilitan números, IA, Flows, nuevas credenciales ni consumidores en producción.

## Matriz de acceso vigente

| Recurso | Admin/Cashier | Superadmin | Kitchen/Deliveryman |
|---|---|---|---|
| Cola central sin sede | Visible dentro del tenant | Visible dentro del tenant | Sin acceso administrativo |
| Chat central enrutado | Sede de atención o asesor asignado | Sedes autorizadas del tenant | Sin acceso administrativo |
| Chat de línea de sucursal | Según permisos actuales de la línea/sede | Según tenant y contexto | Sin acceso administrativo |
| Pedido tomado para otra sede | Gestión por origen y por recurso; conservar restricciones de fecha/estado/rol | Gestión autorizada del tenant | Mantener operación local existente |
| Credencial del worker | No accesible desde el navegador | Configuración de servidor | Sin acceso |

Las herramientas internas resuelven el tenant y la conversación desde un mensaje entrante persistido y actualmente en procesamiento. No aceptan TenantId, cliente, destino o usuario de un payload público como autoridad.

## Contratos comerciales para las etapas 3 y 4

- Línea: identifica el remitente de WhatsApp; se conserva al tomar un pedido para otra sede.
- Enrutamiento de chat: OperationalBranchId actual. Cambiarlo es una transferencia explícita y conserva su semántica de liberar al asesor.
- Responsable: AssignedUserId. Cambiar de chat o cambiar el destino comercial no modifica este dato.
- Destino del borrador: futura operación independiente de enrutamiento; recibe conversación, sesión, destino y versión esperada. Valida destino autorizado y vuelve a cotizar. No escribe BranchContext, AssignedUserId ni la sede de un pedido confirmado.
- Origen del pedido: OriginBranchId resuelto en servidor desde el puesto de trabajo. BranchId corresponde a preparación, inventario, impresión, rutas, venta y cobro.
- Traslado de pedido confirmado: operación existente de gestión del pedido, con su versión y permisos. No se deriva de una transferencia del chat.
- Sesión comercial: un borrador activo inicial por conversación, identificador estable, versión y clave de confirmación. Múltiples pedidos históricos; una clave nueva únicamente al iniciar otro intento comercial.
- Handoff IA/Flow/persona: mismo intento comercial y confirmación idempotente; el asesor debe recibir el estado guardado y resolver cambios de versión. No se crean carritos independientes que puedan confirmar dos pedidos para el mismo intento.

Se identificaron controles en CreateOrderHandler y AgentToolExecutor que todavía vinculan el destino a OperationalBranchId o a la sede de la línea. Su adaptación funcional se hará junto con el borrador autorizado en etapa 4; retirarlos ahora ampliaría permisos sin el recurso/versión que debe acotar esa excepción. Los controles actuales y OriginBranchId se conservan en esta entrega.

## Inventario de procesamiento

| Operación | Dueño en modo External |
|---|---|
| Firma, resolución de canal/tenant y aceptación durable del webhook | API principal |
| Interpretación de eventos, identidad y multimedia entrante | Worker |
| IA, recuperación de mensajes y telemetría de IA | Worker |
| Mensajes automáticos y outbox comercial existente | Worker |
| Lectura de herramientas y herramientas de mensajería de IA | Worker, reutilizando implementaciones existentes |
| Herramientas que modifican estado comercial o atención | API principal, mediante contrato interno autenticado |
| Flow cifrado, cotización, confirmación comercial, pagos e inventario | Motor existente en API principal |
| Peticiones manuales de mensajes, carta, productos, respuestas rápidas, plantillas y multimedia | API autoriza y acepta trabajo durable; worker ejecuta y persiste el resultado |
| SignalR administrativo | API principal, mediante relay de eventos persistidos |

## Ejecución y configuración

- Embedded es el modo por defecto y conserva los consumidores existentes dentro de la API.
- WhatsAppProcessing__Mode=External en la API deshabilita sus consumidores de WhatsApp y activa el relay y la aceptación durable de operaciones manuales.
- El ejecutable SenorArroz.WhatsApp.Worker exige External y habilita solo consumidores WhatsApp. Publica /health/live y /health/ready, sin controladores ni hubs de negocio.
- Ambos procesos deben compartir conexión PostgreSQL e Integrations__EncryptionKey. El arranque externo verifica columnas/tablas y disponibilidad del cifrado. En producción conserva la comprobación del rol y RLS.
- WhatsAppProcessing__WorkerKey: secreto de servidor de al menos 32 caracteres, distinto de tokens de usuarios; compartido solo entre API y worker. Nunca enviarlo al frontend o registrarlo en logs.
- WhatsAppProcessing__BusinessApiUrl: raíz de la API mediante HTTPS; HTTP solo para loopback o red privada Railway. Contrato POST /internal/whatsapp/tools, sin reintentos HTTP automáticos.
- Concurrency=4, AiConcurrency=2 en External, PollSeconds=15, WorkTimeoutSeconds=120, LeaseSeconds=180, MaxAttempts=5 por defecto. La concesión debe exceder el timeout del handler.
- Dockerfile.whatsapp-worker publica el ejecutable separado. El proyecto forma parte de la solución y debe compilarse también en CI.

## Persistencia, recuperación y seguridad

- Cada mensaje/estado de un webhook se guarda por separado, con clave de deduplicación y partición hash de línea/contacto. La transacción de ingreso acepta el lote completo o ninguno.
- El payload necesario para procesar tokens de Flow se cifra; la copia de auditoría utiliza el saneamiento existente. El payload cifrado se elimina después de completar correctamente el trabajo.
- PostgreSQL reclama trabajo con FOR UPDATE SKIP LOCKED. El primer pendiente bloquea los posteriores de su partición; otras conversaciones pueden avanzar.
- LISTEN/NOTIFY despierta consumidores y relay después de persistir; se utiliza una conexión de señales por proceso y la recuperación periódica de 15 segundos como respaldo. Las señales no sustituyen la cola durable ni transportan contenido del cliente.
- LeaseToken impide que un propietario vencido complete el trabajo reclamado por otro proceso. Los intentos tienen límite y backoff.
- Si ya se guardaron efectos y no se confirmó el procesamiento posterior, se requiere revisión. No se repite ciegamente un envío a Meta que pudo haber sido aceptado.
- La IA no reclama mensajes cuyo webhook durable no terminó, ni adelanta mensajes anteriores pendientes/en procesamiento de la misma conversación.
- El worker encola IA después de confirmar el trabajo durable; no depende del siguiente barrido de recuperación para atender el primer mensaje. Concurrency controla consumidores de webhook. En External, AiConcurrency=2 permite procesar conversaciones distintas en paralelo; PostgreSQL conserva el orden de cada conversación y el siguiente mensaje se agenda al terminar el anterior, sin esperar otro barrido. Embedded conserva un consumidor de IA.
- El anclaje histórico de una conversación central se resuelve dentro del tenant, sin fijar BranchId=1 y sin convertir ese anclaje en la sede de preparación del pedido.
- La IA revalida la versión temporal de atención durante su ejecución, para invalidar también el ciclo IA → humano → IA.
- La versión de atención se guarda con el mensaje entrante y se conserva al recuperar una respuesta generada. Se descartan respuestas de otra versión y respuestas legacy generadas sin una versión recuperable, incluso después del ciclo IA → humano → IA.
- Eventos WhatsApp se persisten antes de enviarlos a SignalR en modo externo. Cada réplica de API confirma su propia entrega; no compiten por una única marca global.
- Al arrancar, un relay marca únicamente eventos ya visibles como históricos; las conexiones nuevas reconcilian por REST. No avanza un cursor global que pueda saltar transacciones confirmadas fuera de orden.
- El relay resuelve nuevamente los grupos autorizados y el estado actual. Los destinatarios liberados reciben solo invalidación mínima de enrutamiento, sin datos del cliente o mensajes.
- SignalR es entrega al menos una vez; se mantiene deduplicación de mensajes en el frontend y reconciliación al reconectar. La retención elimina eventos antiguos en lotes acotados; una réplica desconectada más allá de esa ventana debe reconciliar por REST.

## Línea base y presupuestos

Lectura Railway producción de una hora, 61 muestras por servicio, durante esta ejecución:

| Medida | API | PostgreSQL |
|---|---:|---:|
| CPU_USAGE promedio, unidad reportada por Railway | 0.013117 | 0.057157 |
| CPU_USAGE máximo, unidad reportada por Railway | 0.020821 | 0.121526 |
| Memoria promedio, GB reportados | 0.496255 | 0.390850 |
| Memoria máxima, GB reportados | 0.514576 | 0.453231 |

API y frontend reportaron despliegue SUCCESS y no había cambios pendientes en Railway al consultar. Esto no prueba picos en horario comercial ni la capacidad máxima. No se realizaron pruebas de carga contra producción.

Presupuestos iniciales para validar en un ambiente controlado: webhook aceptado p95 ≤ 500 ms sin I/O de Meta/IA; evento persistido → relay p95 ≤ 1.5 s bajo la carga acordada; cambio de chat cacheado p95 ≤ 150 ms y primera carga p95 ≤ 1.5 s cuando se implemente la nueva pantalla. Son objetivos de aceptación propuestos, no resultados medidos ni un SLA ya aprobado.

Pendiente: consultas/conexiones SQL y distribución p50/p95 real, tamaño de respuestas, volumen de asesores y backlog en horario operativo. El ensayo reproducible de cola usa PostgreSQL desechable; no modifica el contenedor de laboratorio en puerto 15434 ni las bases de producción.

## Esquema y corte operativo

Aplicar SenorArroz.Infrastructure/Scripts/add_whatsapp_processing_worker.sql antes del backend: amplía whatsapp_webhook_event y agrega whatsapp_realtime_event con RLS. El script no promueve eventos legacy ni reprocesa historial.

El inicializador completo está fuera del repositorio remoto, en el directorio padre del checkout original. Se creó una copia aislada en C:/Users/LENOVO/Documents/ChatGPT/Señor arroz real/.work/local-init-completo.sql y se agregó el SQL puntual. El original compartido conserva SHA256 3761248AE7E213D616200D4391AD755E022FE062314BA2229F5880599C8818C4. No se ejecutó el inicializador completo ni se modificó el esquema productivo.

El cambio de modo requiere un corte coordinado: drenar consumidores Embedded, detenerlos, habilitar API External y arrancar el worker. No mezclar versiones Embedded y External durante el corte. Rollback: pausar worker, conservar trabajos y eventos, reconciliar resultados inciertos y volver al modo anterior sin consumidores superpuestos. No revertir tablas ni eliminar mensajes.

Antes de desplegar, verificar además los permisos mínimos del rol runtime sobre la nueva tabla y su secuencia, la compatibilidad de cambios del laboratorio al integrarlos y la retención de eventos. No ampliar automáticamente permisos a roles desconocidos.

Fuentes de diseño: https://www.postgresql.org/docs/17/explicit-locking.html y https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/web-queue-worker.


## Validación técnica de la entrega inicial — 2026-10-31

| Comprobación | Resultado |
|---|---|
| Regresión general | 1420 aprobadas; 107 omitidas |
| PostgreSQL y procesos separados | 9 aprobadas; 0 omitidas |

Solución y worker compilados sin errores. PostgreSQL desechable verifica ingreso, recuperación, RLS, migración idempotente y operación entre sucursales. La prueba de procesos separados cubre una línea de sucursal del tenant 1 y una línea central del tenant 2, sin llamadas reales a Meta. El barrido de recuperación se fija a 300 segundos para comprobar el encolado inmediato después de confirmar el webhook. No se declara cerrada la validación operacional ni habilitado External en producción.


Validación adicional 2026-10-31: el ensayo de procesos separados también ejecuta una herramienta comercial autenticada en la API, rechaza una versión de atención obsoleta y confirma que el estado comercial del segundo tenant permanece intacto. La ampliación de esta prueba pasó 1/1. El paquete del worker se publicó localmente en configuración Release, sin errores; esto no es un despliegue de Railway.


## Continuación de cierre técnico — 2026-10-31

- `WhatsAppManualOperations` concentra las operaciones existentes y sus controles compartidos. En Embedded conserva la respuesta directa; en External los endpoints devuelven 202 con `operationId` y el frontend consulta el resultado mediante GET autenticado. La consulta exige tenant, actor y acceso vigente; el cambio de sucursal de preparación de un pedido no participa de este transporte.
- `WhatsAppManualWorkQueue` reutiliza `whatsapp_webhook_event`: payload y resultado cifrados, clave de solicitud única por tenant/actor, exclusión por conversación y contexto de trabajo obtenido del servidor. El worker reconstruye el actor desde el usuario vigente; cambios de rol, sede de trabajo o versión de atención invalidan el envío pendiente.
- El worker guarda el resultado antes de cerrar el trabajo. Si cae después, recupera ese resultado sin repetir la operación. Si cae durante un envío sin resultado persistido, la incidencia requiere revisión y no vuelve a enviar automáticamente.
- La multimedia se deposita primero en el almacenamiento existente para aceptar una referencia durable. PostgreSQL conserva una referencia pequeña, no el binario. El worker descarga del bucket configurado por nombre de objeto y sube a Meta; reutiliza ese objeto como multimedia del mensaje. La política operativa deberá contemplar objetos aceptados por Storage cuyo alta en base de datos no llegara a confirmarse.
- La deduplicación entrante usa línea, identificador del mensaje y transición de estado; cambios en contactos o formato del lote no provocan otro trabajo. Alias hash de teléfono/BSUID, separados por línea y tenant, conservan la partición al conocerse su asociación. Agrupar trabajo no fusiona historiales ni concede permisos. No se infiere que dos identidades sin vínculo conocido sean la misma persona.
- `WhatsAppProcessingMaintenance` publica gauges de pendientes, en proceso, revisión, reintentos, edad del más antiguo y eventos retenidos. El consumidor conserva histogramas de duración/edad y contadores de finalización/error; el relay añade edad de entrega. Logs agregados permiten observarlos aun sin un exportador de métricas.
- Valores iniciales: métricas cada 30 s; eventos de tiempo real 2 días; payloads/resultados terminales 7 días; limpieza de hasta 500 filas por ciclo. Se conserva el identificador de deduplicación después del saneamiento. Nunca se elimina trabajo pendiente/en proceso por antigüedad.
- La versión adicional de esquema debe aplicarse antes de cualquier versión nueva de API/worker, incluso usando Embedded. No se ha aplicado en producción.

Validación de esta continuación completada; resultados y límites registrados al final de este documento.


## Línea base ampliada, sin cambios productivos

Evidencia agregada: `artifacts/whatsapp-stage01/production-baseline.json` y `database-baseline.json`. La inspección PostgreSQL usa conexión TLS, sesión con `default_transaction_read_only=on`, transacción `READ ONLY`, límites de consulta/bloqueo y `ROLLBACK`; no imprime credenciales ni devuelve teléfonos, nombres, textos o consultas SQL de usuarios.

- En la verificación de esta continuación, main remoto coincidía con las bases de esta rama: API d4435e7 y frontend e2d1087. Railway mantiene la API en main y el despliegue SUCCESS `5ef45632-a288-4b21-89d1-95b88f75f66a`, con una réplica y sin cambios preparados.
- Ventana de recursos de 24 horas, 1441 muestras: API CPU_USAGE promedio 0.015250 y máximo 0.207390; memoria promedio 0.587026 GB y máxima 0.814854 GB. PostgreSQL CPU_USAGE promedio 0.064013 y máximo 0.172421; memoria promedio 0.363726 GB y máxima 0.608563 GB. Se conservan las unidades de Railway, sin convertir CPU_USAGE a porcentajes.
- Latencia HTTP global de la API en 24 horas: el mayor p95 entre 31 intervalos fue 143 ms; los p50 por intervalo van de 10 a 42 ms. No es un p95 global ni una medición exclusiva de WhatsApp. Los percentiles por intervalo no se promedian.
- PostgreSQL 17.11, lectura del 31 de octubre a las 00:32 de Bogotá: 3 conexiones, de las cuales una es la inspección; sin deadlocks acumulados en las estadísticas disponibles. Los contadores de transacciones/buffers son acumulados y no se interpretan como consultas por segundo.
- Integración registrada en los últimos 7 días: 13 mensajes, 5 entrantes y 8 salientes, 2 conversaciones y 1 asesor con envío manual registrado. Pico entrante de 2 mensajes por intervalo de 5 minutos. Hay 46 conversaciones asignadas a 4 identificadores distintos de asesor y 3 esperando atención; esas asignaciones no prueban presencia simultánea. Backlog de IA pendiente/en proceso: 0.
- Tamaños almacenados promedio de texto y payload: 23 y 195 bytes respectivamente. Son tamaños en base de datos, distintos de los cuerpos HTTP medidos en el ensayo sintético.
- Se verificaron `order.origin_branch_id` y `order.management_version`. Las nuevas columnas de procesamiento y `whatsapp_realtime_event` todavía no están instaladas en producción, como corresponde a esta rama sin despliegue.
- `pg_stat_statements` no está instalado y tracing permanece desactivado. La línea base no atribuye latencia a consultas productivas individuales ni intenta habilitar instrumentación. El perfil controlado registra los comandos SQL ejecutados por la nueva infraestructura.

Presupuestos de ingeniería para el ambiente controlado: aceptación durable p95 ≤ 500 ms y relay p95 ≤ 1.5 s. El perfil inicial usa 240 mensajes, 32 conversaciones, 2 tenants, 8 productores y 4 consumidores, con handler simulado de 10 ms. Las metas de cambio de chat cacheado ≤ 150 ms y primera carga ≤ 1.5 s se medirán sobre la interfaz de etapas 5/6; no se presentan como resultados de una pantalla aún pendiente. La escasa actividad actual no permite extrapolar la capacidad máxima de la futura operación de coexistencia.

El corte futuro debe publicar primero el frontend compatible manteniendo Embedded; después aplicar/verificar SQL y desplegar código compatible, drenar consumidores y activar External de manera coordinada. Activar External con un frontend que interpreta la respuesta 202 como mensaje final rompe el contrato de UX. Esta secuencia es documentación de etapa 7, no una operación ejecutada.


La versión de atención se captura desde el ingreso durable cuando ya existe la conversación, no únicamente al generar la respuesta. También se descarta trabajo antiguo sin versión recuperable que precede un cambio de atención. El ensayo con procesos separados deja un mensaje en cola, cambia la atención con el worker detenido y comprueba su descarte al reiniciar.

Entrada y envíos manuales comparten resolución de partición bajo la misma transacción y exclusión por línea/conversación. El índice `(tenant_id, conversation_id, id)` complementa los alias GIN. Los timeouts, fallos de red y respuestas inválidas de Meta se consideran resultados inciertos en operaciones manuales External, incluyendo una respuesta exitosa sin identificador de mensaje. Se requiere revisión y no se inicia un fallback de carta sobre un envío incierto.

El SQL agregado de diagnóstico se conserva en `SenorArroz.Infrastructure/Scripts/inspect_whatsapp_processing_baseline.sql`; para repetirlo se utiliza la conexión interactiva del repositorio desde Bash con `railway connect MainDatabase`, siguiendo AGENTS.md. No contiene DDL ni modificación de datos del negocio.


## Cierre técnico de etapas 0 y 1 — 2026-10-31

| Verificación | Resultado |
|---|---|
| Backend general | 1424 aprobadas; 107 omitidas |
| PostgreSQL, procesos y pedidos remotos | 24 aprobadas; ninguna omitida |
| Ampliación del caso de multimedia con Storage no disponible | 1/1 aprobada; es un caso de la tabla anterior ampliado, no otro escenario único |
| Frontend completo | 996 aprobadas en 157 archivos |
| Solución, tipos/frontend y publicación local Release del worker | Sin errores |

La aceptación incluye SQL de actualización idempotente y RLS, ingreso cifrado/deduplicado, orden y exclusión compartidos entre entrada y salida manual, alias teléfono/BSUID separados por línea/tenant, API disponible con el worker detenido, recuperación tras reinicio, atención capturada antes de quedar en cola, resultados manuales persistidos, permisos revocados, origen de trabajo de superadmin, concurrencia de IA entre chats y continuidad ordenada dentro de uno, fanout por réplica y ausencia de contenido a grupos revocados. Los pedidos de origen Santander y preparación remota conservan origen, destino, responsable y versiones; no se cambiaron los handlers comerciales.

Perfil controlado más reciente: ingreso p50 156.09 ms y p95 301.43 ms; relay a proxy SignalR simulado p95 1287.27 ms. Se aceptaron 240 mensajes con backlog máximo observado al iniciar el drenaje de 240, 8 conexiones PostgreSQL al terminar el ingreso y 2484 comandos SQL durante el ensayo completo. El drenaje con handler simulado de 10 ms tomó 6.61 s. Cuerpos de respuesta de la operación manual: 126 bytes al aceptar y 453 bytes al completar, sin cabeceras HTTP. El perfil excluye Meta/LLM y la red del cliente; no mide capacidad productiva ni latencia de una pantalla nueva.

Comandos de aceptación: `dotnet build SenorArroz.sln --no-restore`; regresión `dotnet test ... --no-build --filter 'Category!=PostgreSqlIntegration'`; integración con `RUN_POSTGRES_INTEGRATION_TESTS=true` filtrando `WhatsAppProcessingPostgreSqlTests` y las pruebas `Concurrent_source_edits_have_one_winner` / `Direct_SQL_updates_increment_version_and_cannot_rewrite_provenance`. Frontend: `node node_modules/vitest/vitest.mjs run --maxWorkers=2` y `npm run build` con VITE_API_URL HTTPS. Publicación local: proyecto `SenorArroz.WhatsApp.Worker`, configuración Release.

Las etapas quedan cerradas técnicamente. La validación real de coexistencia, la sesión comercial compartida y el editor integrado conservan sus etapas 2–6. SQL/grants productivos, compatibilidad al integrar cambios del laboratorio, exportación/alertas de métricas, retención de objetos huérfanos, piloto con Meta/Storage reales, prueba de corte y activación de External quedan en etapa 7. No se aplicó SQL de actualización, desplegó, activó ni conectó un número en producción. La lectura diagnóstica de producción no modifica datos del negocio; las pruebas escriben únicamente en PostgreSQL desechable.
