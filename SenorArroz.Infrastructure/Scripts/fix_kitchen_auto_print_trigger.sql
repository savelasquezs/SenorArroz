BEGIN;

ALTER TABLE public.branch_print_settings
ADD COLUMN IF NOT EXISTS kitchen_auto_print_trigger character varying(32)
NOT NULL DEFAULT 'whenMarkedReady';

ALTER TABLE public.branch_print_settings
DROP CONSTRAINT IF EXISTS "CK_branch_print_settings_kitchen_auto_print_trigger";

ALTER TABLE public.branch_print_settings
ALTER COLUMN kitchen_auto_print_trigger TYPE character varying(32),
ALTER COLUMN kitchen_auto_print_trigger SET DEFAULT 'whenMarkedReady';

UPDATE public.branch_print_settings
SET kitchen_auto_print_trigger = CASE kitchen_auto_print_trigger
    WHEN 'when_marked_ready' THEN 'whenMarkedReady'
    WHEN 'when_order_created' THEN 'whenOrderCreated'
    ELSE kitchen_auto_print_trigger
END;

ALTER TABLE public.branch_print_settings
ADD CONSTRAINT "CK_branch_print_settings_kitchen_auto_print_trigger"
CHECK (kitchen_auto_print_trigger IN ('whenMarkedReady', 'whenOrderCreated'));

COMMIT;
