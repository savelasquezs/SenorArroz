BEGIN;
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS delivery_status varchar(40);
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS delivery_event_at timestamptz;
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS delivery_event_id varchar(256);
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS delivery_error varchar(2000);
CREATE INDEX IF NOT EXISTS ix_email_outbox_provider_message
    ON email_outbox_message(provider_message_id) WHERE provider_message_id IS NOT NULL;
COMMIT;
