BEGIN;
SET LOCAL lock_timeout = '5s';
CREATE INDEX IF NOT EXISTS ix_whatsapp_commerce_latest ON whatsapp_commerce_session (tenant_id, conversation_id, id DESC);
CREATE INDEX IF NOT EXISTS ix_whatsapp_message_history ON whatsapp_message (tenant_id, conversation_id, timestamp DESC, id DESC);
CREATE INDEX IF NOT EXISTS ix_whatsapp_message_transfer_reason ON whatsapp_message (tenant_id, conversation_id, timestamp DESC, id DESC)
    WHERE ai_processing_status = 'transferredtohuman' AND ai_processing_error IS NOT NULL;
COMMIT;
