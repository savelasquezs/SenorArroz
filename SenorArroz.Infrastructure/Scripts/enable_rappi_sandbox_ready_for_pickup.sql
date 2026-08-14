BEGIN;

UPDATE delivery_app_store s
SET manual_ready_for_pickup_enabled = true,
    updated_at = now()
FROM delivery_app_connection c
WHERE c.id = s.connection_id
  AND c.provider = 'rappi'
  AND c.environment = 'sandbox'
  AND s.rappi_store_id IN ('900173116', '900173117')
  AND NOT s.manual_ready_for_pickup_enabled;

COMMIT;
