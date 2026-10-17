# P2 — Jornada, ruta vigente y flota

Estado de preparación: PR #37, pendiente de validación y despliegue. P0/P1: backend #36 y Flutter #9 integrados con todos los checks aprobados.

## Contrato operativo

`DeliveryRouteScope` unifica ruta actual en captura GPS, agrupación de pedidos y disponibilidad. Una ruta nueva se vincula a la jornada activa del mismo tenant/sucursal/domiciliario. Las filas históricas mantienen WorkSessionId nulo: solo puede vincularse una ruta no vacía cuya última asignación y reloj operativo pertenezcan a la jornada activa. Las rutas antiguas no se rejuvenecen al recibir GPS nuevo. La ausencia de ruta válida con pedidos pendientes es una incidencia, no un domiciliario libre.

Una asignación de caja anterior al inicio de jornada puede producir una ruta no vinculada del mismo día; no concede capacidad automática ni permite adjuntar GPS de otra jornada. Debe revisarse la vinculación si el pedido se asignó antes de comenzar la jornada del dispositivo. No se añade un bypass a los filtros tenant.

Estados de flota: available, loading, delivering, returning, probableAtBranch, away, unknown, inconsistent, unavailable. Cada persona aparece una vez, usando su última jornada activa. La lectura GPS más reciente debe ser válida; no se busca un punto más viejo y cómodo si la lectura actual es imprecisa. Un punto liviano entre cadencias puede conservar presencia probable sin prometer capacidad inmediata confirmada. Para regreso se utiliza ETA con margen explícito; fallo de Google degrada a aproximación, no rompe la planificación.

`GET /api/delivery-routing/fleet` está autorizado a los mismos roles del plan y utiliza IBranchContext. Devuelve identidad operativa y estado, no coordenadas, teléfono, correo ni perfil privado. P4 integrará esta flota en la decisión conjunta y P5 en los controles del cliente.

## Persistencia / mapa de BD

Migración aditiva `SenorArroz.Infrastructure/Scripts/add_routing_operational_session.sql`:
- delivery_route.work_session_id nullable.
- delivery_route.departed_at_utc timestamptz nullable.
- delivery_route.departure_source varchar(16) nullable; gps o confirmed con timestamp.
- FK compuesta (tenant_id,work_session_id,deliveryman_id,branch_id) a delivery_work_session con índice único de alcance; índice de consulta por tenant/jornada/status.

No se modifica RLS ni la política de tenant. La FK RESTRICT conserva jornadas referenciadas por historia de rutas. No se encontró consumidor que borre esas jornadas; la retención de puntos GPS es independiente.

Esquema antes de aplicación: respaldar/verificar copia, aplicar SQL con lock_timeout=5s y statement_timeout=60s, comprobar columnas/FK/índices/RLS. Revertir código conserva estas columnas compatibles; no borrar datos al revertir. No hay EF migration ni alteración de pedidos como parte del DDL. El archivo `../local-init-completo.sql`, en la raíz del workspace, incluye el script puntual. En esta ejecución el trabajo es exclusivamente local.

Los puntos históricos solo conservan la ruta enviada cuando coinciden tenant, sucursal, domiciliario, jornada e intervalo de captura. Una asociación inválida se descarta sin perder el punto y sin ejecutar autoentrega ni salida física.

## Salida y cierre

DepartedAtUtc no reemplaza RouteStartedAtUtc: el segundo sigue siendo el reloj legacy para métricas previas. Salida física requiere presencia interior previa y dos puntos exteriores confiables, separados 15–90 s, registrados después de crear la ruta, misma jornada. Se audita la primera muestra exterior observada. La consolidación no inventa salida.

Un cierre administrativo al iniciar otro viaje conserva paradas y estados de pedidos, registra su fuente y NO inventa CompletedAtUtc, duración ni cumplimiento SLA. Las paradas entregadas no se eliminan de una ruta mixta al consolidarla.

Saneamiento de rutas históricas: separar preview y aplicación, limitarse a filas activas sin pedidos pendientes y sin jornada actual, conservar datos originales en auditoría, no tocar estados de pedidos ni fabricar tiempos. El resultado y el respaldo se documentarán tras verificar datos de producción; no está ejecutado por este archivo.

## Pruebas

Regresiones de ruta previa, sesión reemplazada, GPS fuera de orden, presencia probable, posición imprecisa, capacidad sin duplicados, regreso aproximado, salida observada idempotente, agrupación en ruta y preservación de historia. PostgreSQL real aplica el script dos veces, verifica RLS y rechaza asociaciones cross-tenant/driver/branch y salida sin fuente. La prueba de retorno GPS automática existente continúa vigente.

P3 queda pendiente de la aprobación y despliegue de este PR. No habilitar la instrumentación de 14 días antes de validar retención/exportación y escribir T0 real.
