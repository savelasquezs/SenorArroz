# Data Gateway

Integrado en la API .NET 9 existente. Desactivado por defecto; no requiere otro servicio Railway. Reutiliza exclusivamente `senorarroz_db_reader`, con tenant fijo 1. No compartir esta credencial con otros tenants. GitHub Actions permanece como respaldo.

## Superficie

- `GET /api/internal/data-gateway/schema`
- `POST /api/internal/data-gateway/query`
- `POST /api/internal/data-gateway/explain`
- `POST /api/internal/data-gateway/mcp` (Streamable HTTP sin sesiones; GET/DELETE devuelven 405).
- `GET /.well-known/oauth-protected-resource/api/internal/data-gateway/mcp` y alias raíz.

Las consultas son objetos `{view, columns?, filters?, orderBy?, descending?, maxRows?}`. Solo se permiten vistas explícitamente configuradas dentro de `diagnostics`, con `security_invoker=true` y `security_barrier=true`. Identificadores cotejados con el catálogo; valores parametrizados; operadores eq/ne/gt/gte/lt/lte; propiedades desconocidas rechazadas. No hay SQL libre, joins enviados por clientes, DDL/DML ni ejecución de código en el servidor.

Cada conexión comprueba nombre, atributos, membresías y propiedad del lector. Cada transacción es READ ONLY, configura tenant desde identidad autenticada y apaga system_scope; timeout SQL 3 s, lock 500 ms, pool máximo 3 y 2 consultas concurrentes por instancia. Cuerpos de hasta 32 KiB, 60 solicitudes/minuto por tenant, 500 filas, 30 columnas y 20 filtros; valores de texto limitados a 2048 caracteres y filas serializadas a 256 KiB. `truncated` indica filas omitidas. Los valores numéricos se devuelven como cadenas para preservar precisión.

Las 39 tablas autorizadas usan SELECT por columna; 40 vistas incluyen además filtro explícito tenant=1 tras aplicar las ampliaciones revisadas. `orders_summary` agrupa por fecha de Colombia y conserva decimales. Las 90 tablas restantes se excluyen. El inventario completo y las columnas aprobadas están en [DB_READER_PERMISSIONS.md](DB_READER_PERMISSIONS.md). `diagnostics.users` agrega exclusivamente `id` y `name` desde la tabla real `public."user"`; requiere `add_gateway_user_names_20261110.sql` y añadir la vista a AllowedViews después de aplicar el SQL. `tenant_id` se concede únicamente para el filtro interno y no aparece en esa vista.

## Autenticación

Dos vías aisladas, bajo la misma política del gateway:

- JWT habitual de la API: Superadmin del tenant 1; conserva las comprobaciones existentes de sesión y revocación.
- OAuth alojado en la misma API mediante OpenIddict 7.7.2: authorization code con PKCE S256 obligatorio, códigos de un solo uso (2 minutos), access tokens RSA (10 minutos), refresh tokens rotatorios (7 días) y revocación persistente. El issuer y las claves son distintos del JWT del POS. El usuario debe ser Superadmin activo, no terminal y pertenecer al tenant 1 activo. `AllowedSubjects` restringe IDs explícitos; `AllowTenantSuperadmins=true` permite los Superadmin de ese tenant. Ambas opciones están cerradas por defecto. Se revalidan rol, actividad y versión de acceso del tenant al emitir o usar tokens.
- También se admite proveedor OAuth externo configurando `HostedEnabled=false`, Authority HTTPS, Resource exacto, tenant_id=1, scope gateway:read y AllowedSubjects explícitos.

El consentimiento alojado valida las credenciales existentes sin abrir ni sustituir sesiones del POS. Incluye antiforgery, CSP, límites de cuerpo e intentos por IP. DCR solo registra clientes públicos con PKCE y callbacks HTTPS de ChatGPT o loopback de Codex; máximo 128 clientes y dos registros simultáneos. Las cuatro tablas técnicas de `gateway_oauth` tienen FORCE RLS y guard restrictivo tenant=1 para la cuenta runtime existente; el lector no puede acceder a ellas. No contienen datos operativos del restaurante. La migración se aplica mediante Actions y nunca al arrancar la API.

Los tokens OAuth del gateway solo son aceptados por esta política; no autentican las otras rutas administrativas. Se publican metadata RFC 9728, RFC 8414, JWKS y challenge WWW-Authenticate. El JWT del POS sirve para clientes administrativos, mientras ChatGPT usa el consentimiento OAuth. Los clientes públicos usan token_endpoint_auth_method=none con PKCE; no se crean secretos de cliente. Para clientes antiguos que omiten resource, el servidor asigna exclusivamente su Resource configurado; un resource explícito distinto siempre se rechaza y el token conserva la audiencia exacta.

Se rechaza todo `Origin` del MCP salvo coincidencia exacta en `DataGateway:AllowedOrigins`; clientes servidor a servidor pueden omitirlo. No autorizar comodines. No incluir tokens en URLs, logs, Issues o repositorios.

Referencias: [autenticación de OpenAI](https://developers.openai.com/plugins/build/auth), [MCP en Codex](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [transporte MCP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports), [vistas PostgreSQL](https://www.postgresql.org/docs/17/sql-createview.html).

## Railway

Preparación en `tools/data-gateway/railway.env.example`. Servicio existente: `SenorArroz`; no crear otro servicio.

1. Mantener `DataGateway__Enabled=false`.
2. Instalar `ConnectionStrings__DataGatewayReader` con la credencial existente y host privado de MainDatabase. El workflow manual owner-only `reader-handoff.yml` del asistente permite transportarla cifrada RSA-OAEP SHA-256: la clave privada permanece en memoria del operador y el artefacto contiene solo ciphertext. No usar DefaultConnection ni rotar la contraseña.
3. Configurar `DataGateway__OAuth__HostedEnabled=true`, Authority `https://senorarrozapi.up.railway.app/api/internal/data-gateway/oauth/`, Resource `https://senorarrozapi.up.railway.app/api/internal/data-gateway/mcp` y `AllowTenantSuperadmins=true` o IDs autorizados. Instalar SigningKey RSA-3072+ PKCS8 PEM y EncryptionKey AES-256 Base64 como variables secretas; conservarlas entre despliegues y no reutilizar JwtSettings.
4. Configurar `DataGateway__AllowedViews__N` solo con las vistas revisadas y aplicadas de diagnostics (40 con la ampliación de almuerzos).
5. Aplicar ambas migraciones mediante importación, revisión, SHA, respaldo restaurado y autorización del workflow existente. El merge no aplica SQL.
6. Comprobar con el lector real: columnas excluidas, RLS entre tenants, credencial sin escritura, protocolo MCP y expiración. Activar `DataGateway__Enabled=true` después de documentar resultados. Apagado inmediato: `DataGateway__Enabled=false`; para cerrar también autorización, HostedEnabled=false.

## ChatGPT, Codex y Code Mode

ChatGPT: registrar la URL HTTPS `/api/internal/data-gateway/mcp` como app/conector privado y seleccionar OAuth con registro DCR. Añadir `offline_access` a Base scopes y `gateway:read` a los scopes; completar inicio de sesión y consentimiento con una cuenta elegible de Señor Arroz. Probar `get_schema`, `execute_query` y `explain_query`; herramientas declaradas de lectura, idempotentes y con `structuredContent`. La instalación/consentimiento de una cuenta ChatGPT requiere intervención del propietario y no se realiza automáticamente al desplegar.

Codex HTTP: en su configuración MCP indicar `url` con ese endpoint y ejecutar `codex mcp login senor_arroz` cuando se use OAuth. Alternativamente `bearer_token_env_var="SENOR_ARROZ_GATEWAY_TOKEN"` para un JWT administrativo vigente; no escribir el token en TOML. Codex stdio: ejecutar `python` con ruta absoluta a `tools/data-gateway/mcp_stdio.py`, pasando `SENOR_ARROZ_GATEWAY_URL` y `SENOR_ARROZ_GATEWAY_TOKEN` por entorno. El puente reenvía el protocolo HTTP y reutiliza los esquemas del servidor. No sigue redirecciones ni imprime credenciales.

SDK TypeScript: `DataGateway` consume REST con token suministrado por el host, timeout y respuestas limitadas, sin redirecciones. En un sandbox Code Mode usar `codeModeGateway(invoke)`: el host proporciona una función que invoca las tres herramientas MCP y conserva las credenciales fuera del código del agente. No exponer variables de entorno, conexiones PostgreSQL ni tokens al sandbox. La disponibilidad de Code Mode depende del cliente; el gateway no habilita esa función en una cuenta.

### Permanencias y almuerzos

La detección operativa ya excluye los puntos del intervalo de almuerzo y divide las permanencias al inicio de ese intervalo. Para contrastar alertas históricas o almuerzos corregidos, `add_gateway_lunch_review_20261110.sql` permite cruzar `delivery_tracking_alert.incident_id` con `delivery_tracking_incident.id`, y `employee_lunch.user_id` con `delivery_tracking_incident.deliveryman_id`, siempre dentro del mismo tenant. `users.id/name` resuelve el nombre. Usar inicio/fin del incidente, no la fecha de notificación ni duración/resolución de la alerta. Una sucursal histórica distinta del empleado no invalida por sí sola un almuerzo registrado de esa misma persona.

El helper puro `tools/data-gateway/helpers/stay-lunch.ts` procesa respuestas completas en el cliente y devuelve segundos cubiertos/no cubiertos, sin herramientas ni endpoints adicionales. Une intervalos superpuestos y usa límites `[inicio, fin)` UTC; admite cruces de medianoche. Un intervalo cerrado totalmente cubierto se excluye de una revisión por permanencia. El solapamiento parcial requiere revisar solo el tiempo exterior; ausencia de solapamiento indica investigar, nunca una falta imputable automática. Un almuerzo sin regreso y evidencia incompleta requieren revisión; conservar las clasificaciones administrativas existentes.

El cliente debe consultar todos los almuerzos que puedan solaparse, incluidos los iniciados antes del período: una consulta de cerrados con `started_at < fin` y `ended_at > inicio`, y otra de abiertos con `started_at < fin` y `ended_at eq null`. Combinar ambas respuestas y conservar `truncated=true` si cualquiera fue truncada. Fijar un `asOf` explícito con zona horaria y seleccionar `tenant_id`, identidades, intervalos, `evidence_complete` y revisión; el helper rechaza datos parciales o intervalos inválidos.

`stayLunchSummary` orquesta el cruce desde el cliente: selecciona alertas por `occurred_at` dentro de `[from,to)`, pagina por ID, obtiene el rango de incidentes referenciados y descarta los ajenos dentro de Code Mode. Valida identidad/tenant/sucursal, deduplica incidentes y conserva los estados y clasificaciones administrativas en el resumen. Consulta almuerzos cerrados y abiertos con un máximo de dos llamadas concurrentes. Cada conjunto admite cuatro páginas de 500 filas; si falta evidencia o se agota ese límite, falla y exige acotar el período/empleado. El límite del desglose de empleados se marca aparte y no cambia los totales. La fuente `helpers/stay-lunch-summary.ts` y el helper temporal se compilan al recurso JS del skill con `node tools/data-gateway/build-code-mode.mjs`; CI comprueba que no esté desactualizado.

### Plugin híbrido y medición de Code Mode

El paquete privado 1.1.0 conserva el nombre y `.app.json` del plugin instalado y añade `skills/senor-arroz-analytics`. No cambia el servidor MCP, OAuth, credenciales ni permisos. Generar el ZIP con `python tools/data-gateway/package-plugin.py <destino.zip>` y actualizar el plugin existente mediante **Upload new version**; iniciar una conversación nueva para cargar la habilidad. No crear otra app ni reconectar OAuth si la conexión existente sigue vigente.

La habilidad dirige cruces/agregaciones a Code Mode y mantiene lecturas simples como llamadas directas. Su recurso contiene código confiable revisado; puede cargarse y compilarse dentro del sandbox y reutilizarse por versión mediante store/load. Las respuestas PostgreSQL se pasan como datos y nunca se compilan. El host conserva autenticación; el programa emite solo el resumen. El recurso no necesita shell, red directa ni un servicio permanente. Si el cliente no ofrece Code Mode, debe indicar esa limitación en lugar de afirmar que ejecutó el programa.

El 14 de noviembre de 2026 se verificaron ejecuciones reales mediante `functions.exec` y las herramientas MCP autenticadas de este gateway: cuatro consultas por informe, datos intermedios retenidos en V8 y únicamente el resumen emitido. Dos informes se reconciliaron independientemente contra las respuestas capturadas. Después se instaló la versión 1.1.0 en ChatGPT y una conversación web nueva devolvió el mismo resumen completo de 11 incidentes. Su detalle técnico identifica `functions.exec` y la llamada a `stayLunchSummary`; la interfaz no ofrece un registro bruto de herramientas ni contadores `usage` para auditarla independientemente. Estado y capas de evidencia en `tasks/data-gateway-rollout.md`.

Medición reproducible con tiktoken 0.14.0 / `o200k_base`, sobre las mismas consultas y respuestas capturadas:

| Caso | Filas leídas | Respuestas completas | Resumen | Argumentos + respuestas directas | Programa + resumen, recurso cargado | Primera carga medida |
|---|---:|---:|---:|---:|---:|---:|
| Período amplio, 11 incidentes | 92 | 7362 | 331 | 7745 | 526 | 878 |
| Un incidente | 2 | 245 | 157 | 681 | 356 | 708 |

La primera prueba reduce el contenido de respuestas un 95.50%; contando argumentos frente al programa y resumen, el tráfico tokenizado disminuye un 93.21% con el recurso cargado y un 88.66% incluyendo el arranque medido. En el caso pequeño, el arranque consume 27 tokens más que la ruta directa. Cargar recursos dentro de Code Mode y reutilizarlos evita repetir fuente en el contexto. Otro cliente puede cargar esa fuente de forma distinta y tener un arranque más caro.

Estas cifras son conteos del tráfico de herramientas, no facturación ni consumo completo del modelo: excluyen razonamiento, caché, historial, envolturas del cliente y la selección del skill. No se ejecutó un modelo directo separado; se mantuvieron iguales las consultas y los datos para comparar su representación. Un A/B de consumo total exige sesiones aisladas con el mismo modelo/prompts y contadores `usage` de ambos recorridos. No atribuir el porcentaje medido al costo total de ChatGPT.

`benchmark_tokens.py` cuenta capturas privadas con `--program` y opcionalmente `--bootstrap`/`--bootstrap-output`; requiere tiktoken. No versionar filas, nombres ni capturas de empleados. La evidencia técnica sanitizada está en [DATA_GATEWAY_CODE_MODE_BENCHMARK.json](DATA_GATEWAY_CODE_MODE_BENCHMARK.json). La comparación conserva completitud y revisión; reducir salida no debe esconder errores, páginas faltantes ni evidencia necesaria.

## Validación

CI ejecuta .NET, SDK TypeScript y stdio. Las pruebas HTTP verifican firmas, expiración, audiencia, scope, sujeto, rol y tenant; el flujo OAuth real en PostgreSQL valida antiforgery, PKCE incorrecto, código reutilizado, refresh, revocación y pérdida de actividad/rol/tenant/version; las pruebas PostgreSQL ejecutan dos veces la migración, suplantación de GUC, columnas privadas, SQL malicioso, escrituras, filtros de fecha, decimales, límites de filas y un umbral de rendimiento local. `FLOW_POSTGRES_TEST_CONNECTION` debe apuntar a un servidor desechable con permiso de crear bases y roles. Nunca apuntarlo a producción. La prueba crea/elimina una base aislada y el rol lector dentro de ese servidor.

Pendientes operativos y evidencia del respaldo: [tasks/data-gateway-rollout.md](../tasks/data-gateway-rollout.md).

Mejoras posteriores: medir p95 y CPU con carga real antes de ampliar límites; agregar índices compuestos tenant/fecha solo cuando EXPLAIN lo justifique; autorizar nuevas vistas por caso de uso. No cachear datos entre identidades ni compartir el lector con otros tenants.
