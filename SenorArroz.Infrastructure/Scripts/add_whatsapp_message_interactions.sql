BEGIN;

ALTER TABLE whatsapp_message
    ADD COLUMN IF NOT EXISTS hidden_at timestamp without time zone,
    ADD COLUMN IF NOT EXISTS hidden_by_user_id integer,
    ADD COLUMN IF NOT EXISTS customer_reaction varchar(32),
    ADD COLUMN IF NOT EXISTS customer_reaction_at timestamp without time zone,
    ADD COLUMN IF NOT EXISTS business_reaction varchar(32),
    ADD COLUMN IF NOT EXISTS business_reaction_at timestamp without time zone;

COMMIT;
