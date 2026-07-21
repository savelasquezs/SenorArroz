# Pruebas controladas del seguimiento de domiciliarios

## Cobertura automatizada

- Inicio y reanudación de jornada con cierre de Colombia.
- Bloqueo de inicio después del cierre o de una liquidación total.
- Cambio de dispositivo y cierre de la jornada anterior.
- Modos `LIGHT`, `ACTIVE_DELIVERY` y `OFFLINE`.
- Idempotencia de ubicaciones y eventos enviados desde colas offline.
- Rechazo de puntos capturados fuera de la jornada.
- Cierre automático al sincronizar después de la hora límite.
- Permanencias válidas, movimiento fuera del radio y precisión GPS insuficiente.
- Clasificación en sucursal, destino, lugar autorizado, inesperado y GPS no confiable.
- Copia estable de evidencia y margen anterior/posterior.
- Alertas, recuperación automática y resumen del correo diario.
- Retención segura de ubicaciones e incidentes en `cleaning_database_service`.

## Pruebas con dispositivo Android

Ejecutar con una sucursal de pruebas y un domiciliario sin pedidos reales.

1. **Inicio normal:** iniciar sesión, aceptar permisos y confirmar la notificación permanente de seguimiento.
2. **Modo liviano:** esperar dos intervalos y comprobar puntos `light` asociados a la jornada.
3. **Pedido activo:** asignar un pedido en camino y comprobar puntos `active_delivery` asociados a la ruta.
4. **Permanencia:** permanecer más del límite configurado en sucursal, destino y un tercer lugar; revisar sus clasificaciones.
5. **Sin internet:** activar modo avión, esperar al menos tres capturas, recuperar internet y confirmar hora de captura, ausencia de duplicados y alerta informativa de sincronización.
6. **GPS y permiso:** apagar/encender GPS y retirar/recuperar permiso; comprobar eventos y resolución automática de alertas.
7. **Batería:** probar con 15 % y confirmar un único evento de batería baja.
8. **Reinicio:** reiniciar Android durante la jornada y comprobar que el servicio vuelve a ejecutarse y conserva la misma jornada.
9. **Cierre forzado:** forzar detención, abrir de nuevo la aplicación y comprobar el evento `app_stopped` sin duplicarlo.
10. **Cierre sin internet:** dejar el teléfono offline hasta superar la hora límite y confirmar que el servicio se detiene localmente.
11. **Liquidación total:** liquidar sin pedidos activos, confirmar cierre de sesión y ausencia de puntos posteriores.
12. **Cambio de hora:** modificar hora o zona del teléfono; el corte debe seguir usando el instante UTC descargado, sin prolongar la jornada.

## Verificación posterior

- Revisar mapa, precisión, batería, GPS, internet y tiempos de captura/sincronización.
- Confirmar que una falla técnica se presenta como contexto y no como infracción automática.
- Ejecutar la limpieza con datos vencidos y verificar que no elimine puntos pendientes ni evidencia incompleta.
