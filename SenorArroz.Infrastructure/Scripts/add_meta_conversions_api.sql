BEGIN;

ALTER TABLE storefront_checkout
    ADD COLUMN IF NOT EXISTS meta_consent_granted boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS meta_client_user_agent varchar(512),
    ADD COLUMN IF NOT EXISTS meta_client_ip_address varchar(64),
    ADD COLUMN IF NOT EXISTS meta_fbp varchar(255),
    ADD COLUMN IF NOT EXISTS meta_fbc varchar(255);

ALTER TABLE payment_notification_outbox
    ADD COLUMN IF NOT EXISTS meta_status varchar(20) NOT NULL DEFAULT 'ignored',
    ADD COLUMN IF NOT EXISTS meta_attempt_count integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS meta_next_attempt_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS meta_processed_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS meta_last_error varchar(1000),
    ADD COLUMN IF NOT EXISTS meta_consent_granted boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS meta_client_user_agent varchar(512),
    ADD COLUMN IF NOT EXISTS meta_client_ip_address varchar(64),
    ADD COLUMN IF NOT EXISTS meta_fbp varchar(255),
    ADD COLUMN IF NOT EXISTS meta_fbc varchar(255);

CREATE INDEX IF NOT EXISTS ix_payment_notification_outbox_meta_pending
    ON payment_notification_outbox (meta_status, meta_next_attempt_at);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_payment_notification_outbox_meta_status'
    ) THEN
        ALTER TABLE payment_notification_outbox
            ADD CONSTRAINT ck_payment_notification_outbox_meta_status
            CHECK (meta_status IN ('pending', 'processed', 'failed', 'ignored'));
    END IF;
END $$;

COMMIT;
