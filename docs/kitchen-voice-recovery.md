# Cocina: avisos de voz recuperables

La voz sigue siendo la API `speechSynthesis` del navegador. SignalR `NewOrder` y
`ReservationReady` son pistas de actualización, no la fuente de verdad.

## Despliegue (orden obligatorio)

1. Ejecutar con `railway connect MainDatabase` y `\\i SenorArroz.Infrastructure/Scripts/add_kitchen_voice_events.sql`.
2. Comprobar la tabla, índice, políticas de tenant y permisos del rol de runtime.
3. Publicar backend antes que frontend. Backend antiguo no implementa `GET /api/orders/kitchen/voice-events`.
4. Publicar frontend. Una terminal nueva consulta solamente los avisos de sus últimos 2 minutos;
   una terminal existente recupera desde su último cursor guardado para la sucursal y tenant.

La tabla agrega un registro por pedido cuando el backend llama a `NotifyNewOrderToKitchen` o
`NotifyReservationToKitchen` (una sola vez aunque se reintente). No replica pedidos,
pagos ni detalles. Las notificaciones en vivo se conservan aunque falle la inserción,
y el servidor registra el error.

Los terminales solamente avanzan su cursor cuando termina satisfactoriamente el TTS.
Si Chrome rechaza audio, el usuario debe pulsar **Activar sonido**; no se marca como leído
un anuncio que no se ha podido reproducir.

## Pruebas de aceptación

- Iniciar cocina, activar sonido y comprobar que dice **Listo**.
- Crear 2 pedidos sucesivos y comprobar que ambos se escuchan completos.
- Reiniciar el backend durante un pedido; después de reconectar debe anunciarlo una vez.
- Probar cuentas Kitchen, Admin y Superadmin, cada una limitada a su sucursal seleccionada.
- Desconectar la red, crear un pedido en caja y restablecer conexión; debe recuperarse.
- Confirmar que un pedido pagado en web sólo se anuncia cuando entra a cocina.
- Confirmar que una reserva pendiente no suena hasta su hora de entrada en cocina.
