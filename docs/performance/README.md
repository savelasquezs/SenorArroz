# Inventario maestro de rendimiento — El Señor Arroz

Fecha inicial: 2026-10-12. Fuente de verdad: esta carpeta del repositorio backend. Alcance inter-repositorio: [API](https://github.com/savelasquezs/SenorArroz), [Vue](https://github.com/savelasquezs/senorArrozFront), [Flutter](https://github.com/savelasquezs/delivery_app), [Next.js](https://github.com/savelasquezs/senor-arroz-web-nextjs), [Print Agent](https://github.com/savelasquezs/senorArrozPrintAgent).

## Objetivo
Disminuir latencia, llamadas redundantes al backend, consultas y transferencia PostgreSQL, consumo de CPU/RAM, tráfico saliente y costos, **sin deteriorar funcionalidades, permisos, aislamiento entre sucursales/tenants ni exactitud financiera**.

## Cómo trabajar cada día
1. Leer este archivo, [CHECKLIST.md](CHECKLIST.md) y los informes en [reviews/](reviews/).
2. Elegir una funcionalidad **pendiente** priorizando frecuencia, costo y riesgo. No confundir módulo completo con feature.
3. Auditar **solo lectura** en todos los repositorios implicados; registrar rama/commit, rutas y endpoints. Ante falta de datos productivos, declararlo; no inventar métricas.
4. Medir o instrumentar de forma no invasiva: solicitudes por flujo, latencias p50/p95, tamaño de respuestas, query count, SQL/EXPLAIN, RAM/CPU/egress, retries y estabilidad. Separar hipótesis de hallazgos.
5. Registrar diagnóstico en `reviews/YYYY-MM-DD-Mxx-Fyy.md` con plantilla [REVIEW_TEMPLATE.md](REVIEW_TEMPLATE.md); actualizar fila del checklist a **Analizado**, enlazando informe.
6. **No modificar código, BD o despliegues sin aprobación explícita del usuario**. Tras aprobar, abrir issue/PR por mejora, validar pruebas y resultados antes/después; marcar Implementado y posteriormente Validado. Cuando no hay mejora necesaria, documentar evidencia y cerrar como Validado sin cambios.
7. Un feature por sesión. Si es demasiado grande, subdividir manteniendo referencias. Evitar alterar rutas ajenas.
8. Usar PR a `main` para cambios documentales o de código. No hacer merge sin aprobación.

## Estados del checklist
- `Pendiente`: existe en inventario, sin diagnóstico técnico.
- `Analizado`: revisión hecha, informe enlazado.
- `Mejora aprobada`: el usuario aprobó una propuesta concreta.
- `Implementado`: PR fusionado/desplegado según corresponda; aún no validado con resultado medido.
- `Validado`: mejora verificada o ausencia de mejora justificada con evidencia.
- `Bloqueado`: falta información o acceso, documentar razón.
- `En desarrollo`: funcionalidad aún no publicada completamente; no equivale a rendimiento auditado.

Roles aproximados (no sustituyen revisar atributos `Authorize`, servicio y permisos reales): C=Cajero, A=Administrador, S=Superadministrador, K=Cocina, D=Domiciliario.

## Catálogo funcional (inventario inicial, pendiente de validar flujo por flujo)

| ID | Módulo | Roles orientativos | Funcionalidades / subáreas |
|---|---|---|---|
| M01 | Autenticación | C,A,S,K,D | Login; logout; refresh JWT; recuperación y cambio de contraseña; sesión/validación |
| M02 | Usuarios y perfiles | A,S; propio todos | CRUD de usuarios; roles; activación; perfil; fotografía; nómina |
| M03 | Sucursales y multitenencia | S; A parcial; C contexto | CRUD sucursales; contexto; aislamiento tenant; estadísticas |
| M04 | Configuración operativa | A,S; C parcial | Horarios; cobertura; barrios; tarifas; lugares autorizados |
| M05 | Productos y categorías | A,S | Catálogo; categorías; presentaciones; precios; fotos; disponibilidad; stock |
| M06 | Clientes y direcciones | C,A,S | Búsqueda; teléfonos; direcciones; historial; tarifas por sucursal |
| M07 | Promociones y descuentos | C,A,S; configuración A,S | Promoción diaria; códigos; validación; aplicación de beneficios |
| M08 | Fidelización | C,A,S; configuración A,S | Ciclos; niveles; recompensas; aplicación por pedido |
| M09 | Toma de pedidos POS | C,A,S | Catálogo POS; cliente; domicilio; tipo; borradores; pagos; tarifas; envío |
| M10 | Administración de pedidos | C,A,S | Listado; filtros; detalles; cambios; estados; cancelación; seguimiento |
| M11 | Pedidos entre sucursales | C,A,S | Selección destino; creación cross-branch; administración remota por origen (PR #19 pendiente) |
| M12 | Reservas y anticipos | C,A,S | Reservas; programación; consulta; abonos; edición |
| M13 | Cocina | K,A,S | Cola; observaciones; estados de preparación; eventos en vivo |
| M14 | Impresión | C,A,S,K,D según operación | Comandas; reimpresión; colas; agente Windows; configuración |
| M15 | Despacho de domicilios | C,A,S | Asignación; desasignación; pedidos listos; entrega y monitoreo |
| M16 | Optimización de rutas | C,A,S,D según acción | Planes; propuestas; agrupaciones; orden; recálculos; mapas |
| M17 | Flutter: disponibles | D | Pedidos libres; en preparación; rutas sugeridas; autoasignación |
| M18 | Flutter: entregas activas | D | Ruta activa; estados; navegación; llamadas; WhatsApp; impresión |
| M19 | GPS y jornadas | D; consulta C,A,S | Tracking activo/idle; offline; reintentos; jornadas; eventos dispositivo |
| M20 | Auditoría y alertas GPS | A,S; vista limitada C | Ubicación; playback; permanencias; incidentes; alertas |
| M21 | Gestión diaria de domiciliarios | C,A,S | Anticipos; liquidación; resumen; recaudos; jornadas; desbloqueo |
| M22 | Flutter: historial y métricas | D | Historial; filtros; estadísticas; tiempos; ganancias |
| M23 | Bancos y aplicaciones | A,S; C lectura | Bancos; apps; cuentas; saldos; movimientos |
| M24 | Pagos y conciliaciones | C,A,S según acción | Pagos bancarios/apps; verificación; liquidación; efectivo |
| M25 | Caja y cierres | C,A,S según acción | Esperado; cierre; caja fuerte; préstamos; conciliación |
| M26 | Gastos | C crea; A,S administran | Registro; líneas; categorías; búsquedas; imputación |
| M27 | Proveedores | C crea; A,S administran | CRUD; gastos; vínculo sucursales |
| M28 | Inventario | A,S; recetas S | Balances; movimientos; recetas; ajustes; conteos; transferencias; reportes |
| M29 | Dashboard y estadísticas | A,S | Ventas; gastos; evolución; productos; rutas; comparación entre sedes |
| M30 | WhatsApp: atención | C,A,S | Inbox; mensajes; multimedia; plantillas; respuestas; atención humana |
| M31 | WhatsApp: IA y flujos | C,A,S operación; A,S configuración | Flujos; borradores; prompts; consumo; diagnósticos; workers |
| M32 | Web Next.js y checkout | Público; backend | Menú; carrito; OTP; cobertura; cotización; pedido; pago |
| M33 | Wompi | A,S configuración; público pago | Webhook; pagos; reintentos; revisión |
| M34 | Rappi | C,A,S operación; A,S configuración | Sincronización; recepción; aceptación/rechazo; menú |
| M35 | Meta y analítica comercial | A,S | Feed; conversiones; diagnósticos; embudo |
| M36 | Blog y SEO | S; público consulta | Artículos; publicación Notion; SEO; enlace frontend a verificar |
| M37 | Documentos corporativos | Todos consulta; S administra | Ver; descargar; crear; editar; eliminar |
| M38 | Tiempo real | Transversal | SignalR; reconexiones; grupos; payloads; suscripciones |
| M39 | Notificaciones | Transversal | FCM; notificaciones de pedidos, impresión y pagos |
| M40 | Procesos automáticos | Transversal | Workers; outbox; sincronizadores; periodicidad; fallos/reintentos |
| M41 | PostgreSQL/EF Core | Transversal | Consultas; índices; N+1; conexiones; RLS; backups |
| M42 | Infraestructura/despliegue | Transversal | Railway; RAM; CPU; egress; CI; logs; despliegues |

## Notas de integridad
- M11: [PR backend #19](https://github.com/savelasquezs/SenorArroz/pull/19) abierto a fecha inicial; frontend e instalación productiva pendientes según descripción del PR. **No dar por funcionalidad desplegada**.
- M36: confirmar si la opción Blog/SEO está conectada a una ruta activa de Vue.
- Facturación electrónica: la sección visible es provisional, con acción deshabilitada; **no clasificar como funcionalidad implementada**.
- Para cada feature registrar ruta del archivo en Vue/Flutter/Next.js, endpoint API, handler, repositorio, tablas y permisos antes de concluir.
- No colocar secretos, tokens, números privados o datos personales productivos en reportes.
- Ajustar módulos y features si el código cambia, mediante PR con trazabilidad.
