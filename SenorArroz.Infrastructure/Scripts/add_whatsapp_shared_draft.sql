BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL app.system_scope = 'on';
ALTER TABLE whatsapp_commerce_session
    ALTER COLUMN channel_setting_id DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS draft_json jsonb NOT NULL DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS editor_json jsonb,
    ADD COLUMN IF NOT EXISTS draft_source varchar(20) NOT NULL DEFAULT 'flow',
    ADD COLUMN IF NOT EXISTS commercial_status varchar(32) NOT NULL DEFAULT 'in_progress',
    ADD COLUMN IF NOT EXISTS order_id integer REFERENCES "order"(id),
    ADD COLUMN IF NOT EXISTS checkout_id varchar(100),
    ADD COLUMN IF NOT EXISTS internal_notes_json jsonb NOT NULL DEFAULT '[]';
ALTER TABLE whatsapp_commerce_outbox ALTER COLUMN channel_setting_id DROP NOT NULL;
UPDATE whatsapp_commerce_session s
SET order_id = o.id, commercial_status = 'order_taken'
FROM "order" o
WHERE o.tenant_id = s.tenant_id AND o.storefront_idempotency_key = s.idempotency_key
    AND s.order_id IS NULL;
UPDATE whatsapp_commerce_session s
SET checkout_id = c.public_id::text,
    commercial_status = CASE WHEN c.status = 'review_required' OR c.expires_at > NOW()
        THEN 'waiting_customer' ELSE 'no_sale' END
FROM storefront_checkout c
WHERE c.tenant_id = s.tenant_id AND c.idempotency_key = s.idempotency_key
    AND s.order_id IS NULL AND s.checkout_id IS NULL;
UPDATE whatsapp_commerce_session SET commercial_status = 'no_sale'
WHERE status IN ('cancelled', 'expired') AND order_id IS NULL AND checkout_id IS NULL;
COMMIT;
