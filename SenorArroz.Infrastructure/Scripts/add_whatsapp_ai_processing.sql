BEGIN;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_status varchar(32) NOT NULL DEFAULT 'notapplicable';
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processed_at timestamptz NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_error varchar(1000) NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS sent_by_ai boolean NOT NULL DEFAULT FALSE;
CREATE INDEX IF NOT EXISTS idx_whatsapp_message_ai_processing_status ON whatsapp_message(ai_processing_status);
DO $$ BEGIN ALTER TABLE whatsapp_message ADD CONSTRAINT ck_whatsapp_message_ai_processing_status CHECK (ai_processing_status IN ('notapplicable','pending','processing','completed','ignored','failed','transferredtohuman')); EXCEPTION WHEN duplicate_object THEN NULL; END $$;
-- Históricos permanecen notapplicable. Solo el webhook marca nuevos mensajes entrantes como pending.
COMMIT;
