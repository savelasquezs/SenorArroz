# Ruteo P0/P1: validez y medición temporal

Fecha de preparación: 2026-10-16. Backend PR #36; Flutter `delivery_app` PR #9. Estado: ramas de revisión, sin despliegue ni medición activa.

## Alcance

Reutiliza DeliveryRoutingPlanService, OR-Tools, el estimador de cocina, disponibilidad, Google Compute Routes, notificaciones y notifiers existentes. No Google Route Matrix, nuevas tablas, consultas por cada punto GPS ni saneamiento automático de rutas históricas.

P1 comparte elegibilidad entre generación, preview y representación. Las reservas no listas con preparación posterior a la ventana (900 s predeterminados) no participan. Las reservas realmente listas siguen siendo candidatas. Coordenadas inválidas quedan explicadas, no descartadas silenciosamente. Comida cuya preparación prevista no es próxima queda como no enrutada con motivo. Esto no sustituye la predicción de tiempo restante de P3.

La huella (incluidas ETA de slots/configuración) y la edad máxima del plan se comprueban antes de matriz/solver/Google. La publicación relee candidatos dentro de la transacción. Google se consulta antes de abrir esa transacción; si falla, se retiene la aproximación explícitamente. No cambia la propiedad de ningún pedido.

Salida prevista = máximo entre disponibilidad del vehículo y preparación de todos los pedidos de esa salida. Las llegadas se derivan de esa salida única en sucursal. Una ruta sin listos no recomienda salir ahora. Si esperar un complemento supera la referencia (180 s), se conserva la salida de los listos y se explica por qué el complemento no se incluyó. Comparar globalmente salir/esperar/dividir y congelar límites de espera aceptados pertenece a P4, no está terminado aquí.

P1 protege los dominios nativos del solver: pedidos/vehículos fuera del horizonte no pueden provocar SetMin/SetRange inválidos. La preferencia direccional solo penaliza costo, no tiempo físico. No se afirma óptimo global ni se introduce un nuevo motor.

Flutter depura propuestas y no enrutados por el handler SignalR existente, protege lecturas HTTP en vuelo y conserva el plan viejo como referencia NO reclamable cuando falla la carga. La selección manual permanece. Un timer local invalida recomendaciones a los dos minutos sin hacer polling; es un límite conservador de la app, mientras el backend permite configurar 30–300 s de caché.

## P0: qué mide y qué NO mide

Un evento `RoutingMeasurementV1` por petición de planificación instrumentada, con ID único para desduplicar exportaciones. Contiene tenant/sucursal, algoritmo, ventana del experimento, resultado (generated/reused/failed/cancelled), duración total/espera de lock/entrada, duración del solver guardada en el plan, llamadas/fallos/duración real de Compute Routes y conteos de candidatos/propuestas/no enrutados. No incluye direcciones, teléfonos, nombres, coordenadas, tokens ni mensajes arbitrarios de excepción.

`solverMsOnPlan` solo se exporta al generar; generar un plan vacío puede no ejecutar el solver. No confundir número de planes con invocaciones del solver ni sumar duración histórica en aciertos de caché. No se instrumenta cada muestra GPS, tarjeta o render.

Esta etapa aún NO mide salida física, llegada real del cliente, error de preparación o ahorro causal antes/después. Esos indicadores requieren el modelo operativo de P2/P3. No usar hora de autoasignación o autoentrega recuperada como sustituto de la hora física. Informes siempre con n, cobertura, mediana y P90, segmentados por sucursal y versión; no presentar pocas muestras como mejora demostrada.

## Activación: no iniciar el contador hasta cumplir todo

1. CI del último commit aprobado (incluidos Android relevantes). Revisar diff y regresiones de caja/tenant. Backend antes de app, sin publicar por este documento.
2. Exportar evidencia histórica disponible antes del despliegue. Los logs operativos existentes permiten algunas comparaciones, no una latencia que nunca se midió. Una referencia offline usa el commit anterior y las mismas entradas conocidas en ese instante; no mantener motor viejo en producción.
3. Verificar retención real del destino de logs. Hacer una exportación de prueba y ejecutar el informe. Si no cubre la ventana completa, acordar exportación privada regular antes de habilitar; no instalar otro servicio por defecto.
4. Documentar aquí inicio REAL, fin y commit desplegado; configurar ambas fechas UTC absolutas. Mantenerlas iguales al redeploy. Ventana elegida: 14 días como máximo, no prorrogable automáticamente.
5. Habilitar solo después de verificar muestra recibida y exportable. Un fallo del logger nunca debe afectar asignaciones ni cálculo.

Variables de entorno, sin valores ficticios:

```text
DeliveryRouting__Measurement__Enabled=true
DeliveryRouting__Measurement__StartedAtUtc=<instante-real-UTC-con-Z>
DeliveryRouting__Measurement__ExpiresAtUtc=<inicio-mas-14-dias-con-Z>
```

Predeterminado: Enabled=false, fechas ausentes. Ausencia, orden incorrecto, offsets no UTC o plazo mayor a 14 días deshabilitan la medición. Al llegar exactamente al vencimiento ya no se emiten muestras. Las fechas no se derivan del arranque del proceso.

Registro operativo pendiente:
- Activación UTC: NO ACTIVADA.
- Vencimiento UTC: NO DEFINIDO; será activación +14 días.
- Commit de producción: NO DESPLEGADO.
- Destino/retención/exportación: POR VERIFICAR.
- Recordatorio: creado el 16 de octubre; revisa fecha REAL de activación/vencimiento y avisa al terminar, no modifica código ni despliega.

## Informe

```bash
python scripts/routing_measurement_report.py exportacion.log > reporte.json
```

Acepta JSONL con el payload o líneas de consola con el marcador. Ignora otras líneas, desduplica sampleId y separa tenant/sucursal/algoritmo/ventana. Exportar solo a un destino privado autorizado. `ignoredLines` no mide fallos operativos. La ausencia de logs no cuenta como éxito. El reporte de P0 no demuestra reducción de tiempo de entrega por sí solo.

## Retirada física P6

Al vencer: exportar informe y desactivar; luego PR a main para eliminar:
- RoutingMeasurement.cs y sus llamadas/parámetros en DeliveryRoutingPlanService.
- RoutingMeasurementOptions y DeliveryRoutingOptions.Measurement.
- Variables Measurement del despliegue.
- scripts/routing_measurement_report.py y pruebas exclusivas de diagnóstico/exportador.
- Tareas de exportación temporal que se hayan autorizado.

Antes de borrar, buscar TODOS los consumidores (código, tests, docs, CI y variables). Conservar elegibilidad, ventanas, protección de concurrencia, pruebas funcionales, logs operativos previos, auditoría y tiempos necesarios para el ruteo. Volver a ejecutar suites sin la instrumentación. Apagar una bandera NO completa la retirada.

## Validación y siguiente etapa

Pruebas: reservas futuras vs realmente listas; preview consistente; ruta solo en preparación; complemento demorado; caché antes del solver; ETA cambiante; expiración; cambios durante cálculo; límites OR-Tools; logger fallido; eventos/HTTP desordenados; warning visible y selección manual. Simulación manual de dos dispositivos sigue pendiente hasta pilotar una versión instalada.

P2 pendiente: sanear rutas residuales con revisión/auditoría, identificar ruta y jornada correctas por domiciliario y separar asignación de salida física. No cerrar ni cancelar rutas históricas solo por antigüedad desde P0/P1.
