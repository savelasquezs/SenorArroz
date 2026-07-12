BEGIN;
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS attention_mode varchar(32) NOT NULL DEFAULT 'ai';
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS assigned_user_id integer NULL;
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS ai_paused_at timestamptz NULL;
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS human_assigned_at timestamptz NULL;
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS closed_at timestamptz NULL;
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS attention_mode_updated_at timestamptz NOT NULL DEFAULT NOW();
ALTER TABLE whatsapp_conversation ADD COLUMN IF NOT EXISTS attention_mode_updated_by_user_id integer NULL;
UPDATE whatsapp_conversation c SET attention_mode = 'human', attention_mode_updated_at = NOW()
WHERE NOT EXISTS (SELECT 1 FROM branch_ai_setting ai WHERE ai.branch_id = c.branch_id AND ai.is_active = TRUE AND ai.is_verified = TRUE);
DO $$ BEGIN ALTER TABLE whatsapp_conversation ADD CONSTRAINT ck_whatsapp_conversation_attention_mode CHECK (attention_mode IN ('ai','human','waitingforhuman','paused','closed')); EXCEPTION WHEN duplicate_object THEN NULL; END $$;
CREATE INDEX IF NOT EXISTS ix_whatsapp_conversation_attention_mode ON whatsapp_conversation(branch_id, attention_mode);
CREATE INDEX IF NOT EXISTS idx_whatsapp_conversation_assigned_user ON whatsapp_conversation(assigned_user_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_conversation_attention_updated_by ON whatsapp_conversation(attention_mode_updated_by_user_id);
DO $$ BEGIN ALTER TABLE whatsapp_conversation ADD CONSTRAINT fk_whatsapp_conversation_assigned_user FOREIGN KEY (assigned_user_id) REFERENCES "user"(id) ON DELETE SET NULL; EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN ALTER TABLE whatsapp_conversation ADD CONSTRAINT fk_whatsapp_conversation_attention_updated_by FOREIGN KEY (attention_mode_updated_by_user_id) REFERENCES "user"(id) ON DELETE SET NULL; EXCEPTION WHEN duplicate_object THEN NULL; END $$;
COMMIT;
