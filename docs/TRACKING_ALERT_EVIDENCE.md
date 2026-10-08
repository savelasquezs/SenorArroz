# Alertas de seguimiento: evidencia y reglas acordadas (2026-10-08)

## Alcance

No se introduce el filtro pospuesto `active_delivery + ruta activa`. Ambos modos siguen siendo elegibles para permanencias. La frecuencia de captura sigue siendo la configuración existente (30 s activa / 300 s liviana). La cadencia solo limita los vacíos de evidencia; no añade llamadas de ubicación ni heartbeats HTTP.

La política operativa de permanencia es 10 minutos, radio 20 m y al menos 90% de acuerdo entre lecturas evaluables. El centro es la mediana espacial de las muestras. Lecturas con precisión peor que el radio son neutrales; además se exige cobertura evaluable del 90% para no convertir tres lecturas buenas entre muchas desconocidas en una certeza falsa. Los huecos superiores a 1,5 intervalos esperados separan periodos; no se suman. No se borran puntos por ser atípicos. El porcentaje es acuerdo de muestras, no probabilidad de una falta. Los antiguos campos de umbral/radio de sucursal se conservan por compatibilidad; la respuesta publica la regla efectiva y el formulario los muestra de solo lectura. No se modifica la tolerancia geográfica de la sede/destino.

La sede permanece exenta. En dirección de cliente, más de 20 minutos genera revisión. La clasificación se recalcula por `RecordedAt`; los datos recuperados pueden corregir o retractar una inferencia manteniendo su identificador, observaciones y decisiones administrativas. La duración mostrada nunca avanza sin nuevas muestras.

## Estados del dispositivo

Las alertas administrativas de GPS/permisos requieren `evidence_version=2;source=android_state` y los valores independientes de Android. Un error de consulta es desconocido. Ubicación desactivada requiere 60 s de observación monotónica continua; quitar precisión/permiso usable es inmediato. La recuperación se asocia por `episode`. Los reportes legacy no se elevan a evidencia confirmada y se marcan como no verificables; no se alteran notas ni decisiones de revisión existentes.

El cliente consulta `ApplicationExitInfo` al volver a iniciar. Solo `REASON_USER_REQUESTED=10` en Android 14/API34 o superior se etiqueta como solicitud del usuario: versiones anteriores también usaban ese motivo para actualizaciones. `REASON_USER_STOPPED=11` no es el cierre manual de esta app. Reinicios/salidas técnicas son informativos; interrupciones sin prueba conservan causa desconocida. Ningún código de terminación identifica a la persona o prueba intención. Un proceso detenido no puede enviar un aviso inmediato: el servidor detecta silencio y la evidencia OS se completa cuando el proceso vuelve a ejecutar.

Estas son observaciones del cliente autenticado, no una atestación criptográfica del teléfono ni prueba disciplinaria. Se exige revisión humana; no se bloquean funciones ni se asignan sanciones automáticamente.

## Comunicación y notificaciones

Sin comunicación: aviso a los 3 min en modo activo; con cadencia de 5 min, no antes del siguiente reporte esperado más 60 s (6 min). Revisión a los 10 min. Se muestra última ubicación conocida con hora original, no una ubicación actual inventada.

Un error de transporte/API no se llama ausencia de Internet. El campo InternetAvailable desconocido permanece null. Las recuperaciones genéricas cierran el episodio y conservan historial; GPS/permisos y detención manual respaldada siguen pendientes de revisión aunque se recupere el servicio. No se deduce una falta a partir de repetición de cortes cortos.

El evento se guarda antes de notificar. El canal administrativo usa SignalR aislado por tenant/sucursal y la app recibe FCM por su usuario. Un push enviado no prueba recepción o lectura: el texto dice que administración tiene el evento registrado. Ante un fallo de push, la evidencia queda disponible en el listado y el error se registra; no se pierde la alerta ni se inventa una confirmación de entrega.

## Compatibilidad y pruebas

Sin cambios de esquema PostgreSQL, retenciones ni infraestructura. Se conserva el aislamiento EF/RLS y la autorización de sucursal. Probar GPS 59/60 s, error de lectura, permiso independiente, salida técnica/manual, recuperación genérica, aviso con cadencia liviana, 90% de acuerdo, saltos GPS, huecos de captura, sede exenta, destino >20 min y reconstrucción que invalida una permanencia. Pruebas de integración y compilación no sustituyen validación en dispositivos físicos.

## Orden de publicación

Validar el head final sin archivos de transferencia ni workflows con permisos de escritura. Publicar primero la API compatible, después administración y finalmente la app por Google Play interno. No desinstalar la aplicación ni borrar su historial local para probar la actualización. Confirmar GPS apagado/encendido, permiso preciso, pantalla bloqueada, recuperación de comunicación y cierre de jornada en teléfonos reales antes de atribuir un caso a una persona.
