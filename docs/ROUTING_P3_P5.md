# Ruteo P3–P5 local

Implementación sobre `local/routing-p2` (`471a38f`), desarrollada y validada primero en `local/routing-p3-p5` e integrada en `main` por solicitud del usuario. La preparación y publicación de producción requieren autorización separada; la entrega autorizada se registra al final.

## Predicción y capacidad

`KitchenPreparationEstimator` reemplaza el promedio fijo por percentiles condicionados al tiempo transcurrido en la fase actual. Proyecta hasta 2000 pedidos recientes de la sucursal; prioriza cantidad, productos y franja cuando alcanza la muestra mínima. Solo usa observaciones de Listo ya ocurridas. Devuelve ETA mediana, referencia conservadora P90, probabilidad dentro de la ventana y confianza. Una preparación atrasada nunca se declara Lista por vencer su estimación. Sin observaciones supervivientes usa tiempo restante positivo y confianza baja; la probabilidad neutral del fallback no implica calibración demostrada.

La flota mantiene una sola entrada y un solo slot por domiciliario. Una ruta aceptada que carga o entrega conserva pedidos/orden y aporta disponibilidad futura estimada sobre sus paradas pendientes. GPS sin precisión o fuera de cadencia no promete capacidad. El regreso directo usa Google cuando corresponde, caché en memoria de 30 segundos por tenant/sucursal/jornada/punto y aproximación con margen si falla. La ETA sigue siendo una predicción; no prueba llegada física.

## Optimización

Se conserva OR-Tools, matriz local y validación Compute Routes de finalistas. Las restricciones vinculan preparación con salida en sucursal; no permiten esperar cocina durante el recorrido. Cada pedido Listo aporta un límite de espera basado en su timestamp persistido, con creación como fallback conservador. Un recálculo no reinicia ese límite. El solver compara implícitamente salir, esperar y dividir dentro de la función objetivo, con costo de salida configurable, edad del pedido, penalización por espera/retraso y visitas opcionales. No se afirma óptimo global: la búsqueda tiene tiempo acotado.

Los complementos con probabilidad inferior al 60% no comparten vehículo con comida lista. Las rutas aceptadas quedan fuera de los pedidos candidatos y consumen la disponibilidad correspondiente. Cualquier fallo controlable del solver conserva la selección manual. Las preferencias de dirección y penalizaciones permanecen separadas del tiempo físico.

## Edición y aceptación

Flutter permite quitar, agregar pedidos listos y cambiar el orden en un único editor antes de aceptar. La dirección es principal; barrio y número son secundarios. Quitar y tomar de otra propuesta exigen confirmación. El preview se consulta al abrir y después de cada edición con debounce; no agrega polling ni otra suscripción SignalR. Se ignoran respuestas anteriores y solo la estimación vigente permite aceptar. Un fallo ofrece reintentar; un plan vencido exige actualizar las propuestas. Se conservan jornada y GPS obligatorios.

Tarjetas y editor muestran distancia, duración con todas las paradas y hora local aproximada hasta terminar la última entrega y hasta regresar a sucursal. El regreso es acumulado desde salida y añade solo el tramo dirigido desde la última parada a la sucursal; no duplica la ida ni la espera previa. Ese tramo se calcula con la matriz disponible, se marca como aproximado y se vuelve a calcular al cambiar la última parada. El contrato es aditivo y no necesita columnas nuevas.

El preview devuelve una huella del conjunto ordenado y valida la versión vigente. La aceptación valida de nuevo versión/edad del plan, tenant/sucursal, estado Ready, ausencia de asignación y la huella; conserva transacción serializable y convierte conflictos de serialización, deadlock o concurrencia en 409. Se audita la aceptación y sus IDs ordenados. Los DTOs exponen `deliveryRouteStopSequence` para conservar ese orden en reconexiones y refrescos. La ruta aceptada no tiene editor del domiciliario.

Los clientes anteriores conservan el contrato de reclamo exacto y selección manual. Los campos nuevos son aditivos; no se cambió el mínimo de versión ni la publicación.

## Instalación local y futura entrega

Aplicar `add_routing_operational_session.sql` y `add_routing_prediction_snapshots.sql` sobre el esquema multitenant existente antes de publicar. Ambos son aditivos e idempotentes. El SQL raíz incluye creación V1 antes de aislamiento y los scripts puntuales después. Las pruebas usan bases aisladas desechables y no aplican scripts en producción. Revertir código conserva columnas y datos.

## Medición temporal

Permanece deshabilitada por defecto. Conserva fechas UTC absolutas y máximo de 14 días; redeploy no reinicia la ventana. `RoutingMeasurementV1` agrega snapshots de preparación, espera, exclusión y regreso sin datos personales. `RoutingObservationV1` registra Listo, aceptación, salida observada y regreso con IDs operativos y timestamps. Las llamadas reales a Google para regreso usan observaciones `googleReturn` separadas de las validaciones de finalistas; aciertos de caché no incrementan llamadas.

El exportador desduplica IDs de muestra y separa solicitudes de observaciones. Informa cobertura, mediana/P90 de Listo a salida, espera recomendada/real, pedidos por ruta, error absoluto de cocina/regreso y aceptación modificada. La muestra de Listo a salida exige eventos emparejados; ausencia de GPS o exportación no implica éxito. Los conteos de propuestas generadas no son impresiones vistas ni una demostración de ahorro causal. Retención/exportación, activación real y calibración operativa quedan para el piloto autorizado.

P6 debe retirar también `RoutingMeasurement.Observe`, sus llamadas/opciones opcionales, `RoutingObservationV1` y el exportador extendido. Los snapshots funcionales y sus columnas permanecen porque sostienen las recomendaciones y su interfaz.

## Evidencia local

- Suite completa API: 943 aprobadas, 0 fallidas y 0 omitidas, con PostgreSQL aislado y la prueba Flow habilitada sobre una base vacía local.
- Suite Flutter: 153 aprobadas. Editor verificado a 390×844, actualización SignalR durante preview y conservación del orden aceptado.
- Scripts P2 y snapshots P3 ejecutados dos veces en PostgreSQL; validación FK tenant/jornada/sucursal/domiciliario y conservación de RLS.
- Simulaciones de 8 y 16 pedidos en varias zonas, con 1 y 3 domiciliarios, reservas futuras y preparación incierta. No se duplican pedidos ni se asignan durante la planificación.
- Carrera de dos domiciliarios sobre una selección editada: un solo ganador; recálculo posterior conserva las paradas aceptadas.
- La versión publicada, GPS físico, tráfico real, retención remota y medición en producción requieren piloto posterior. No se afirma mejora operativa calibrada a partir de pruebas sintéticas.

## Revisión para integración local a main — 2026-10-18

- Flutter: 174 pruebas aprobadas y análisis limpio en el módulo modificado; compilación web y recorrido del editor comprobados contra `http://127.0.0.1:5000`.
- API: suite completa con 969 aprobadas, 26 omitidas por requisitos de integración y ninguna fallida. Se ejecutaron además las pruebas PostgreSQL de aceptación concurrente y esquema aditivo/idempotente: 2 aprobadas sobre contenedores desechables, sin usar la base de desarrollo operativa ni producción.
- Exportador temporal: 6 pruebas Python aprobadas.
- Los scripts `add_routing_operational_session.sql` y `add_routing_prediction_snapshots.sql` siguen siendo prerrequisito para publicar el backend. Este merge local no los aplica en producción ni publica una nueva app Android.

## Preparación de producción autorizada — 2026-10-18

- Se integraron los cambios nuevos de `origin/main` sin sobrescribirlos: API hasta `8209f31` y app hasta `1261d7d`. La API integrada pasó 979 pruebas; 35 pruebas dependientes de entornos de integración quedaron omitidas.
- El usuario autorizó expresamente aplicar los SQL aditivos y subir `main`, incluyendo el despliegue de Railway y la publicación de Google Play interno configurados por push.
- Se comprobó que faltaban las ocho columnas operativas/predictivas. En `MainDatabase`, entorno `production`, se ejecutaron desde Bash mediante `railway connect MainDatabase` los scripts `add_routing_operational_session.sql` y `add_routing_prediction_snapshots.sql`, ambos con `COMMIT` correcto y sus límites de bloqueo/ejecución.
- Se verificaron las ocho columnas, los dos índices y las dos restricciones validadas. Las tres tablas conservan RLS habilitado y forzado. Los conteos antes/después fueron idénticos: 4079 rutas, 3593 propuestas y 16181 paradas de propuestas. No se ejecutaron semillas, reinicios ni cambios de coordenadas/pedidos de prueba en producción.
