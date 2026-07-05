CREATE TABLE IF NOT EXISTS whatsapp_branch_setting (
    id serial PRIMARY KEY,
    branch_id integer NOT NULL UNIQUE REFERENCES branch(id) ON DELETE CASCADE,
    phone_number_id varchar(64) NOT NULL,
    business_account_id varchar(64) NOT NULL,
    display_phone_number varchar(32) NOT NULL,
    access_token text NOT NULL,
    webhook_verify_token varchar(255) NOT NULL,
    app_secret varchar(255) NULL,
    is_active boolean NOT NULL DEFAULT false,
    is_verified boolean NOT NULL DEFAULT false,
    last_verified_at timestamp without time zone NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);

CREATE TABLE IF NOT EXISTS whatsapp_conversation (
    id serial PRIMARY KEY,
    branch_id integer NOT NULL REFERENCES branch(id) ON DELETE CASCADE,
    customer_id integer NULL REFERENCES customer(id) ON DELETE SET NULL,
    phone_number varchar(32) NOT NULL,
    contact_name varchar(150) NULL,
    status varchar(20) NOT NULL DEFAULT 'open',
    last_message_at timestamp without time zone NULL,
    last_message_preview varchar(500) NULL,
    unread_count integer NOT NULL DEFAULT 0,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    CONSTRAINT uq_whatsapp_conversation_branch_phone UNIQUE (branch_id, phone_number)
);

CREATE TABLE IF NOT EXISTS whatsapp_message (
    id serial PRIMARY KEY,
    conversation_id integer NOT NULL REFERENCES whatsapp_conversation(id) ON DELETE CASCADE,
    whatsapp_message_id varchar(128) NULL,
    direction varchar(20) NOT NULL,
    type varchar(20) NOT NULL DEFAULT 'text',
    text_body varchar(4096) NOT NULL,
    status varchar(20) NOT NULL,
    sent_by_user_id integer NULL REFERENCES "user"(id) ON DELETE SET NULL,
    timestamp timestamp without time zone NOT NULL,
    raw_payload jsonb NULL,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);

CREATE TABLE IF NOT EXISTS whatsapp_webhook_event (
    id serial PRIMARY KEY,
    event_type varchar(80) NOT NULL,
    whatsapp_message_id varchar(128) NULL,
    raw_payload jsonb NOT NULL,
    processed boolean NOT NULL DEFAULT false,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now()
);

ALTER TABLE customer
    ADD COLUMN IF NOT EXISTS whatsapp_template_opt_in boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS whatsapp_template_authorization_message_id varchar(128) NULL;

CREATE INDEX IF NOT EXISTS idx_whatsapp_branch_setting_branch ON whatsapp_branch_setting(branch_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_branch_setting_phone_number_id ON whatsapp_branch_setting(phone_number_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_branch_setting_verify_token ON whatsapp_branch_setting(webhook_verify_token);
CREATE INDEX IF NOT EXISTS idx_whatsapp_conversation_branch ON whatsapp_conversation(branch_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_conversation_branch_phone ON whatsapp_conversation(branch_id, phone_number);
CREATE INDEX IF NOT EXISTS idx_whatsapp_conversation_last_message_at ON whatsapp_conversation(last_message_at);
CREATE INDEX IF NOT EXISTS idx_whatsapp_message_conversation ON whatsapp_message(conversation_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_message_whatsapp_id ON whatsapp_message(whatsapp_message_id);
CREATE INDEX IF NOT EXISTS idx_whatsapp_message_timestamp ON whatsapp_message(timestamp);
CREATE INDEX IF NOT EXISTS idx_whatsapp_webhook_event_created_at ON whatsapp_webhook_event(created_at);
CREATE INDEX IF NOT EXISTS idx_whatsapp_webhook_event_whatsapp_id ON whatsapp_webhook_event(whatsapp_message_id);
CREATE INDEX IF NOT EXISTS idx_customer_whatsapp_template_opt_in ON customer(whatsapp_template_opt_in);
