# Inventario maestro de rendimiento — El Señor Arroz

Fecha inicial: 2026-10-12. Fuente de verdad: esta carpeta del repositorio backend. Alcance inter-repositorio: [API](https://github.com/savelasquezs/SenorArroz), [Vue](https://github.com/savelasquezs/senorArrozFront), [Flutter](https://github.com/savelasquezs/delivery_app), [Next.js](https://github.com/savelasquezs/senor-arroz-web-nextjs), [Print Agent](https://github.com/savelasquezs/senorArrozPrintAgent).

Programa frontend de octubre: [implementación local y evidencia](reviews/2026-10-26-FRONT-PERF-2026-10.md). No representa validación productiva.

## Objetivo
Disminuir latencia, llamadas redundantes al backend, consultas y transferencia PostgreSQL, consumo de CPU/RAM, tráfico saliente y costos, **sin deteriorar funcionalidades, permisos, aislamiento entre sucursales/tenants ni exactitud financiera**.

## Autorización y flujo de trabajo diario: auditar, corregir y dejar PR listo

El propietario autoriza **anticipadamente** que cada revisión diaria de rendimiento incluya **diagnóstico e implementación de una corrección pequeña en una rama independiente**, con pruebas y pull request dirigido a `main`. Ya **no es necesario esperar aprobación para escribir la corrección en la rama**. La aprobación del propietario se reserva para fusionar el PR y ejecutar migraciones/despliegues productivos.

1. **Seleccionar exactamente un feature:** leer [CHECKLIST.md](CHECKLIST.md), informes previos y PR abiertos de todos los repositorios implicados; evitar duplicar trabajo o mezclar funcionalidades. Priorizar frecuencia, costo y riesgo, especialmente peticiones HTTP/SignalR repetidas, N+1 en SQL, payloads excesivos, CPU/RAM/egress y latencia.
2. **Investigar antes de modificar:** seguir pantalla → store/composable/proveedor → API → handler/servicio → EF/repositorio/SQL → PostgreSQL, y también SignalR, Flutter, workers, Next.js o agente de impresión cuando proceda. Registrar rama y SHA de `main` de cada repo, roles afectados, datos reproducibles y métricas actuales; distinguir hipótesis de hechos. Si no hay acceso a métricas productivas, indicar "sin medir", nunca inventarlas.
3. **Corregir en ramas nuevas:** crear una rama específica por feature **a partir del `main` actualizado** de cada repositorio afectado, por ejemplo `perf/M10-F01-deduplicar-listado`. Aplicar la **solución mínima y comprobable** que preserve roles, reglas de negocio, permisos por sucursal, aislamiento tenant/RLS, integridad financiera, tracking y experiencia de usuario. No introducir refactorizaciones ajenas ni optimizaciones especulativas. Si no se encuentra una mejora segura, documentar por qué y no forzar un cambio.
4. **Probar y comparar:** agregar pruebas de regresión que reproduzcan el problema; ejecutar lint/build/unitarias y las pruebas de integración/e2e pertinentes, según cada repositorio. Medir antes/después cuando el entorno lo permita. Corregir fallos provocados por la rama. Si no es posible validar, declarar bloqueo y mantener PR en borrador, sin presentarlo como listo.
5. **Preparar cambios de BD cuando sean necesarios:** incluir en el PR del backend scripts SQL **versionados e idempotentes** en `SenorArroz.Infrastructure/Scripts/` (el backend no aplica automáticamente migraciones EF). Actualizar entidades/configuración EF, índices, contratos y `docs/DATABASE_MAP.md` si corresponde. Probar con PostgreSQL aislado o de desarrollo, verificar seguridad ante reejecución, concurrencia, multitenant/RLS y compatibilidad hacia atrás; dejar plan para datos existentes, validación, copias de seguridad, orden exacto de aplicación y reversión/contingencia. **No ejecutar DDL, DML ni migraciones en PostgreSQL de producción**.
6. **Abrir PR a `main`:** uno por repositorio afectado, vinculados entre sí y al identificador `Mxx-Fyy`. Incluir causa raíz, cambios, pruebas con resultados, comparación de rendimiento, riesgos, commits y un apartado explícito **"Migración BD: no aplica / pendiente de ejecutar"**. Para varios repos, describir dependencias y orden seguro. Evitar PRs a ramas de features antiguas; no fusionar ni desplegar.
7. **Registrar en el seguimiento mediante PR:** documentar el diagnóstico en `reviews/YYYY-MM-DD-Mxx-Fyy.md` usando [REVIEW_TEMPLATE.md](REVIEW_TEMPLATE.md) y proponer actualización de [CHECKLIST.md](CHECKLIST.md) con enlaces a todos los PRs y scripts. Si este repositorio no es el que cambia código, abrir **PR exclusivamente documental a `main`** para registrar el informe, sin escribir directamente en `main`. Marcar `PR listo para revisión` únicamente si el código está realmente preparado y pruebas pertinentes pasan; si falta verificación, usar `Analizado` o `Bloqueado`. Un PR abierto **no significa** que ya esté en producción.
8. **Entregar decisión al propietario:** resumir el problema confirmado, el cambio y su mejora esperada/medida, los enlaces de PR, estado de las pruebas, riesgos y **las acciones exactas que quedan en sus manos**: revisar, hacer merge y aplicar las migraciones cuando proceda. En backend que dependa de nuevas tablas/columnas, el orden habitual es **backup/verificación → SQL → backend → frontend/app**, pero documentar el orden real y evitar ventanas incompatibles.
9. **Cerrar después de su integración:** cuando el propietario confirme merge y publicación, comprobar migraciones, salud de servicios y métricas comparables; entonces actualizar a `Implementado` y finalmente `Validado`. No marcar estos estados anticipadamente. Las revisiones futuras deben leer el estado vigente y avanzar sin rehacer lo ya cerrado.

### Límites de esta autorización
- **Sí:** lectura y auditoría; cambios de código y pruebas en **ramas nuevas**; scripts SQL preparados y verificados fuera de producción; commits; PRs hacia `main`; documentación y checklist propuestos por PR.
- **No, salvo nueva instrucción explícita:** hacer merge, push directo a `main`, aplicar migraciones o cambiar datos en producción, cambiar infraestructura productiva, desplegar API/frontend/Flutter, activar automatismos que afecten usuarios, ni usar operaciones destructivas contra producción.
- No incluir secretos, datos personales productivos ni credenciales en informes. Evitar consultas costosas o bloqueos de tablas en producción; preferir análisis de solo lectura y entornos aislados.
- La tarea programada puede ejecutar este procedimiento con las herramientas disponibles, pero **debe declarar qué acciones logró completar realmente**. Si no puede editar algún repositorio, correr pruebas o crear un PR, documenta el bloqueo y no afirma que esté listo.

## Estados del checklist
- `Pendiente`: inventariado, aún sin diagnóstico.
- `Analizado`: diagnóstico registrado, todavía sin PR de corrección listo, o sin cambio justificado.
- `PR listo para revisión`: rama y PR(s) abiertos a `main`, cambio delimitado, pruebas pertinentes correctas e instrucciones SQL incluidas si aplican; pendiente de decisión del propietario.
- `Mejora aprobada`: el propietario aprobó una solución, aún no integrada/productiva (opcional si aprueba el PR antes de fusionar).
- `Implementado`: el cambio está fusionado, con migración y despliegue aplicados según corresponda; falta verificación de impacto real.
- `Validado`: producción verificada con evidencia antes/después o ausencia de mejora necesaria justificada.
- `Bloqueado`: sin datos, permisos, pruebas o forma segura de corregir; explicar el impedimento.
- `En desarrollo`: funcionalidad aún no publicada completamente; no es sinónimo de auditoría completada.

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
