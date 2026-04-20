-- Producción: snapshot de apps pendientes por liquidar al cerrar caja (base siguiente período).
-- Ejecutar una vez contra la base existente.

ALTER TABLE cash_register_closure
    ADD COLUMN IF NOT EXISTS pending_app_payments_snapshot character varying(8000) NOT NULL DEFAULT '[]';

COMMENT ON COLUMN cash_register_closure.pending_app_payments_snapshot IS
    'JSON camelCase appId/appName/amount — snapshot de pendiente por liquidar en apps al cerrar';
