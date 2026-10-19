BEGIN;
ALTER TABLE whatsapp_conversation
    ADD COLUMN IF NOT EXISTS ai_order_state jsonb,
    ADD COLUMN IF NOT EXISTS ai_order_state_updated_at timestamptz;
ALTER TABLE whatsapp_ai_invocation
    ADD COLUMN IF NOT EXISTS context_strategy varchar(32) NOT NULL DEFAULT 'simple_v1',
    ADD COLUMN IF NOT EXISTS context_message_count integer,
    ADD COLUMN IF NOT EXISTS tool_definition_count integer,
    ADD COLUMN IF NOT EXISTS system_prompt_characters integer,
    ADD COLUMN IF NOT EXISTS runtime_context_characters integer,
    ADD COLUMN IF NOT EXISTS history_characters integer,
    ADD COLUMN IF NOT EXISTS tool_definitions_characters integer,
    ADD COLUMN IF NOT EXISTS context_planner_fallback boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS context_planner_fallback_reason varchar(300);
COMMIT;
