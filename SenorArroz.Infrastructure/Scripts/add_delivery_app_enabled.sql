BEGIN;

-- Permiso adicional para Admin/Cashier, sin modificar el rol principal.
ALTER TABLE public."user"
    ADD COLUMN IF NOT EXISTS delivery_app_enabled boolean NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN public."user".delivery_app_enabled IS
    'Permite a administradores y cajeros operar como domiciliarios desde la app Flutter.';

COMMIT;
