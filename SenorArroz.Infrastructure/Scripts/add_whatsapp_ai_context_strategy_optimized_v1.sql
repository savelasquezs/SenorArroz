ALTER TABLE branch_ai_setting
    ADD COLUMN IF NOT EXISTS context_strategy varchar(32) NOT NULL DEFAULT 'legacy';

UPDATE branch_ai_setting SET context_strategy = 'legacy'
WHERE context_strategy IS NULL OR context_strategy NOT IN ('legacy', 'optimized_v1');

ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS context_strategy varchar(32) NOT NULL DEFAULT 'legacy';
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS context_message_count integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS tool_definition_count integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS system_prompt_characters integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS runtime_context_characters integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS history_characters integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS tool_definitions_characters integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS context_planner_fallback boolean NOT NULL DEFAULT false;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS context_planner_fallback_reason varchar(300) NULL;

CREATE INDEX IF NOT EXISTS idx_whatsapp_ai_invocation_strategy_created
    ON whatsapp_ai_invocation(context_strategy, created_at);
