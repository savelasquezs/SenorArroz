BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL app.system_scope = 'on';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'whatsapp_quick_reply' AND column_name = 'reply_mode'
    ) THEN
        ALTER TABLE public.whatsapp_quick_reply
            ADD COLUMN reply_mode varchar(24) NOT NULL DEFAULT 'send'
            CHECK (reply_mode IN ('send', 'draft', 'chicharron_quote'));

        UPDATE public.whatsapp_quick_reply SET reply_mode = 'chicharron_quote' WHERE shortcut = 'conchi';
    END IF;
END $$;

COMMIT;
