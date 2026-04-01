-- Configura el token del agente de impresión LOCAL a partir del token en claro
-- (el mismo que pegas en appsettings del print agent).
--
-- EN LA BD NO SE GUARDA EL TOKEN EN CLARO. Solo:
--   agent_token_salt  = cadena hex (16 bytes aleatorios, 32 caracteres)
--   agent_token_hash  = SHA-256 en hex minúsculas de UTF-8(salt || token_plano)
--
-- Opción sin este script: en producción ejecuta
--   SELECT branch_id, agent_token_salt, agent_token_hash FROM branch_print_settings WHERE branch_id = ?;
-- y en desarrollo haz UPDATE con esos dos valores (misma sucursal lógica).
--
-- Requiere extensión pgcrypto (habitual en Postgres).
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- AJUSTA: id de sucursal en tu base de DESARROLLO
-- Pega el token en claro entre comillas simples (escapa comillas simples duplicándolas).
UPDATE branch_print_settings AS s
SET
    agent_token_salt = v.salt,
    agent_token_hash = lower(encode(digest(convert_to(v.salt || v.plain_token, 'UTF8'), 'sha256'), 'hex')),
    agent_token_updated_at = (NOW() AT TIME ZONE 'utc'),
    updated_at = NOW()
FROM (
    SELECT
        encode(gen_random_bytes(16), 'hex') AS salt,
        'TlMethwLHeI0YVdGVuxe5BmWXnPkT8R9_QHKxeWZ-XE' AS plain_token
) AS v
WHERE s.branch_id = 1;

-- Si no actualizó ninguna fila, esa sucursal no tiene fila en branch_print_settings
-- (poco habitual si ya usas impresión). Crea la fila vía app o local-init.
