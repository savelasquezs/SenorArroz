BEGIN;

SET LOCAL lock_timeout = '5s';

ALTER TABLE "user"
    ADD COLUMN IF NOT EXISTS active_delivery_session_id uuid;

COMMENT ON COLUMN "user".active_session_id IS
    'Sesión del panel para Admin/Superadmin; sesión global para los demás roles.';

COMMENT ON COLUMN "user".active_delivery_session_id IS
    'Sesión exclusiva de delivery para Admin/Superadmin con acceso habilitado.';

COMMIT;
