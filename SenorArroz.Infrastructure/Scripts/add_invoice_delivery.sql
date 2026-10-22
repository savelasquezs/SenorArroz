BEGIN;

ALTER TABLE electronic_invoice_document ADD COLUMN IF NOT EXISTS provider_pdf bytea;
ALTER TABLE electronic_invoice_document ADD COLUMN IF NOT EXISTS pdf_version integer NOT NULL DEFAULT 0;
ALTER TABLE electronic_invoice_document ADD COLUMN IF NOT EXISTS email_channel text;
ALTER TABLE electronic_invoice_document ADD COLUMN IF NOT EXISTS email_error text;
ALTER TABLE electronic_invoice_document ADD COLUMN IF NOT EXISTS email_sent_at timestamptz;
UPDATE electronic_invoice_document SET email_channel = CASE WHEN email_status = 'accepted' THEN 'plemsi' ELSE 'application' END WHERE email_channel IS NULL;
ALTER TABLE electronic_invoice_document ALTER COLUMN email_channel SET DEFAULT 'application';
ALTER TABLE electronic_invoice_document ALTER COLUMN email_channel SET NOT NULL;

ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS deduplication_key varchar(256);
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS first_attempted_at timestamptz;
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS lease_until timestamptz;
ALTER TABLE email_outbox_message ADD COLUMN IF NOT EXISTS provider_message_id varchar(256);
CREATE UNIQUE INDEX IF NOT EXISTS ux_email_outbox_message_tenant_deduplication
    ON email_outbox_message(tenant_id, deduplication_key) WHERE deduplication_key IS NOT NULL;

ALTER TABLE email_outbox_message ENABLE ROW LEVEL SECURITY;
ALTER TABLE email_outbox_message FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS invoice_delivery_tenant ON email_outbox_message;
CREATE POLICY invoice_delivery_tenant ON email_outbox_message
    USING (app.is_system_scope() OR tenant_id = app.current_tenant_id())
    WITH CHECK (app.is_system_scope() OR tenant_id = app.current_tenant_id());

CREATE OR REPLACE FUNCTION protect_invoice_email_tenant() RETURNS trigger LANGUAGE plpgsql AS $guard$
BEGIN
 IF NEW.message_type = 'electronic_invoice' AND
    (NEW.related_entity_type IS DISTINCT FROM 'electronic_invoice' OR NEW.related_entity_id IS NULL OR
      NOT EXISTS (SELECT 1 FROM electronic_invoice_document WHERE id = NEW.related_entity_id AND tenant_id = NEW.tenant_id)) THEN
   RAISE EXCEPTION 'El correo fiscal debe pertenecer al mismo tenant del documento.' USING ERRCODE = '23514';
 END IF;
 RETURN NEW;
END $guard$;
DROP TRIGGER IF EXISTS protect_invoice_email_tenant ON email_outbox_message;
CREATE TRIGGER protect_invoice_email_tenant BEFORE INSERT OR UPDATE ON email_outbox_message
    FOR EACH ROW EXECUTE FUNCTION protect_invoice_email_tenant();

COMMIT;
