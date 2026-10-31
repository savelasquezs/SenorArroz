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
| Peticiones manuales de mensajes, plantillas y multimedia | Rutas actuales en API; extracción restante pendiente |
| SignalR administrativo | API principal, mediante relay de eventos persistidos |

## Ejecución y configuración

- Embedded es el modo por defecto y conserva los consumidores existentes dentro de la API.
- WhatsAppProcessing__Mode=External en la API deshabilita sus consumidores de WhatsApp y activa el relay.
- El ejecutable SenorArroz.WhatsApp.Worker exige External y habilita solo consumidores WhatsApp. Publica /health/live y /health/ready, sin controladores ni hubs de negocio.
- Ambos procesos deben compartir conexión PostgreSQL e Integrations__EncryptionKey. El arranque externo verifica columnas/tablas y disponibilidad del cifrado. En producción conserva la comprobación del rol y RLS.
- WhatsAppProcessing__WorkerKey: secreto de servidor de al menos 32 caracteres, distinto de tokens de usuarios; compartido solo entre API y worker. Nunca enviarlo al frontend o registrarlo en logs.
- WhatsAppProcessing__BusinessApiUrl: raíz de la API mediante HTTPS; HTTP solo para loopback o red privada Railway. Contrato POST /internal/whatsapp/tools, sin reintentos HTTP automáticos.
- Concurrency=4, PollSeconds=15, WorkTimeoutSeconds=120, LeaseSeconds=180, MaxAttempts=5 por defecto. La concesión debe exceder el timeout del handler.
- Dockerfile.whatsapp-worker publica el ejecutable separado. El proyecto forma parte de la solución y debe compilarse también en CI.

## Persistencia, recuperación y seguridad

- Cada mensaje/estado de un webhook se guarda por separado, con clave de deduplicación y partición hash de línea/contacto. La transacción de ingreso acepta el lote completo o ninguno.
- El payload necesario para procesar tokens de Flow se cifra; la copia de auditoría utiliza el saneamiento existente. El payload cifrado se elimina después de completar correctamente el trabajo.
- PostgreSQL reclama trabajo con FOR UPDATE SKIP LOCKED. El primer pendiente bloquea los posteriores de su partición; otras conversaciones pueden avanzar.
- LISTEN/NOTIFY despierta consumidores y relay después de persistir; se utiliza una conexión de señales por proceso y la recuperación periódica de 15 segundos como respaldo. Las señales no sustituyen la cola durable ni transportan contenido del cliente.
- LeaseToken impide que un propietario vencido complete el trabajo reclamado por otro proceso. Los intentos tienen límite y backoff.
- Si ya se guardaron efectos y no se confirmó el procesamiento posterior, se requiere revisión. No se repite ciegamente un envío a Meta que pudo haber sido aceptado.
- La IA no reclama mensajes cuyo webhook durable no terminó, ni adelanta mensajes anteriores pendientes/en procesamiento de la misma conversación.
- El worker encola IA después de confirmar el trabajo durable; no depende del siguiente barrido de recuperación para atender el primer mensaje. Concurrency controla consumidores de webhook; se conserva por ahora el consumidor serial de IA existente.
- El anclaje histórico de una conversación central se resuelve dentro del tenant, sin fijar BranchId=1 y sin convertir ese anclaje en la sede de preparación del pedido.
- La IA revalida la versión temporal de atención durante su ejecución, para invalidar también el ciclo IA → humano → IA.
- Pendiente antes de producción: persistir esa versión con la respuesta generada y comprobarla al recuperarla después de reiniciar el worker. La comprobación actual durante la ejecución y en RPC no acredita ese escenario de recuperación.
- Eventos WhatsApp se persisten antes de enviarlos a SignalR en modo externo. Cada réplica de API confirma su propia entrega; no compiten por una única marca global.
- Al arrancar, un relay marca únicamente eventos ya visibles como históricos; las conexiones nuevas reconcilian por REST. No avanza un cursor global que pueda saltar transacciones confirmadas fuera de orden.
- El relay resuelve nuevamente los grupos autorizados y el estado actual. Los destinatarios liberados reciben solo invalidación mínima de enrutamiento, sin datos del cliente o mensajes.
- SignalR es entrega al menos una vez; se mantiene deduplicación de mensajes en el frontend y reconciliación al reconectar. Debe añadirse retención de eventos antes de un funcionamiento prolongado en producción.

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
