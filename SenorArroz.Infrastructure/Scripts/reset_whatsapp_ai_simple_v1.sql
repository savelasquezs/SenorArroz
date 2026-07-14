BEGIN;

DROP TABLE IF EXISTS whatsapp_order_confirmation CASCADE;
DROP TABLE IF EXISTS whatsapp_order_draft_item CASCADE;
DROP TABLE IF EXISTS whatsapp_order_draft CASCADE;

ALTER TABLE branch_ai_setting
    DROP COLUMN IF EXISTS context_strategy;

ALTER TABLE whatsapp_conversation
    ADD COLUMN IF NOT EXISTS ai_order_state jsonb,
    ADD COLUMN IF NOT EXISTS ai_order_state_updated_at timestamptz;

ALTER TABLE whatsapp_conversation
    DROP CONSTRAINT IF EXISTS ck_whatsapp_conversation_ai_order_state_object;

ALTER TABLE whatsapp_conversation
    ADD CONSTRAINT ck_whatsapp_conversation_ai_order_state_object
    CHECK (ai_order_state IS NULL OR jsonb_typeof(ai_order_state) = 'object');

UPDATE whatsapp_conversation
SET ai_order_state = NULL,
    ai_order_state_updated_at = NULL
WHERE ai_order_state IS NOT NULL OR ai_order_state_updated_at IS NOT NULL;

COMMIT;
