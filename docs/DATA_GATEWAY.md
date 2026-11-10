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
- OAuth del gateway: firma y descubrimiento OIDC mediante `DataGateway:OAuth:Authority`, audiencia exactamente `DataGateway:OAuth:Resource`, expiración, claim `tenant_id=1`, scope `gateway:read` y `sub` presente en `DataGateway:OAuth:AllowedSubjects`. El issuer debe emitir access tokens JWT. Sin usuarios autorizados no se concede acceso OAuth.

Los tokens OAuth del gateway solo son aceptados por esta política; no autentican las otras rutas administrativas. Los endpoints publican metadata RFC 9728 y challenge `WWW-Authenticate` para descubrir el proveedor. El proveedor OAuth es una dependencia de configuración pendiente: debe ofrecer authorization code + PKCE S256, registro del cliente ChatGPT mediante DCR/CIMD o cliente preestablecido, audiencia ligada al resource y refresh tokens si se requiere acceso continuo. No basta el JWT del POS para conectar ChatGPT.

Se rechaza todo `Origin` del MCP salvo coincidencia exacta en `DataGateway:AllowedOrigins`; clientes servidor a servidor pueden omitirlo. No autorizar comodines. No incluir tokens en URLs, logs, Issues o repositorios.

Referencias: [autenticación de OpenAI](https://developers.openai.com/plugins/build/auth), [MCP en Codex](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [transporte MCP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports), [vistas PostgreSQL](https://www.postgresql.org/docs/17/sql-createview.html).

## Railway

Preparación en `tools/data-gateway/railway.env.example`. Servicio existente: `SenorArroz`; no crear otro servicio.

1. Mantener `DataGateway__Enabled=false`.
2. Configurar `ConnectionStrings__DataGatewayReader` con la credencial existente del lector y el host interno de MainDatabase. No usar `DefaultConnection`, no crear/rotar la contraseña. `DATABASE_READ_URL` de GitHub Actions no es recuperable mediante la API de secrets; el operador debe instalar la misma credencial en Railway sin publicarla.
3. Configurar Authority del proveedor OAuth, Resource canónico HTTPS y sujetos autorizados. Vincular tenant y scope en el proveedor, con consentimiento del usuario; no aceptar tenant desde parámetros del cliente.
4. Configurar `DataGateway__AllowedViews__N` solo para vistas revisadas y ya instaladas. Inicialmente `diagnostics.orders_summary`.
5. Aplicar la migración únicamente mediante importación, revisión, SHA, respaldo restaurado y autorización explícita del workflow existente. El merge no aplica SQL.
6. Comprobar con el lector real: columnas excluidas, RLS entre tenants, credencial sin escritura, protocolo MCP y expiración. Activar solamente después de documentar resultados y aprobación. Apagado inmediato: `DataGateway__Enabled=false`.

## ChatGPT, Codex y Code Mode

ChatGPT: registrar la URL HTTPS `/api/internal/data-gateway/mcp` como app/conector privado y configurar el cliente OAuth en el proveedor. Completar inicio de sesión y consentimiento. Probar `get_schema`, `execute_query` y `explain_query`; herramientas declaradas de lectura, idempotentes y con `structuredContent`. La instalación/consentimiento de una cuenta ChatGPT requiere intervención del propietario y no se realiza automáticamente al desplegar.

Codex HTTP: en su configuración MCP indicar `url` con ese endpoint y ejecutar `codex mcp login senor_arroz` cuando se use OAuth. Alternativamente `bearer_token_env_var="SENOR_ARROZ_GATEWAY_TOKEN"` para un JWT administrativo vigente; no escribir el token en TOML. Codex stdio: ejecutar `python` con ruta absoluta a `tools/data-gateway/mcp_stdio.py`, pasando `SENOR_ARROZ_GATEWAY_URL` y `SENOR_ARROZ_GATEWAY_TOKEN` por entorno. El puente reenvía el protocolo HTTP y reutiliza los esquemas del servidor. No sigue redirecciones ni imprime credenciales.

SDK TypeScript: `DataGateway` consume REST con token suministrado por el host, timeout y respuestas limitadas, sin redirecciones. En un sandbox Code Mode usar `codeModeGateway(invoke)`: el host proporciona una función que invoca las tres herramientas MCP y conserva las credenciales fuera del código del agente. No exponer variables de entorno, conexiones PostgreSQL ni tokens al sandbox. La disponibilidad de Code Mode depende del cliente; el gateway no habilita esa función en una cuenta.

## Validación

CI ejecuta .NET, SDK TypeScript y stdio. Las pruebas HTTP verifican firmas, expiración, audiencia, scope, sujeto, rol y tenant; las pruebas PostgreSQL ejecutan dos veces la migración, suplantación de GUC, columnas privadas, SQL malicioso, escrituras, filtros de fecha, decimales, límites de filas y un umbral de rendimiento local. `FLOW_POSTGRES_TEST_CONNECTION` debe apuntar a un servidor desechable con permiso de crear bases y roles. Nunca apuntarlo a producción. La prueba crea/elimina una base aislada y el rol lector dentro de ese servidor.

Pendientes operativos y evidencia del respaldo: [tasks/data-gateway-rollout.md](../tasks/data-gateway-rollout.md).

Mejoras posteriores: medir p95 y CPU con carga real antes de ampliar límites; agregar índices compuestos tenant/fecha solo cuando EXPLAIN lo justifique; autorizar nuevas vistas por caso de uso. No cachear datos entre identidades ni compartir el lector con otros tenants.
