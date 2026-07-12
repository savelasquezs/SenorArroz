BEGIN;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_status varchar(32) NOT NULL DEFAULT 'notapplicable';
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processed_at timestamptz NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_attempts integer NOT NULL DEFAULT 0;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_error varchar(1000) NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS sent_by_ai boolean NOT NULL DEFAULT FALSE;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_processing_started_at timestamptz NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_next_retry_at timestamptz NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_generated_response varchar(4096) NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_response_attempt_id varchar(64) NULL;
ALTER TABLE whatsapp_message ADD COLUMN IF NOT EXISTS ai_response_whatsapp_message_id varchar(128) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_message_ai_response_attempt ON whatsapp_message(ai_response_attempt_id) WHERE ai_response_attempt_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_whatsapp_message_ai_processing_status ON whatsapp_message(ai_processing_status);
ALTER TABLE whatsapp_message DROP CONSTRAINT IF EXISTS ck_whatsapp_message_ai_processing_status;
ALTER TABLE whatsapp_message ADD CONSTRAINT ck_whatsapp_message_ai_processing_status CHECK (ai_processing_status IN ('notapplicable','pending','processing','responsegenerated','sending','sent','completed','ignored','failed','transferredtohuman'));
-- Históricos permanecen notapplicable. Solo el webhook marca nuevos mensajes entrantes como pending.
COMMIT;
