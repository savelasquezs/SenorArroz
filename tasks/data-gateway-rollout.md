# Data Gateway: activación controlada

## Estado

PR backend #116 y asistente #86/#88 fusionados. Importación #84 corregida y reemplazada por #87. Migración del lector aplicada en producción mediante solicitud #89 y Actions 42862104302, con autorización del propietario del 10 de noviembre. Validación de lectura #92: rol existente, 38 vistas, 37 guards, acceso a columnas sensibles falso y escrituras falso.

Credencial existente instalada en Railway mediante handoff cifrado RSA-OAEP SHA-256 (PR #91; run 42863497538); no se imprimió ni regeneró. Gateway apagado durante preparación. El nuevo consentimiento OAuth usa la API y usuarios actuales, sin modificar sesiones POS. Las claves nuevas de OAuth son independientes de todas las credenciales existentes.

PR OAuth backend #117 fusionado (ec06ca48); CI 42867657670: 1747 aprobadas, 1 omitida; SDK 4 y stdio 7. PR SQL #94 fusionado; solicitud #95 / Actions 42868402601 aplicada con SHA 6df50d5e96d010ad64ff33cad29ebdfd0786164d26c2cd1713743c893c21b04a. Solicitud #96 confirma cuatro tablas con FORCE RLS, cuatro guards restrictivos y ningún acceso del lector.

Railway: despliegue inicial be2063d4 y activación a4edd193 correctos; metadata RFC 8414/RFC 9728 HTTP 200 y MCP anónimo HTTP 401 con challenge correcto. Sin nuevas credenciales PostgreSQL ni servicios. Configuración global Codex senor_arroz añadida preservando las preferencias existentes; requiere login OAuth. La CLI instalada 0.123.0 necesita el override temporal service_tier=fast para leer su configuración (no cambiar la preferencia priority del escritorio).

Validación del cliente real: corregir metadata para anunciar clientes públicos none; permitir ausencia de resource únicamente asignando la audiencia fija, para la CLI Codex instalada. Pruebas verifican ambos flujos y rechazan resource externo. Completar instalación/consentimiento ChatGPT y comprobar una consulta real antes de declarar la cuenta conectada.

## Respaldo

Objeto Railway existente `backup-20261110-080417.dump`, 2026-11-10 08:04:21 UTC, 13 522 480 bytes. SHA-256 `617c67c1ffcb8dc5c65d90207ba0edaae94c2eaf552a421080ca3ced8c09dee2`.

Restaurado íntegramente con PostgreSQL 17 en base local aislada (exit 0, 129 tablas). Se recreó únicamente en el servidor de pruebas el nombre del rol requerido por las políticas del dump; el respaldo usa --no-owner/--no-acl, por lo que no restaura contraseñas ni roles de cluster. Migración aplicada dos veces sobre el clon: 37 guards y 38 vistas, sin error; SELECT de columnas privadas y acceso cross-tenant denegados. 30 consultas de resumen sobre los datos restaurados: promedio 14.19 ms, máximo 16.76 ms (medición local; no predice latencia Railway). Clon eliminado tras verificar. Huella normalizada de migración: `82e6255386cb296fa7f6c6728d97b06175d32d4bba125b0d6071ed044d4ad9eb`.

Un resultado SUCCESS del cron por sí solo no acredita restaurabilidad. La autorización debe revisar antigüedad del backup en el momento de aplicar. La prueba de restauración no constituye autorización de modificación de producción.
