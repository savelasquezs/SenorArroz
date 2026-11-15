---
name: senor-arroz-analytics
description: Consulta y cruza datos operativos de Señor Arroz mediante su Data Gateway; calcula en Code Mode y devuelve resúmenes, especialmente al contrastar almuerzos con alertas de permanencia.
---

Usa la app Señor Arroz Data Gateway ya conectada. Para cruces, agrupaciones y cálculos, invoca sus herramientas MCP desde Code Mode/exec: conserva las respuestas dentro del programa y emite solo el resumen necesario. Una consulta pequeña sin cálculos puede usar una llamada directa. No declares ahorro de tokens solo por usar exec.

## Almuerzos y permanencias

Lee [el programa probado](references/stay-lunch-program.js) solo para este cruce. Contiene `stayLunchSummary(query, options)`. Incluye sus funciones en el programa Code Mode o compila ese recurso confiable dentro del sandbox; nunca compiles datos devueltos por PostgreSQL. Si el host permite leer recursos desde exec, léelo allí y guarda su fuente por versión en store/load para evitar cargarla en el contexto. No ejecutes un shell ni uses HTTP desde el sandbox.

Resuelve `execute_query` de esta app en el catálogo de herramientas del host; el prefijo del nombre puede variar. El adaptador `query(request)` debe rechazar `isError`, devolver `structuredContent` o parsear el bloque JSON de texto si no hay contenido estructurado. No imprimas las respuestas. Pasa al programa fechas ISO con zona en `from`, `to` (exclusivo) y `asOf`; convierte los días de Colombia a límites UTC. Admite `branchId`, `deliverymanId` y `names`. El período máximo es 31 días; la selección corresponde a la fecha de inicio de las alertas `unexpected_stay`.

El programa pagina por ID con un máximo de cuatro páginas de 500 filas por conjunto. Lee los incidentes referenciados, comprueba tenant/empleado/sucursal y obtiene almuerzos cerrados y abiertos con un máximo de dos consultas concurrentes. No limites los almuerzos a la sucursal actual: conserva la identidad y sucursal históricas. Deduplica incidentes antes de sumar duración. La unión de intervalos evita contar almuerzos dos veces.

Devuelve únicamente el resultado de `stayLunchSummary`. Incluye período, fecha de corte, completitud, cantidades, cobertura y minutos fuera del almuerzo, estado de revisión y resumen por empleado. Sus categorías describen coincidencia temporal: ausencia de almuerzo significa investigar, nunca falta imputable automática. Un almuerzo abierto o evidencia incompleta requiere revisión. Conserva las clasificaciones administrativas. `employee_details_truncated` limita solo el desglose; los totales incluyen todos los incidentes calculados.

Si falta un incidente, hay identidades incoherentes, falla una consulta o se agota la paginación, emite un fallo `complete:false` y acota el período/empleado antes de concluir. No presentes un resultado parcial como completo. Para consultas de esquema, reduce `get_schema` dentro del programa a las vistas/columnas necesarias; no vuelques todo el catálogo.

## Límites

No solicites ni expongas credenciales, GPS, teléfonos, notas o explicaciones privadas. No añadas endpoints, herramientas por informe, SQL libre, escrituras ni cambios de revisión. La autenticación permanece en el host MCP. Si Code Mode no está disponible, indica esa limitación y no afirmes haber calculado en código. Las métricas tokenizadas del contenido y el consumo facturado total son medidas distintas.
