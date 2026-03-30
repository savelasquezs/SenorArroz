-- =============================================================================
-- Colas de impresora Windows por sucursal (branch_print_settings).
-- Idempotente: ADD COLUMN IF NOT EXISTS (PostgreSQL 11+).
-- =============================================================================

ALTER TABLE branch_print_settings
    ADD COLUMN IF NOT EXISTS printer_queue_kitchen character varying(128);

ALTER TABLE branch_print_settings
    ADD COLUMN IF NOT EXISTS printer_queue_delivery character varying(128);

ALTER TABLE branch_print_settings
    ADD COLUMN IF NOT EXISTS printer_queue_cashier character varying(128);
