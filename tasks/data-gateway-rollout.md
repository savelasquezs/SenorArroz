# Data Gateway: activación controlada

## Estado

PR backend #116 y asistente #86/#88 fusionados. Importación #84 corregida y reemplazada por #87. Migración del lector aplicada en producción mediante solicitud #89 y Actions 42862104302, con autorización del propietario del 10 de noviembre. Validación de lectura #92: rol existente, 38 vistas, 37 guards, acceso a columnas sensibles falso y escrituras falso.

Credencial existente instalada en Railway mediante handoff cifrado RSA-OAEP SHA-256 (PR #91; run 42863497538); no se imprimió ni regeneró. OAuth y gateway activos en la API existente, sin servicios nuevos ni cambios de sesiones POS.

PR backend #117/#118 fusionados; despliegue Railway b8976d35 SUCCESS sobre e01d37fb. OAuth SQL importado/fusionado #94 y aplicado por #95 / Actions 42868402601. #96 confirma cuatro tablas técnicas con FORCE RLS, cuatro guards y cero permisos del lector. CI final: 1747 aprobadas y una omitida; SDK 4, stdio 7, asistente 40.

ChatGPT y Codex OAuth autorizados por el propietario. Verificación real del 10 de noviembre: get_schema devolvió 38 vistas; execute_query devolvió tres grupos de orders_summary. Corroboración productiva tenant=1, rows=3 y HTTP 200 de openai-mcp. Evidencia y estado operativo posterior en la solicitud privada #95. La CLI instalada requiere el override temporal service_tier=fast para interpretar la configuración; se conserva priority en el escritorio.

Ampliación solicitada: diagnostics.users con solo id/name, desde public."user" y con tenant fijo 1. SQL add_gateway_user_names_20261110.sql, pruebas de columnas privadas/RLS/escrituras y matriz actualizada. Aplicar mediante importación, revisión, SHA y respaldo comprobado; después añadir únicamente diagnostics.users a AllowedViews y verificar desde ChatGPT. El estado productivo de esta ampliación se registra en la solicitud de migración correspondiente, sin inferirlo del merge.

## Ampliación de almuerzos

Ampliación de almuerzos/permanencias: PR backend #120 y asistente #105 fusionados. SQL `add_gateway_lunch_review_20261110.sql` aplicado por #106, SHA normalizado `308e237d8f4d5f7af901f4c68fc5c747434776c74b2217eb23a7f3d8bf40f65e`: una vista nueva y dos proyecciones ampliadas, sin herramientas MCP nuevas. #107 confirma FORCE RLS, invoker/barrier, cero columnas fuera de la lista y cero escrituras. `AllowedViews__39=diagnostics.employee_lunch` configurada después de SQL. Helper temporal en `tools/data-gateway/helpers/stay-lunch.ts`, con casos de cobertura total/parcial, extremos, medianoche, duplicados, otro empleado/tenant, almuerzo abierto y datos incompletos. CI de esa ampliación: 1747 pruebas aprobadas/1 omitida, SDK 9, stdio 7 y asistente 40. No convertir ausencia de almuerzo en sanción ni modificar revisiones existentes. El estado del despliegue y la consulta real se registra en #106; la validación posterior de Code Mode se detalla a continuación.

## Validación de Code Mode — 14 de noviembre

Ejecutados informes reales en `functions.exec` con las herramientas del plugin ya conectado: un período amplio (92 filas leídas, 11 incidentes, cuatro consultas MCP) y un caso individual. Cálculo y unión realizados en V8, sin emitir las filas intermedias. Verificación independiente de intervalos y estados correcta. Evidencia sanitizada: `docs/DATA_GATEWAY_CODE_MODE_BENCHMARK.json`. Tráfico tokenizado del período: 7745 tokens directos frente a 526 de programa/resumen, o 878 incluyendo el arranque de este cliente. No mide consumo facturado total; en la muestra pequeña, el arranque supera la ruta directa por 27 tokens.

Plugin privado 1.1.0 instalado mediante Upload new version: la interfaz confirmó la versión, una app y la habilidad `senor-arroz-analytics`, conservando identidad y conexión OAuth. El propietario abrió Chrome y se recuperó el control del navegador.

Prueba en una conversación web nueva: devolvió el mismo informe de 11 incidentes, cinco empleados, cobertura de almuerzo cero y 226.2 minutos fuera de almuerzos registrados, completo y con todos los casos pendientes/sin clasificación final. El detalle técnico de la respuesta identifica `functions.exec`, dos lecturas de habilidad/recurso, una consulta de formato y cuatro consultas de datos durante `stayLunchSummary`; muestra el adaptador MCP y la llamada con límites UTC. La interfaz expone el resumen de actividad y esta explicación técnica, no un registro bruto auditable de herramientas ni contadores de tokens. La ejecución instrumentada de escritorio aporta la evidencia directa y reproducible del programa; no atribuir sus conteos al consumo total de esa conversación web. No quedan pendientes de instalación o consentimiento para empezar a consultar con este plugin en una conversación nueva.

CI del código `167b190a`: Backend CI 43672087732 y Remote order checks 43672087728 correctos; 1749 pruebas .NET aprobadas y una omitida, SDK 13, stdio 7 y exportador 6. Bundle generado, tipado estricto TypeScript y habilidad validados.

## Respaldo restaurable

Objeto Railway existente `backup-20261110-080417.dump`, 2026-11-10 08:04:21 UTC, 13 522 480 bytes. SHA-256 `617c67c1ffcb8dc5c65d90207ba0edaae94c2eaf552a421080ca3ced8c09dee2`.

Restaurado íntegramente con PostgreSQL 17 en base local aislada (exit 0, 129 tablas). Se recreó únicamente en el servidor de pruebas el nombre del rol requerido por las políticas del dump; el respaldo usa --no-owner/--no-acl, por lo que no restaura contraseñas ni roles de cluster. Migración aplicada dos veces sobre el clon: 37 guards y 38 vistas, sin error; SELECT de columnas privadas y acceso cross-tenant denegados. 30 consultas de resumen sobre los datos restaurados: promedio 14.19 ms, máximo 16.76 ms (medición local; no predice latencia Railway). Clon eliminado tras verificar. Huella normalizada de migración: `82e6255386cb296fa7f6c6728d97b06175d32d4bba125b0d6071ed044d4ad9eb`.

Un resultado SUCCESS del cron por sí solo no acredita restaurabilidad. La autorización debe revisar antigüedad del backup en el momento de aplicar. La prueba de restauración no constituye autorización de modificación de producción.
