BEGIN;

UPDATE delivery_app_store s
SET store_integration_id = s.rappi_store_id,
    updated_at = now()
FROM delivery_app_connection c
WHERE c.id = s.connection_id
  AND c.provider = 'rappi'
  AND c.environment = 'sandbox'
  AND s.rappi_store_id IN ('900173116', '900173117')
  AND s.store_integration_id IS DISTINCT FROM s.rappi_store_id;

COMMIT;
