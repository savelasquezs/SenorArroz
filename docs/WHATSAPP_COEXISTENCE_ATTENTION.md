# WhatsApp: coexistencia y atención multiasesor

Implementación local: 2026-11-05. Rama: `feat/whatsapp-worker-stage01` en API y frontend.

La etapa 2 tiene implementación y pruebas con transporte simulado. Su aceptación con una línea real sigue pendiente de elegibilidad, permisos, configuración y piloto de Meta. La etapa 3 incorpora toma versionada, bandejas y transferencias. No se desplegó ni se aplicó SQL en producción; `External` no se activó.

## Esquema y configuración

Aplicar `SenorArroz.Infrastructure/Scripts/add_whatsapp_coexistence_attention.sql` después del esquema de las etapas 0/1 y antes de ejecutar esta versión. Es aditivo e idempotente, conserva mensajes y rechaza duplicados de identificadores de Meta dentro de una conversación o números configurados en varias sucursales. Los resultados comerciales no cambian.

La conexión guiada exige estas variables del servidor:

- `WhatsAppCoexistence__AppId`
- `WhatsAppCoexistence__AppSecret`
- `WhatsAppCoexistence__ConfigurationId`
- `WhatsAppCoexistence__GraphApiVersion`: versión habilitada para la aplicación, sin inferirla de la configuración antigua de Cloud API.
- `WhatsAppCoexistence__WebhookVerifyToken`
- `Integrations__EncryptionKey`, compartida con el worker según el contrato existente.

La aplicación debe tener Facebook Login for Business y una configuración vigente de Embedded Signup con coexistencia, permisos aprobados y webhooks `messages`, `history`, `smb_app_state_sync`, `smb_message_echoes` y `account_update`. El callback firmado y su verificación usan la ruta existente `/api/whatsapp/webhook`. La configuración local no acredita aprobación de Meta.

## Conexión por sucursal

`/api/branches/{branchId}/whatsapp-coexistence` ofrece diagnóstico, `signup-session`, `connect` y `check`. Requiere Admin de la sucursal o Superadmin dentro del tenant. El frontend recibe identificadores públicos y una sesión cifrada de quince minutos; no recibe el secreto de aplicación ni el token de la línea.

El usuario indica el número con código de país; se valida su coincidencia dentro de la cuenta, incluso cuando Meta devuelve únicamente el identificador de una cuenta con varias líneas. La sesión vincula tenant, sucursal y usuario. Se consume atómicamente una sola vez. El servidor verifica la aplicación propietaria del token, los permisos y sus cuentas autorizadas, la pertenencia del número a la cuenta, `is_on_biz_app`, `platform_type` y los campos de webhook. Suscribe la aplicación a la cuenta y solicita contactos e historial, sin registrar nuevamente el número.

Los estados son `pending`, `syncing`, `connected`, `degraded` y `disconnected`. Cada solicitud de sincronización se registra antes de llamar a Meta; no se repite automáticamente tras un resultado incierto. Se conservan los identificadores de solicitud para conciliación. Un registro después de una desconexión confirmada inicia una sincronización nueva; renovar una conexión existente no repite la sincronización anterior.

Se impide sustituir el número de una sucursal que ya tiene conversaciones. El canal global existente se conserva. Cada número es un canal diferente: no se fusionan historiales por compartir el teléfono del cliente.

### Recuperación del piloto Santander

`POST /api/branches/1/whatsapp-coexistence/pilot-recover` recupera el canal después de un alta parcial en Meta. Solo está habilitado en la base local aislada del piloto. Usa la cuenta y el identificador de teléfono configurados en el servidor. Comprueba por Graph que el número pertenece a esa cuenta, que `platform_type` es `CLOUD_API` y que `is_on_biz_app` está activo. Relee la suscripción de la app a la cuenta antes de guardar el canal.

`scripts/start-whatsapp-coexistence-pilot.ps1` usa `-PhoneNumberId`, `-RecoveryWabaId` y `-CallbackUrl` en los roles API y worker. El rol `meta-recovery-token` guarda la credencial ingresada en Windows DPAPI. El modo `-TemporaryRecoveryToken` usa un archivo separado y exige que Graph confirme su vencimiento dentro de 24 horas. Para rotar a una credencial de usuario del sistema, reinicia ambos procesos sin ese modo y vuelve a recuperar el mismo canal. Los secretos se validan y cifran en el servidor.

El callback alterno del piloto se aplica por teléfono. El callback de la app y sus eventos de cuenta siguen en el destino general de Meta. En el POS, **Restaurar webhook de Santander** retira el callback alterno. Hazlo con una credencial vigente y verifica el resultado antes de detener el gateway. El gateway del piloto recibe únicamente el webhook. El procesamiento y los envíos de texto se acotan a Santander y sus destinatarios de prueba. El estado de historial y contactos es **Omitida**.

Para la aceptación de mensajes entrantes usa WhatsApp Web o el celular principal del número de prueba. Meta documenta que WhatsApp para Windows puede entregar el mensaje al celular del negocio sin reflejar su contenido en el webhook. Un eco sin teléfono puede entrar al piloto cuando el identificador de WhatsApp del destinatario ya está asociado a un número de prueba de Santander. [Dispositivos vinculados admitidos](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/onboarding-business-app-users/#linked-devices)

## Ingreso y sincronización

- Los ecos del celular se particionan por línea/contacto junto al ingreso normal. Se muestran con procedencia `businessApp`, sin atribuirlos a un empleado. Pausan la automatización e invalidan respuestas antiguas; conservan al responsable humano y el destino comercial.
- El historial usa `is_historical` y procedencia `history`. No incrementa no leídos, no abre una ventana de atención, no notifica como mensaje nuevo ni ejecuta IA, ausencia, Flow, reintentos de pago o creación de pedidos. Un mensaje histórico enriquecido posteriormente con multimedia actualiza el mismo registro.
- Los contactos solo actualizan el nombre de contacto del canal. No alteran el cliente comercial ni sus direcciones; una eliminación conserva la conversación y sus mensajes. Los contactos sin mensajes quedan archivados hasta que exista actividad.
- `PARTNER_REMOVED` se resuelve desde la cuenta firmada incluso sin `metadata.phone_number_id`; desconecta las líneas correspondientes y cancela su automatización. No afecta otros tenants ni líneas.
- La ventana de servicio se calcula en el servidor desde el último mensaje entrante nuevo, no desde el último mensaje enviado ni desde el historial. Fuera de veinticuatro horas, el envío manual libre se rechaza y la interfaz permite consultar y enviar las plantillas aprobadas de la misma cuenta y línea, sin salir del chat.

La sincronización real debe iniciarse dentro de las veinticuatro horas del registro. Meta limita el historial y la disponibilidad de multimedia; los mensajes sin contenido recuperable conservan un marcador. No se descarga una URL arbitraria del webhook: se reutiliza la recuperación autenticada de multimedia y el almacenamiento existentes.

## Atención multiasesor

`POST /api/whatsapp/conversations/{id}/attention-actions` recibe `action`, `expectedVersion` y, cuando corresponde, `targetUserId` o `reason`. Las rutas antiguas de atención exigen `X-WhatsApp-Attention-Version` y usan el mismo contrato.

Acciones: `take`, `release`, `transfer`, `force-take`, `return-to-ai`, `pause-ai`, `request-human`, `close` y `reopen`. La toma forzada exige un supervisor con acceso y un motivo. Una transferencia valida el acceso del destinatario. No hay apropiación implícita al enviar.

`attention_revision` es un token de concurrencia PostgreSQL. Una versión ausente produce 428; una versión obsoleta o una toma competidora produce 409, con el estado de atención actual cuando corresponde. La transición y su auditoría se guardan en la misma operación. `attention_mode_updated_at` conserva la protección temporal de los trabajos y respuestas de IA ya persistidos.

La lectura y supervisión no conceden escritura. Mensajes, carta, productos, respuestas rápidas, adjuntos, plantillas de conversaciones existentes, cambios de cliente, edición comercial y creación de pedidos requieren responsabilidad explícita. El worker revalida responsable, acceso y versión antes de ejecutar los trabajos manuales.

`needs_human_response` no depende del contador compartido de no leídos: abrir el historial no resuelve una respuesta pendiente. Las respuestas manuales confirmadas solo lo limpian si no existe una pregunta posterior. Cerrar o devolver a IA cambia su estado explícitamente.

El listado filtra Libres, Míos, Equipo y Cerrados en el servidor antes del límite vigente. Sin sede operativa es un filtro independiente. La paginación con cursor de conversaciones permanece en etapa 6. Los enlaces directos resuelven la conversación autorizada aunque no esté en la bandeja cargada, sin marcarla leída. El frontend conserva selección y filtros en la navegación, borradores de mensaje y adjuntos por conversación; una respuesta tardía no borra otro borrador ni retrocede la versión de atención.

Una transferencia de enrutamiento es distinta de transferir asesor y de seleccionar destino de un pedido. La asignación inicial de enrutamiento conserva al asesor; cambiar un enrutamiento existente lo libera. Ni línea, asesor, `OriginBranchId` ni `BranchId` se sustituyen entre sí. Los pedidos ya confirmados conservan sucursal de origen, preparación y permisos vigentes.

La auditoría se guarda como `attention_action` en `whatsapp_webhook_event`, sin textos de mensajes ni credenciales. La limpieza de payloads de transporte no borra estas entradas. Los destinatarios revocados reciben únicamente invalidación mínima; el relay y la publicación Embedded no envían contenido a grupos que perdieron acceso.

## Validación y límites

Pruebas locales: contratos de Meta simulados, aislamiento de líneas, importación sin efectos comerciales, desconexión, permisos, toma/reasignación/versiones, borradores de mensaje, callbacks del SDK y regresión de ambos repositorios. PostgreSQL 17 se ejecutó en un cluster independiente en loopback, con una base nueva por escenario y eliminación al finalizar. No se utilizó la base del laboratorio.

La prueba de dos asesores simultáneos comprueba un único responsable, una única auditoría y un conflicto para el competidor, preservando el pedido remoto. También se prueba el SQL desde un esquema sin las nuevas columnas y se repite su aplicación. El registro guiado prueba canje y permisos con transporte falso, consumo único de sesión y ausencia de tokens en la respuesta.

Validación final: backend general 1.444 aprobadas y 107 omitidas; PostgreSQL y procesos separados 26 aprobadas; frontend 1.008 aprobadas en 160 archivos. Regresiones dirigidas finales: 40 de backend y 78 de frontend, con casos solapados con las suites anteriores. API/worker y frontend con tipos compilan correctamente.

La evidencia agregada final está en `artifacts/whatsapp-stage23/validation.json`. Las cifras de las etapas 0/1 no se reutilizan como resultados de esta entrega.

Pendiente externo: aprobación y configuración real de Meta, conexión de línea piloto, historial/multimedia reales y prueba con dos navegadores y celulares. La activación gradual y el corte de consumidores pertenecen a la etapa 7. No se presenta el transporte simulado como aceptación productiva de coexistencia.

## Documentación oficial contrastada

- https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/onboarding-business-app-users
- https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/version-4/
- https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/reference/history/
- https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/reference/smb_message_echoes/
- https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/reference/smb_app_state_sync/
- https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/reference/account_update/
