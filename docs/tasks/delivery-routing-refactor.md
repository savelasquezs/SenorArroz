# Enrutamiento local desde d03e6a9

## Criterios

- Seleccionar comida lista por antigüedad real; reservas por compromiso, no por su fecha histórica de creación. Numerar las propuestas por su pedido prioritario.
- Rosa de los vientos desde la sucursal, arco circular de 60° como máximo; cercanía y progreso para distinguir corredores sin codificar barrios, IDs ni grupos de prueba. Los 100 m centrales no fijan rumbo; no confundirlos con el radio operativo de 600 m.
- Meta normal de 35 min desde salida, techo de 40 min. Una vez formada la selección, permitir como máximo 5 min adicionales acumulados por entrega al redistribuir. No reiniciar esos márgenes.
- Excepción automática por viaje directo lejano, solo para ese pedido. No necesita permiso y no desplaza pedidos más antiguos; buscar compañía compatible sin retrasar indebidamente las entregas normales. La falta de capacidad sigue siendo un pendiente real.
- Maximizar inclusión sin retirar pedidos antiguos seleccionados. Probar redistribución antes de saltarlos; minimizar salidas individuales y permitir varios domiciliarios hacia el mismo sector. No fabricar cinco rutas si no hay cinco salidas útiles.
- Mantener cocina incierta separada de comida lista, ventanas de espera, reservas, GPS, jornada, tenant/sucursal y rutas aceptadas. Edición solo antes de aceptar.
- Un único presupuesto de búsqueda y una última solución válida. Distinguir exclusiones operativas, búsqueda incompleta y métricas aproximadas de errores reales.
- Conservar web y Android; no introducir autorización manual ni columnas innecesarias.

## Implementación y verificación

1. [x] Descartar cambios sin commit de API y app, con objetivos comprobados.
2. [x] Centralizar horarios, límites y compatibilidad en un helper probado.
3. [x] Separar selección cronológica, refinamiento y redistribución acotada.
4. [x] Actualizar prioridad/reservas, excepciones visibles y motivos de pendientes.
5. [x] Unificar los tres paquetes originales en una única fixture reproducible.
6. [x] Verificar invariantes, regresiones, 3/5 domiciliarios, GPS de regreso y recarga.
7. [x] Dejar API/web local funcionando y entregar rutas con direcciones, tiempos y pendientes. Sin commit, push ni PR.
8. [x] Conservar posteriormente esta primera versión mediante commits locales de API y app, por autorización expresa del usuario. Sin push, PR ni despliegue.

Resultados detallados en [delivery-routing-results.md](delivery-routing-results.md). Backend: 968 aprobadas, 26 omitidas, 0 fallidas. Flutter: 5 aprobadas, análisis limpio y web compilada. Las seis pruebas locales conservaron versión y propuestas al recargar. El navegador muestra las cinco rutas del primer paquete, con direcciones y la excepción de Girardota. No se alteró el arranque Android.
