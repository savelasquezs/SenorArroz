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

Las 37 tablas autorizadas usan SELECT por columna; 38 vistas incluyen además filtro explícito tenant=1. `orders_summary` agrupa por fecha de Colombia y conserva decimales. Las 92 tablas restantes se excluyen. El inventario completo y las columnas aprobadas están en [DB_READER_PERMISSIONS.md](DB_READER_PERMISSIONS.md).

## Autenticación

Dos vías aisladas, bajo la misma política del gateway:

- JWT habitual de la API: Superadmin del tenant 1; conserva las comprobaciones existentes de sesión y revocación.
- OAuth alojado en la misma API mediante OpenIddict 7.7.2: authorization code con PKCE S256 obligatorio, códigos de un solo uso (2 minutos), access tokens RSA (10 minutos), refresh tokens rotatorios (7 días) y revocación persistente. El issuer y las claves son distintos del JWT del POS. El usuario debe ser Superadmin activo, no terminal y pertenecer al tenant 1 activo. `AllowedSubjects` restringe IDs explícitos; `AllowTenantSuperadmins=true` permite los Superadmin de ese tenant. Ambas opciones están cerradas por defecto. Se revalidan rol, actividad y versión de acceso del tenant al emitir o usar tokens.
- También se admite proveedor OAuth externo configurando `HostedEnabled=false`, Authority HTTPS, Resource exacto, tenant_id=1, scope gateway:read y AllowedSubjects explícitos.

El consentimiento alojado valida las credenciales existentes sin abrir ni sustituir sesiones del POS. Incluye antiforgery, CSP, límites de cuerpo e intentos por IP. DCR solo registra clientes públicos con PKCE y callbacks HTTPS de ChatGPT o loopback de Codex; máximo 128 clientes y dos registros simultáneos. Las cuatro tablas técnicas de `gateway_oauth` tienen FORCE RLS y guard restrictivo tenant=1 para la cuenta runtime existente; el lector no puede acceder a ellas. No contienen datos operativos del restaurante. La migración se aplica mediante Actions y nunca al arrancar la API.

Los tokens OAuth del gateway solo son aceptados por esta política; no autentican las otras rutas administrativas. Se publican metadata RFC 9728, RFC 8414, JWKS y challenge WWW-Authenticate. El JWT del POS sirve para clientes administrativos, mientras ChatGPT usa el consentimiento OAuth.

Se rechaza todo `Origin` del MCP salvo coincidencia exacta en `DataGateway:AllowedOrigins`; clientes servidor a servidor pueden omitirlo. No autorizar comodines. No incluir tokens en URLs, logs, Issues o repositorios.

Referencias: [autenticación de OpenAI](https://developers.openai.com/plugins/build/auth), [MCP en Codex](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [transporte MCP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports), [vistas PostgreSQL](https://www.postgresql.org/docs/17/sql-createview.html).

## Railway

Preparación en `tools/data-gateway/railway.env.example`. Servicio existente: `SenorArroz`; no crear otro servicio.

1. Mantener `DataGateway__Enabled=false`.
2. Instalar `ConnectionStrings__DataGatewayReader` con la credencial existente y host privado de MainDatabase. El workflow manual owner-only `reader-handoff.yml` del asistente permite transportarla cifrada RSA-OAEP SHA-256: la clave privada permanece en memoria del operador y el artefacto contiene solo ciphertext. No usar DefaultConnection ni rotar la contraseña.
3. Configurar `DataGateway__OAuth__HostedEnabled=true`, Authority `https://senorarrozapi.up.railway.app/api/internal/data-gateway/oauth/`, Resource `https://senorarrozapi.up.railway.app/api/internal/data-gateway/mcp` y `AllowTenantSuperadmins=true` o IDs autorizados. Instalar SigningKey RSA-3072+ PKCS8 PEM y EncryptionKey AES-256 Base64 como variables secretas; conservarlas entre despliegues y no reutilizar JwtSettings.
4. Configurar `DataGateway__AllowedViews__N` solo con las 38 vistas revisadas de diagnostics.
5. Aplicar ambas migraciones mediante importación, revisión, SHA, respaldo restaurado y autorización del workflow existente. El merge no aplica SQL.
6. Comprobar con el lector real: columnas excluidas, RLS entre tenants, credencial sin escritura, protocolo MCP y expiración. Activar `DataGateway__Enabled=true` después de documentar resultados. Apagado inmediato: `DataGateway__Enabled=false`; para cerrar también autorización, HostedEnabled=false.

## ChatGPT, Codex y Code Mode

ChatGPT: registrar la URL HTTPS `/api/internal/data-gateway/mcp` como app/conector privado y seleccionar OAuth con registro DCR. Añadir `offline_access` a Base scopes y `gateway:read` a los scopes; completar inicio de sesión y consentimiento con una cuenta elegible de Señor Arroz. Probar `get_schema`, `execute_query` y `explain_query`; herramientas declaradas de lectura, idempotentes y con `structuredContent`. La instalación/consentimiento de una cuenta ChatGPT requiere intervención del propietario y no se realiza automáticamente al desplegar.

Codex HTTP: en su configuración MCP indicar `url` con ese endpoint y ejecutar `codex mcp login senor_arroz` cuando se use OAuth. Alternativamente `bearer_token_env_var="SENOR_ARROZ_GATEWAY_TOKEN"` para un JWT administrativo vigente; no escribir el token en TOML. Codex stdio: ejecutar `python` con ruta absoluta a `tools/data-gateway/mcp_stdio.py`, pasando `SENOR_ARROZ_GATEWAY_URL` y `SENOR_ARROZ_GATEWAY_TOKEN` por entorno. El puente reenvía el protocolo HTTP y reutiliza los esquemas del servidor. No sigue redirecciones ni imprime credenciales.

SDK TypeScript: `DataGateway` consume REST con token suministrado por el host, timeout y respuestas limitadas, sin redirecciones. En un sandbox Code Mode usar `codeModeGateway(invoke)`: el host proporciona una función que invoca las tres herramientas MCP y conserva las credenciales fuera del código del agente. No exponer variables de entorno, conexiones PostgreSQL ni tokens al sandbox. La disponibilidad de Code Mode depende del cliente; el gateway no habilita esa función en una cuenta.

## Validación

CI ejecuta .NET, SDK TypeScript y stdio. Las pruebas HTTP verifican firmas, expiración, audiencia, scope, sujeto, rol y tenant; el flujo OAuth real en PostgreSQL valida antiforgery, PKCE incorrecto, código reutilizado, refresh, revocación y pérdida de actividad/rol/tenant/version; las pruebas PostgreSQL ejecutan dos veces la migración, suplantación de GUC, columnas privadas, SQL malicioso, escrituras, filtros de fecha, decimales, límites de filas y un umbral de rendimiento local. `FLOW_POSTGRES_TEST_CONNECTION` debe apuntar a un servidor desechable con permiso de crear bases y roles. Nunca apuntarlo a producción. La prueba crea/elimina una base aislada y el rol lector dentro de ese servidor.

Pendientes operativos y evidencia del respaldo: [tasks/data-gateway-rollout.md](../tasks/data-gateway-rollout.md).

Mejoras posteriores: medir p95 y CPU con carga real antes de ampliar límites; agregar índices compuestos tenant/fecha solo cuando EXPLAIN lo justifique; autorizar nuevas vistas por caso de uso. No cachear datos entre identidades ni compartir el lector con otros tenants.
