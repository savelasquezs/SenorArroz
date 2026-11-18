BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL app.system_scope = 'on';
ALTER TABLE whatsapp_branch_setting
    ADD COLUMN IF NOT EXISTS coexistence_enabled boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS connection_state varchar(32) NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS connection_error varchar(500),
    ADD COLUMN IF NOT EXISTS coexistence_connected_at timestamptz,
    ADD COLUMN IF NOT EXISTS history_sync_status varchar(32) NOT NULL DEFAULT 'not_started',
    ADD COLUMN IF NOT EXISTS contacts_sync_status varchar(32) NOT NULL DEFAULT 'not_started',
    ADD COLUMN IF NOT EXISTS history_sync_request_id varchar(128),
    ADD COLUMN IF NOT EXISTS contacts_sync_request_id varchar(128),
    ADD COLUMN IF NOT EXISTS last_sync_at timestamptz;
ALTER TABLE whatsapp_conversation
    ADD COLUMN IF NOT EXISTS last_customer_message_at timestamptz,
    ADD COLUMN IF NOT EXISTS attention_revision bigint NOT NULL DEFAULT 0;
DO $$ DECLARE needs_backfill boolean; BEGIN
    SELECT NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'whatsapp_conversation' AND column_name = 'needs_human_response') INTO needs_backfill;
    ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS needs_human_response boolean NOT NULL DEFAULT false;
    IF needs_backfill THEN
        UPDATE whatsapp_conversation SET needs_human_response = true
        WHERE unread_count > 0 AND attention_mode IN ('human', 'waitingforhuman', 'paused');
    END IF;
END $$;
ALTER TABLE whatsapp_message
    ADD COLUMN IF NOT EXISTS source varchar(32) NOT NULL DEFAULT 'api',
    ADD COLUMN IF NOT EXISTS is_historical boolean NOT NULL DEFAULT false;
UPDATE whatsapp_conversation c SET last_customer_message_at = m.last_at
FROM (SELECT tenant_id, conversation_id, max(timestamp) last_at FROM whatsapp_message
      WHERE direction = 'inbound' AND NOT is_historical GROUP BY tenant_id, conversation_id) m
WHERE c.id = m.conversation_id AND c.tenant_id = m.tenant_id AND c.last_customer_message_at IS NULL;
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM whatsapp_branch_setting WHERE phone_number_id <> '' GROUP BY phone_number_id HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate WhatsApp lines: reconcile before applying this script';
    END IF;
    IF EXISTS (SELECT 1 FROM whatsapp_message WHERE whatsapp_message_id IS NOT NULL AND whatsapp_message_id <> ''
               GROUP BY tenant_id, conversation_id, whatsapp_message_id HAVING count(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate Meta message IDs in a conversation: reconcile before applying this script';
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_branch_setting_line ON whatsapp_branch_setting (phone_number_id) WHERE phone_number_id <> '';
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_message_conversation_meta_id
    ON whatsapp_message (tenant_id, conversation_id, whatsapp_message_id) WHERE whatsapp_message_id IS NOT NULL AND whatsapp_message_id <> '';
CREATE INDEX IF NOT EXISTS ix_whatsapp_conversation_inbox
    ON whatsapp_conversation (tenant_id, assigned_user_id, attention_mode, last_message_at DESC, id DESC);
COMMIT;
