BEGIN;

DROP INDEX IF EXISTS uq_delivery_device_event_client_id;
CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_device_event_client_id
    ON delivery_device_event (tenant_id, client_event_id) WHERE client_event_id IS NOT NULL;

DROP INDEX IF EXISTS uq_dloc_client_point_id;
CREATE UNIQUE INDEX IF NOT EXISTS uq_dloc_client_point_id
    ON deliveryman_location (tenant_id, client_point_id) WHERE client_point_id IS NOT NULL;

DROP INDEX IF EXISTS uq_delivery_tracking_alert_dedup;
CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_tracking_alert_dedup
    ON delivery_tracking_alert (tenant_id, deduplication_key);

DROP INDEX IF EXISTS ux_whatsapp_message_agent_dispatch_key;
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_message_agent_dispatch_key
    ON whatsapp_message (tenant_id, agent_dispatch_key) WHERE agent_dispatch_key IS NOT NULL;

DROP INDEX IF EXISTS ux_whatsapp_commerce_outbox_event;
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_commerce_outbox_event
    ON whatsapp_commerce_outbox (tenant_id, event_key);

DROP INDEX IF EXISTS ux_whatsapp_commerce_event_key;
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_commerce_event_key
    ON whatsapp_commerce_event (tenant_id, event_key);

DROP INDEX IF EXISTS ux_storefront_checkout_idempotency_key;
CREATE UNIQUE INDEX IF NOT EXISTS ux_storefront_checkout_idempotency_key
    ON storefront_checkout (tenant_id, idempotency_key);

DROP INDEX IF EXISTS uq_user_device_token_token;
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_device_token_token
    ON user_device_token (tenant_id, token);

COMMIT;
