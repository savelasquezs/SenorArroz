BEGIN;

ALTER TABLE customer
    ALTER COLUMN phone1 DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS whatsapp_user_id varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS whatsapp_username varchar(64) NULL;

ALTER TABLE whatsapp_conversation
    ALTER COLUMN phone_number DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS whatsapp_user_id varchar(256) NULL,
    ADD COLUMN IF NOT EXISTS whatsapp_username varchar(64) NULL;

ALTER TABLE whatsapp_conversation
    DROP CONSTRAINT IF EXISTS uq_whatsapp_conversation_branch_phone;

DROP INDEX IF EXISTS idx_whatsapp_conversation_branch_phone;

CREATE UNIQUE INDEX IF NOT EXISTS idx_whatsapp_conversation_branch_phone
    ON whatsapp_conversation(branch_id, phone_number)
    WHERE phone_number IS NOT NULL AND phone_number <> '';

CREATE UNIQUE INDEX IF NOT EXISTS uq_whatsapp_conversation_branch_user_id
    ON whatsapp_conversation(branch_id, whatsapp_user_id)
    WHERE whatsapp_user_id IS NOT NULL AND whatsapp_user_id <> '';

CREATE INDEX IF NOT EXISTS idx_whatsapp_conversation_branch_username
    ON whatsapp_conversation(branch_id, whatsapp_username);

CREATE UNIQUE INDEX IF NOT EXISTS uq_customer_branch_whatsapp_user_id
    ON customer(branch_id, whatsapp_user_id)
    WHERE whatsapp_user_id IS NOT NULL AND whatsapp_user_id <> '';

CREATE INDEX IF NOT EXISTS idx_customer_branch_whatsapp_username
    ON customer(branch_id, whatsapp_username);

CREATE INDEX IF NOT EXISTS idx_customer_branch_phone1
    ON customer(branch_id, phone1);

CREATE INDEX IF NOT EXISTS idx_customer_branch_phone2
    ON customer(branch_id, phone2);

DO $$
BEGIN
    ALTER TABLE whatsapp_conversation
        ADD CONSTRAINT ck_whatsapp_conversation_contact_identity
        CHECK (
            NULLIF(BTRIM(phone_number), '') IS NOT NULL
            OR NULLIF(BTRIM(whatsapp_user_id), '') IS NOT NULL
        );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DO $$
BEGIN
    ALTER TABLE customer
        ADD CONSTRAINT ck_customer_contact_identity
        CHECK (
            NULLIF(BTRIM(phone1), '') IS NOT NULL
            OR NULLIF(BTRIM(whatsapp_username), '') IS NOT NULL
            OR NULLIF(BTRIM(whatsapp_user_id), '') IS NOT NULL
        );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

COMMENT ON COLUMN customer.whatsapp_user_id IS
    'Identificador de usuario de WhatsApp con alcance de negocio (BSUID). Administrado por el backend.';
COMMENT ON COLUMN customer.whatsapp_username IS
    'Username visible de WhatsApp normalizado en minusculas y con prefijo @.';
COMMENT ON COLUMN whatsapp_conversation.whatsapp_user_id IS
    'BSUID estable usado como destinatario cuando Meta oculta el telefono.';
COMMENT ON COLUMN whatsapp_conversation.whatsapp_username IS
    'Username visible recibido en contacts.profile.username.';

COMMIT;
