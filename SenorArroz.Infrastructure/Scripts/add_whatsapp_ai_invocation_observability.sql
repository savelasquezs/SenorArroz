CREATE TABLE IF NOT EXISTS whatsapp_ai_invocation (
    id bigserial PRIMARY KEY,
    branch_id integer NOT NULL REFERENCES branch(id) ON DELETE CASCADE,
    conversation_id integer NOT NULL REFERENCES whatsapp_conversation(id) ON DELETE CASCADE,
    incoming_message_id integer NOT NULL REFERENCES whatsapp_message(id) ON DELETE CASCADE,
    provider varchar(40) NOT NULL,
    model varchar(120) NOT NULL,
    invocation_index integer NOT NULL,
    attempt_index integer NOT NULL,
    started_at timestamp without time zone NOT NULL,
    completed_at timestamp without time zone NULL,
    duration_ms bigint NULL,
    input_tokens integer NULL,
    cached_input_tokens integer NULL,
    output_tokens integer NULL,
    thinking_tokens integer NULL,
    billable_output_tokens integer NULL,
    tool_call_count integer NOT NULL DEFAULT 0,
    finish_reason varchar(80) NULL,
    success boolean NOT NULL,
    is_transient_error boolean NOT NULL,
    http_status_code integer NULL,
    error_category varchar(80) NULL,
    error_message varchar(500) NULL,
    input_price_per_million_usd numeric(18,8) NULL,
    cached_input_price_per_million_usd numeric(18,8) NULL,
    output_price_per_million_usd numeric(18,8) NULL,
    estimated_cost_usd numeric(18,10) NULL,
    pricing_effective_date timestamp without time zone NULL,
    created_at timestamp without time zone NOT NULL DEFAULT NOW()
);

ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS billable_output_tokens integer NULL;
ALTER TABLE whatsapp_ai_invocation ADD COLUMN IF NOT EXISTS pricing_effective_date timestamp without time zone NULL;

CREATE INDEX IF NOT EXISTS idx_whatsapp_ai_invocation_branch_created
    ON whatsapp_ai_invocation(branch_id, created_at);
CREATE INDEX IF NOT EXISTS idx_whatsapp_ai_invocation_provider_model_created
    ON whatsapp_ai_invocation(provider, model, created_at);
CREATE INDEX IF NOT EXISTS idx_whatsapp_ai_invocation_message
    ON whatsapp_ai_invocation(incoming_message_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_ai_invocation_conversation
    ON whatsapp_ai_invocation(conversation_id);
