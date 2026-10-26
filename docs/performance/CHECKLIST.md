# Checklist de funcionalidades para auditoría de rendimiento

Versión inicial: 2026-10-12. [Inventario y normas](README.md) · [Plantilla de informe](REVIEW_TEMPLATE.md).

**Importante:** las filas son unidades de análisis iniciales; antes de concluir se verifican pantallas, permisos, API, SQL y comportamiento real. Se puede corregir cada feature de forma autónoma **en rama independiente**, preparar scripts SQL idempotentes y abrir PR(s) a `main` sin aprobación previa. **El propietario conserva la decisión de fusionar, ejecutar migraciones productivas y desplegar.** Cada entrada se actualiza mediante PR documental y con evidencia. Ver [procedimiento completo](README.md) y [plantilla](REVIEW_TEMPLATE.md).

**Estados permitidos:** `Pendiente` · `Analizado` · `PR listo para revisión` · `Mejora aprobada` · `Implementado` · `Validado` · `Bloqueado` · `En desarrollo`. `PR listo para revisión` significa rama, PR y pruebas listos para evaluar; **no significa merge, migración ni deploy**. Si faltan pruebas esenciales, registrar `Bloqueado` y abrir PR borrador si ayuda a revisar.

| ID | Funcionalidad a revisar | Estado de auditoría | Última revisión | Informe | Issue/PR mejora | Verificación |
|---|---|---|---|---|---|---|
| M01-F01 | Login | Pendiente | — | — | — | — |
| M01-F02 | Refresh JWT | Pendiente | — | — | — | — |
| M01-F03 | Logout | Pendiente | — | — | — | — |
| M01-F04 | Recuperación de contraseña | Pendiente | — | — | — | — |
| M01-F05 | Cambio de contraseña | Pendiente | — | — | — | — |
| M01-F06 | Validación y carga de sesión | Pendiente | — | — | — | — |
| M02-F01 | Listado y filtros de usuarios | Pendiente | — | — | — | — |
| M02-F02 | Crear/editar usuario y rol | Pendiente | — | — | — | — |
| M02-F03 | Desactivar/eliminar usuario | Pendiente | — | — | — | — |
| M02-F04 | Perfil y foto | Pendiente | — | — | — | — |
| M02-F05 | Estadísticas de nómina | Pendiente | — | — | — | — |
| M03-F01 | Carga y selección de sucursales | Pendiente | — | — | — | — |
| M03-F02 | CRUD y estadísticas sucursales | PR listo para revisión | 2026-10-21 | [Informe](reviews/2026-10-21-M03-F02.md) | [API #50](https://github.com/savelasquezs/SenorArroz/pull/50) | CI: 1142 aprobadas/1 omitida; 5/5 regresiones nuevas. PostgreSQL aislado: estadísticas de 10 sucursales, 50 COUNT → 3 SELECT agrupados; vacío → 0. Sin migración, merge ni despliegue; métricas productivas pendientes. |
| M03-F03 | Contexto y permisos cross-branch | Pendiente | — | — | — | — |
| M03-F04 | Tenant RLS y contexto PostgreSQL | Pendiente | — | — | — | — |
| M04-F01 | Horarios de atención | Pendiente | — | — | — | — |
| M04-F02 | Barrios, zonas y cobertura | Pendiente | — | — | — | — |
| M04-F03 | Tarifa por dirección/sucursal | Pendiente | — | — | — | — |
| M04-F04 | Lugares autorizados GPS | Pendiente | — | — | — | — |
| M05-F01 | Carga y búsqueda catálogo POS | Pendiente | — | — | — | — |
| M05-F02 | CRUD productos y categorías | Pendiente | — | — | — | — |
| M05-F03 | Precios, presentaciones y fotos | Pendiente | — | — | — | — |
| M05-F04 | Stock y disponibilidad | Pendiente | — | — | — | — |
| M06-F01 | Búsqueda por teléfono | Mejora aprobada | 2026-10-25 | [Informe](reviews/2026-10-25-M06-F01.md) | [Vue #43](https://github.com/savelasquezs/senorArrozFront/pull/43), [API #66](https://github.com/savelasquezs/SenorArroz/pull/66) | Ampliación autorizada: estados de búsqueda, contexto efectivo, abort y sugerencias de 10 con selección completa. CI de implementación: Vue 855/855; API 1363/1363 y PostgreSQL (2 SELECT por página con resultados). Ver informe y revisión final de los PR para SHA/checks/merge posteriores. Sin migración ni métricas/despliegue productivos verificados. |
| M06-F02 | Alta/edición cliente | Pendiente | — | — | — | — |
| M06-F03 | Direcciones y barrio | Pendiente | — | — | — | — |
| M06-F04 | Dirección-servicio por sucursal | Pendiente | — | — | — | — |
| M06-F05 | Historial y estadísticas cliente | PR listo para revisión | 2026-10-19 | [Informe](reviews/2026-10-19-M06-F05.md) | [PR backend #44](https://github.com/savelasquezs/SenorArroz/pull/44) | CI verde; p50/p95 y SQL por medir antes de merge |
| M07-F01 | Carga promociones del día | Pendiente | — | — | — | — |
| M07-F02 | Guardar y desactivar promoción | Pendiente | — | — | — | — |
| M07-F03 | Validar y aplicar cupón | Pendiente | — | — | — | — |
| M07-F04 | Administrar códigos promocionales | Pendiente | — | — | — | — |
| M08-F01 | Consultar progreso/beneficios | Pendiente | — | — | — | — |
| M08-F02 | Aplicar beneficio al pedido | Pendiente | — | — | — | — |
| M08-F03 | Configuración ciclo y niveles | Pendiente | — | — | — | — |
| M09-F01 | Inicialización de pantalla POS | PR listo para revisión | 2026-10-21 | [Informe 2026-10-20 y seguimiento](reviews/2026-10-20-M09-F01.md) | [Vue #33](https://github.com/savelasquezs/senorArrozFront/pull/33) | Build OK; Vitest 582/582, incluidas 20 regresiones POS. Solicitud compartida por sucursal/modo, invalidación de sesión y errores reintentables. Sin merge ni despliegue; medición productiva pendiente. |
| M09-F02 | Búsqueda y selección de productos | Pendiente | — | — | — | — |
| M09-F03 | Borradores y cambio de pestañas | Pendiente | — | — | — | — |
| M09-F04 | Cliente/dirección y cotización | Pendiente | — | — | — | — |
| M09-F05 | Cálculo de descuentos/promociones | Pendiente | — | — | — | — |
| M09-F06 | Selección de pagos y total | Pendiente | — | — | — | — |
| M09-F07 | Guardar pedido y notificaciones | Pendiente | — | — | — | — |
| M10-F01 | Cargar listado del día | Implementado | 2026-10-13 | [Informe 2026-10-13](reviews/2026-10-13-M10-F01.md) | [API #23](https://github.com/savelasquezs/SenorArroz/pull/23) (`e681ae5`), [Vue #17](https://github.com/savelasquezs/senorArrozFront/pull/17) (`1fb28cf`) | CI: 869/869 backend; PostgreSQL COUNT verificado; 854/15 omitidas y 3/3 remoto; Vue 487/487. Railway API y Vue SUCCESS. Sin migración. Pendiente prueba funcional y medición real antes de Validado. |
| M10-F02 | Búsquedas, filtros y paginación | Pendiente | — | — | — | — |
| M10-F03 | Detalle e historial de pedido | Pendiente | — | — | — | — |
| M10-F04 | Editar datos, productos y precios | Pendiente | — | — | — | — |
| M10-F05 | Modificar estados | Pendiente | — | — | — | — |
| M10-F06 | Cancelar/descancelar | Pendiente | — | — | — | — |
| M10-F07 | Cambiar asignación domiciliario | Pendiente | — | — | — | — |
| M10-F08 | Pagos y reimpresión | Pendiente | — | — | — | — |
| M11-F01 | Seleccionar sede destino al tomar | Pendiente | — | — | — | — |
| M11-F02 | Crear pedido cross-branch | Pendiente | — | — | — | — |
| M11-F03 | Listado de pedidos por sede origen | Pendiente | — | — | — | — |
| M11-F04 | Editar y seguir pedido remoto | Pendiente | — | — | — | — |
| M11-F05 | Permisos y concurrencia remota | Pendiente | — | — | — | — |
| M12-F01 | Listado reservas y filtros | PR listo para revisión | 2026-10-25 | [Informe](reviews/2026-10-25-M12-F01.md) | [API #64](https://github.com/savelasquezs/SenorArroz/pull/64), [Vue #42](https://github.com/savelasquezs/senorArrozFront/pull/42), [Docs #65](https://github.com/savelasquezs/SenorArroz/pull/65) | API [CI](https://github.com/savelasquezs/SenorArroz/actions/runs/40199143317) 1357/1357, 2 regresiones nuevas incluyendo PostgreSQL; Vue [CI](https://github.com/savelasquezs/senorArrozFront/actions/runs/40199147929) 835/835, 4 regresiones nuevas. Filtro de reservas activas y búsqueda antes de paginar; sin SQL, merge ni despliegue. Publicar backend antes de frontend. |
| M12-F02 | Crear/editar reserva | Pendiente | — | — | — | — |
| M12-F03 | Abonos y saldos | Pendiente | — | — | — | — |
| M12-F04 | Vencimiento y estados reservas | Pendiente | — | — | — | — |
| M13-F01 | Carga cola cocina | PR listo para revisión | 2026-10-15 | [Informe 2026-10-15](reviews/2026-10-15-M13-F01.md) | [API #25](https://github.com/savelasquezs/SenorArroz/pull/25), [Vue #18](https://github.com/savelasquezs/senorArrozFront/pull/18), [Docs #26](https://github.com/savelasquezs/SenorArroz/pull/26) | Backend CI 872/872; Remote 856 aprobadas/16 omitidas + PostgreSQL 3/3; frontend CI 493/493, build OK. Sin migración ni despliegue; pendiente validación funcional y métricas productivas. |
| M13-F02 | Tomado a preparación | Pendiente | — | — | — | — |
| M13-F03 | Preparación a listo | Pendiente | — | — | — | — |
| M13-F04 | Notificaciones cocina y reimpresión | Pendiente | — | — | — | — |
| M14-F01 | Encolar comandas | Mejora aprobada | 2026-10-16 | [Informe](reviews/2026-10-16-M14-F01.md) | [API #29](https://github.com/savelasquezs/SenorArroz/pull/29) (fusionado), [Agente #1](https://github.com/savelasquezs/senorArrozPrintAgent/pull/1) (fusionado), [Docs #30](https://github.com/savelasquezs/SenorArroz/pull/30) | Backend 885/885 incl. PostgreSQL; Remote 862 aprobadas/23 omitidas + PG 3/3; agente 15/15. Sin SQL; pendiente confirmar publicación y prueba física antes de Implementado/Validado. |
| M14-F02 | Poll y claim del agente Windows | PR listo para revisión | 2026-10-17 | [Informe 2026-10-17](reviews/2026-10-17-M14-F02.md) | [PR único API #38](https://github.com/savelasquezs/SenorArroz/pull/38) | Apertura EF establece tenant RLS en claim específico; auth proyecta 3 campos; orden de claim determinista. [Backend CI #191](https://github.com/savelasquezs/SenorArroz/actions/runs/39052260958) 919/919 incl. nueva regresión PostgreSQL RLS; [remotos #70](https://github.com/savelasquezs/SenorArroz/actions/runs/39052261022) 894/919 (25 omitidas) + PG 3/3. Sin SQL ni despliegue. |
| M14-F03 | Notificaciones SignalR del agente | Pendiente | — | — | — | — |
| M14-F04 | Confirmar, fallar y reintentar impresión | Pendiente | — | — | — | — |
| M14-F05 | Configuración y reimpresión | Pendiente | — | — | — | — |
| M15-F01 | Pedidos disponibles para despacho | Pendiente | — | — | — | — |
| M15-F02 | Asignar y desasignar domiciliario | Pendiente | — | — | — | — |
| M15-F03 | Tablero y filtro de asignados | Pendiente | — | — | — | — |
| M15-F04 | Seguimiento de entrega | Pendiente | — | — | — | — |
| M16-F01 | Solicitar plan de rutas | Pendiente | — | — | — | — |
| M16-F02 | Previsualizar propuestas | Pendiente | — | — | — | — |
| M16-F03 | Recalcular y reclamar rutas | Pendiente | — | — | — | — |
| M16-F04 | Google Routes y OR-Tools | Pendiente | — | — | — | — |
| M17-F01 | Cargar disponibles Flutter | Pendiente | — | — | — | — |
| M17-F02 | Cargar sugerencias y próximos | Pendiente | — | — | — | — |
| M17-F03 | Autoasignación y bloqueo concurrente | Pendiente | — | — | — | — |
| M18-F01 | Cargar y refrescar ruta activa | Pendiente | — | — | — | — |
| M18-F02 | Cambiar estado entregas | Pendiente | — | — | — | — |
| M18-F03 | Abrir navegación y contacto cliente | Pendiente | — | — | — | — |
| M18-F04 | Reimprimir y consultar detalle | Pendiente | — | — | — | — |
| M19-F01 | Inicio/cierre jornada | Pendiente | — | — | — | — |
| M19-F02 | Scheduler GPS activo e inactivo | Pendiente | — | — | — | — |
| M19-F03 | Validación y envío de ubicación | Pendiente | — | — | — | — |
| M19-F04 | Cola offline, reintentos y sincronización | Pendiente | — | — | — | — |
| M19-F05 | Eventos dispositivo y permisos | Pendiente | — | — | — | — |
| M20-F01 | Consultar última ubicación | Pendiente | — | — | — | — |
| M20-F02 | Playback y puntos históricos | Pendiente | — | — | — | — |
| M20-F03 | Detección de permanencia | Pendiente | — | — | — | — |
| M20-F04 | Alertas y resolución | Pendiente | — | — | — | — |
| M20-F05 | Incidentes/evidencias/revisión | Pendiente | — | — | — | — |
| M21-F01 | Resumen diario de domiciliarios | Pendiente | — | — | — | — |
| M21-F02 | Registrar/modificar anticipos | Pendiente | — | — | — | — |
| M21-F03 | Liquidación y desbloqueo | Pendiente | — | — | — | — |
| M21-F04 | Consulta de recaudos y entregas | Pendiente | — | — | — | — |
| M22-F01 | Historial filtrado por fecha | Pendiente | — | — | — | — |
| M22-F02 | Dashboard métricas Flutter | Pendiente | — | — | — | — |
| M22-F03 | Ganancias, fees y tiempos | Pendiente | — | — | — | — |
| M23-F01 | Listado y edición bancos | Pendiente | — | — | — | — |
| M23-F02 | Configuración de aplicaciones | Pendiente | — | — | — | — |
| M23-F03 | Consulta de saldos y movimientos | Pendiente | — | — | — | — |
| M24-F01 | Registrar y editar pago bancario | Pendiente | — | — | — | — |
| M24-F02 | Verificar/desverificar transferencias | Pendiente | — | — | — | — |
| M24-F03 | Pagos apps y liquidaciones | Pendiente | — | — | — | — |
| M24-F04 | Efectivo en tienda | Pendiente | — | — | — | — |
| M24-F05 | Concurrencia y consistencia contable | Pendiente | — | — | — | — |
| M25-F01 | Calcular efectivo esperado | Pendiente | — | — | — | — |
| M25-F02 | Cerrar caja | Pendiente | — | — | — | — |
| M25-F03 | Historial y auditoría de cierre | Pendiente | — | — | — | — |
| M25-F04 | Movimientos de caja fuerte | Pendiente | — | — | — | — |
| M25-F05 | Préstamos y conciliación | Pendiente | — | — | — | — |
| M26-F01 | Listado, filtros y resumen gastos | Pendiente | — | — | — | — |
| M26-F02 | Crear/editar gasto y conceptos | Pendiente | — | — | — | — |
| M26-F03 | Categorías y encabezados | Pendiente | — | — | — | — |
| M26-F04 | Imputación gastos y menú | Pendiente | — | — | — | — |
| M27-F01 | Listado y filtros proveedores | Pendiente | — | — | — | — |
| M27-F02 | Alta/edición y gastos vinculados | Pendiente | — | — | — | — |
| M28-F01 | Consulta de balances | Pendiente | — | — | — | — |
| M28-F02 | Movimientos y disponibilidad | Pendiente | — | — | — | — |
| M28-F03 | Recetas por producto | Pendiente | — | — | — | — |
| M28-F04 | Ajustes de existencias | Pendiente | — | — | — | — |
| M28-F05 | Conteo y confirmación | Pendiente | — | — | — | — |
| M28-F06 | Transferencias y recepción | Pendiente | — | — | — | — |
| M28-F07 | Reportes y desviaciones | Pendiente | — | — | — | — |
| M29-F01 | Carga general dashboard | PR listo para revisión | 2026-10-23 | [Informe](reviews/2026-10-23-M29-F01.md) | [Vue #38](https://github.com/savelasquezs/senorArrozFront/pull/38) | Carrera de solicitudes Principal y ventas vs gastos; cancelación + versionado y 4 pruebas agregadas. [CI](https://github.com/savelasquezs/senorArrozFront/actions/runs/39969574606): build OK y 746/746 pruebas (4 nuevas); sin merge, mediciones productivas ni SQL. |
| M29-F02 | KPIs ventas/gastos | Pendiente | — | — | — | — |
| M29-F03 | Evolución y comparativos | Pendiente | — | — | — | — |
| M29-F04 | Ventas por productos/categorías | Pendiente | — | — | — | — |
| M29-F05 | Rendimiento de domicilios | Pendiente | — | — | — | — |
| M29-F06 | Comparación entre sucursales | Pendiente | — | — | — | — |
| M30-F01 | Listado bandeja y no leídos | Analizado | 2026-10-18 | [Informe](reviews/2026-10-18-M30-F01.md) | [PR API](https://github.com/savelasquezs/SenorArroz/pull/41) | Consulta agrupada y filtros compartidos; CI y frontend pendientes. Sin SQL productivo. |
| M30-F02 | Carga mensajes y adjuntos | Pendiente | — | — | — | — |
| M30-F03 | Enviar texto/medios/menú | Pendiente | — | — | — | — |
| M30-F04 | Plantillas y respuestas rápidas | Pendiente | — | — | — | — |
| M30-F05 | Tomar/transferir/cerrar conversación | Pendiente | — | — | — | — |
| M31-F01 | Webhooks y enrutamiento conversación | Pendiente | — | — | — | — |
| M31-F02 | IA: contexto, modelo y prompts | Pendiente | — | — | — | — |
| M31-F03 | Extracción de pedido y borrador | Pendiente | — | — | — | — |
| M31-F04 | Métricas, consumo y diagnósticos | Pendiente | — | — | — | — |
| M31-F05 | Workers de mensajes y reintentos | Pendiente | — | — | — | — |
| M32-F01 | Carga catálogo público y medios | Pendiente | — | — | — | — |
| M32-F02 | Carrito y estado local | Pendiente | — | — | — | — |
| M32-F03 | OTP y sesión cliente | Pendiente | — | — | — | — |
| M32-F04 | Geocobertura, direcciones y cotización | Pendiente | — | — | — | — |
| M32-F05 | Checkout y creación del pedido | Pendiente | — | — | — | — |
| M32-F06 | Estado del pago y reintentos | Pendiente | — | — | — | — |
| M33-F01 | Configuración Wompi sucursal | Pendiente | — | — | — | — |
| M33-F02 | Crear, verificar y reintentar pago | Pendiente | — | — | — | — |
| M33-F03 | Procesar webhooks | Pendiente | — | — | — | — |
| M33-F04 | Revisiones y conciliación | Pendiente | — | — | — | — |
| M34-F01 | Recepción y mapeo webhooks | Pendiente | — | — | — | — |
| M34-F02 | Listado/aceptación/rechazo de pedidos | Pendiente | — | — | — | — |
| M34-F03 | Sincronización estado y trabajadores | Pendiente | — | — | — | — |
| M34-F04 | Publicación de catálogo y disponibilidad | Pendiente | — | — | — | — |
| M35-F01 | Publicación feed Meta | Pendiente | — | — | — | — |
| M35-F02 | Captura de eventos y funnel | Pendiente | — | — | — | — |
| M35-F03 | Outbox conversiones y reintentos | Pendiente | — | — | — | — |
| M35-F04 | Diagnóstico Meta CAPI | Pendiente | — | — | — | — |
| M36-F01 | Listado/editorial de artículos | Pendiente | — | — | — | — |
| M36-F02 | Preview y publicación Notion | Pendiente | — | — | — | — |
| M36-F03 | API pública del blog | Pendiente | — | — | — | — |
| M36-F04 | Rutas SEO y metadatos | Pendiente | — | — | — | — |
| M37-F01 | Listado y descarga documentos | Pendiente | — | — | — | — |
| M37-F02 | Crear/editar/eliminar documentos | Pendiente | — | — | — | — |
| M37-F03 | Acceso público por enlace | Pendiente | — | — | — | — |
| M38-F01 | Conexión inicial SignalR | Pendiente | — | — | — | — |
| M38-F02 | Reconexión y limpieza listeners | Mejora aprobada | 2026-10-24 | [Informe](reviews/2026-10-24-M38-F02.md) | [Vue #41](https://github.com/savelasquezs/senorArrozFront/pull/41) fusionado, [Docs #61](https://github.com/savelasquezs/SenorArroz/pull/61) | Merge autorizado `c7eda21`; head `984e219` verificado: [CI](https://github.com/savelasquezs/senorArrozFront/actions/runs/40111026784) 831/831 tests, 45 del cambio; [Chromium](https://github.com/savelasquezs/senorArrozFront/actions/runs/40111026798) OK. Sincronización inicial segura, eventos por ID/versión y recargas seriales. Sin SQL; despliegue y métricas productivas aún no verificados. |
| M38-F03 | Difusión eventos pedidos | Pendiente | — | — | — | — |
| M38-F04 | Difusión WhatsApp/impresión | Pendiente | — | — | — | — |
| M38-F05 | Aislamiento grupos por sucursal/tenant | Pendiente | — | — | — | — |
| M39-F01 | Registro/eliminación FCM tokens | Pendiente | — | — | — | — |
| M39-F02 | Push entrega y asignaciones | Pendiente | — | — | — | — |
| M39-F03 | Avisos de pagos/cambios | Pendiente | — | — | — | — |
| M40-F01 | Outbox de WhatsApp y Meta | Pendiente | — | — | — | — |
| M40-F02 | Worker de Rappi | Pendiente | — | — | — | — |
| M40-F03 | Workers de pagos y correos | Pendiente | — | — | — | — |
| M40-F04 | Workers GPS y cierre jornadas | Pendiente | — | — | — | — |
| M40-F05 | Carga recurrente y ciclos de polling | Pendiente | — | — | — | — |
| M41-F01 | SQL pedidos y detalles | Pendiente | — | — | — | — |
| M41-F02 | SQL clientes y direcciones | Pendiente | — | — | — | — |
| M41-F03 | SQL dashboard y agregados | Pendiente | — | — | — | — |
| M41-F04 | SQL tracking geográfico | Pendiente | — | — | — | — |
| M41-F05 | Índices, planes EXPLAIN y paginación | Pendiente | — | — | — | — |
| M41-F06 | Pooling conexiones, RLS y concurrencia | Pendiente | — | — | — | — |
| M41-F07 | Backups, mantenimiento y limpieza | Pendiente | — | — | — | — |
| M42-F01 | Uso CPU/RAM por servicio | Pendiente | — | — | — | — |
| M42-F02 | Network egress Railway | Pendiente | — | — | — | — |
| M42-F03 | Despliegue y logs API | Pendiente | — | — | — | — |
| M42-F04 | CI pipelines y pruebas | Pendiente | — | — | — | — |
| M42-F05 | Costos, alertas y límites | Pendiente | — | — | — | — |

## Cómo registrar cambios y resultados diarios
- Al iniciar: seleccionar una única funcionalidad `Pendiente`, priorizando impacto y consultando informes y PR anteriores.
- **Siempre intentar corregir en rama nueva** tras demostrar causa raíz. Preparar pruebas y PR a `main` en cada repositorio afectado; evitar cambios especulativos.
- `Analizado`: evidencia registrada, falta una corrección demostrable o justificación para no modificar.
- `PR listo para revisión`: código modificado en ramas independientes y PR(s) a `main`, tests pertinentes correctos y evidencia de mejora o límites claramente descritos; enlazar cada PR y script de BD preparado.
- `Bloqueado`: ausencia de herramienta/pruebas/datos o cambio riesgoso sin verificación; documentar bloqueo, mantener PR en borrador, nunca afirmar listo.
- `Mejora aprobada`: el propietario autorizó la propuesta; no implica despliegue.
- `Implementado`: ya hubo merge y despliegue/migración correspondiente, verificado.
- `Validado`: comprobar funcionamiento y mejora mediante comparaciones reales o justificar ausencia de cambios.
- Cada cambio de estado se presenta en PR documental, incluso si el código se modifica en Flutter, Vue, Next.js o Print Agent.
- **No fusionar PR ni alterar PostgreSQL productivo automáticamente**. El propietario hará el merge y las migraciones; documentar backup, reejecución, rollback y el orden SQL/backend/frontend cuando aplique.
- M11-F04 y M11-F05 pueden depender del PR #19; no confundir un feature abierto en PR con algo publicado.
- Si el feature fue modificado antes en otra rama, comparar el estado de `main` y PR existentes para no duplicar trabajo.