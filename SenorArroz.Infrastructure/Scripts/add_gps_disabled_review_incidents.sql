BEGIN;

ALTER TABLE delivery_tracking_incident
    ADD COLUMN IF NOT EXISTS source_device_event_id bigint;

ALTER TABLE delivery_tracking_incident
    ALTER COLUMN center_latitude DROP NOT NULL,
    ALTER COLUMN center_longitude DROP NOT NULL;

ALTER TABLE delivery_tracking_incident
    DROP CONSTRAINT IF EXISTS ck_delivery_tracking_incident_type;
ALTER TABLE delivery_tracking_incident
    ADD CONSTRAINT ck_delivery_tracking_incident_type
    CHECK (incident_type IN ('stay', 'route_deviation', 'location_disabled'));

CREATE UNIQUE INDEX IF NOT EXISTS uq_delivery_tracking_incident_device_event
    ON delivery_tracking_incident(source_device_event_id)
    WHERE source_device_event_id IS NOT NULL;

COMMIT;
