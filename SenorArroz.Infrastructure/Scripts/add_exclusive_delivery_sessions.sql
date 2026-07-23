-- Sesión exclusiva por domiciliario.
-- Idempotente: se puede ejecutar antes de desplegar el backend actualizado.
-- Railway (desde Bash y la raíz de SenorArroz):
--   railway connect MainDatabase
-- Dentro de psql:
--   \i SenorArroz.Infrastructure/Scripts/add_exclusive_delivery_sessions.sql

ALTER TABLE "user"
    ADD COLUMN IF NOT EXISTS active_session_id uuid;

ALTER TABLE refresh_token
    ADD COLUMN IF NOT EXISTS session_id uuid;

CREATE INDEX IF NOT EXISTS idx_refresh_token_session_id
    ON refresh_token (session_id);

COMMENT ON COLUMN "user".active_session_id IS
    'Identificador de la única sesión autenticada vigente para un domiciliario.';

COMMENT ON COLUMN refresh_token.session_id IS
    'Sesión autenticada a la que pertenece el refresh token.';
