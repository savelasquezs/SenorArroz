ALTER TABLE branch_print_settings
    ADD COLUMN IF NOT EXISTS kitchen_auto_print_trigger character varying(30);

UPDATE branch_print_settings
SET kitchen_auto_print_trigger = 'when_marked_ready'
WHERE kitchen_auto_print_trigger IS NULL;

ALTER TABLE branch_print_settings
    ALTER COLUMN kitchen_auto_print_trigger SET DEFAULT 'when_marked_ready',
    ALTER COLUMN kitchen_auto_print_trigger SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'CK_branch_print_settings_kitchen_auto_print_trigger'
    ) THEN
        ALTER TABLE branch_print_settings
            ADD CONSTRAINT "CK_branch_print_settings_kitchen_auto_print_trigger"
            CHECK (kitchen_auto_print_trigger IN ('when_marked_ready', 'when_order_created'));
    END IF;
END $$;

ALTER TABLE print_job ADD COLUMN IF NOT EXISTS automatic_order_id integer;
ALTER TABLE print_job ADD COLUMN IF NOT EXISTS automatic_trigger character varying(30);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'CK_print_job_automatic_trigger'
    ) THEN
        ALTER TABLE print_job
            ADD CONSTRAINT "CK_print_job_automatic_trigger"
            CHECK (automatic_trigger IS NULL OR automatic_trigger IN ('when_marked_ready', 'when_order_created'));
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_print_job_automatic_event
    ON print_job (branch_id, kind, automatic_order_id, automatic_trigger)
    WHERE automatic_order_id IS NOT NULL AND automatic_trigger IS NOT NULL;
